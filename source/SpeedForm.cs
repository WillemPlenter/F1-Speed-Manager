using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal sealed class SpeedForm : Form
{
    HotkeyManager hotkeys;
    HotkeySettings hotkeySettings;
    bool editingHotkeys;
    readonly ToolTip hints=new ToolTip();
    readonly Button configure=new Button();
    readonly Label multiplier=new Label(),status=new Label(),detail=new Label(),credit=new Label();
    readonly Button[] buttons=new Button[6];
    readonly PictureBox logo=new PictureBox();
    readonly Image appLogo;
    readonly CheckBox overlayToggle=new CheckBox();
    readonly ComboBox overlayPosition=new ComboBox();
    readonly SpeedOverlay overlay=new SpeedOverlay();
    readonly System.Windows.Forms.Timer overlayTimer=new System.Windows.Forms.Timer();
    volatile OverlaySnapshot overlaySnapshot;
    readonly Icon appIcon;
    readonly int[] rates={1,2,3,5,10,1};
    readonly ManualResetEvent shutdown=new ManualResetEvent(false),workerDone=new ManualResetEvent(false);
    readonly System.Windows.Forms.Timer closeTimer=new System.Windows.Forms.Timer();
    Thread worker;
    int desired=1,requestSerial;
    volatile bool ready,closing,canClose;
    internal SpeedForm():this(true){}
    // The unconnected constructor is used only by local UI tests, never a CLI mode.
    internal SpeedForm(bool startController)
    {
        hotkeySettings=startController?HotkeySettings.Load():HotkeySettings.Defaults;
        using(var stream=typeof(SpeedForm).Assembly.GetManifestResourceStream("AppIcon.ico"))
        using(var icon=new Icon(stream)){appIcon=(Icon)icon.Clone();}
        Icon=appIcon;
        using(var stream=typeof(SpeedForm).Assembly.GetManifestResourceStream("Logo.png"))
        using(var image=Image.FromStream(stream)){appLogo=new Bitmap(image);}
        Text=AppVersion.WindowTitle;ClientSize=new Size(444,334);
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(25,28,34);ForeColor=Color.White;Font=new Font("Segoe UI",10);
        logo.SetBounds(336,8,88,88);logo.Image=appLogo;logo.SizeMode=PictureBoxSizeMode.Zoom;
        logo.TabStop=false;logo.AccessibleName="F1 Speed Manager logo";Controls.Add(logo);
        multiplier.SetBounds(18,12,310,50);multiplier.Text="1x";multiplier.Font=new Font("Segoe UI",30,FontStyle.Bold);
        status.SetBounds(20,69,312,28);status.Text="Waiting for F1 Manager 2023 / 2024…";
        overlayToggle.SetBounds(20,185,160,32);overlayToggle.Appearance=Appearance.Button;
        overlayToggle.Text="Overlay: Off · F7";overlayToggle.TextAlign=ContentAlignment.MiddleCenter;
        overlayToggle.FlatStyle=FlatStyle.Flat;overlayToggle.BackColor=Color.FromArgb(43,48,59);
        overlayToggle.AccessibleName="In-game overlay";
        overlayPosition.SetBounds(195,187,230,28);overlayPosition.DropDownStyle=ComboBoxStyle.DropDownList;
        overlayPosition.Items.AddRange(new object[]{"Top right","Top left","Bottom right","Bottom left","Top center"});
        overlayPosition.SelectedIndex=0;overlayPosition.AccessibleName="Overlay position";
        overlayToggle.CheckedChanged+=(s,e)=>{UpdateHotkeyLabels();overlayToggle.BackColor=overlayToggle.Checked?Color.FromArgb(105,32,40):Color.FromArgb(43,48,59);Program.Log("Overlay "+(overlayToggle.Checked?"enabled":"hidden")+".");RefreshOverlay();};
        overlayPosition.SelectedIndexChanged+=(s,e)=>{Program.Log("Overlay position: "+overlayPosition.Text+".");RefreshOverlay();};
        Controls.Add(overlayToggle);Controls.Add(overlayPosition);
        detail.SetBounds(20,227,405,67);detail.ForeColor=Color.Silver;detail.Font=new Font("Segoe UI",9);
        detail.Text="Extra multiplier × the game's own speed.\nF1–F6: speed. F7: show/hide the in-game overlay.\nClosing restores extra 1x. Game files stay unchanged.";
        configure.SetBounds(20,296,140,26);configure.Text="Hotkeys…";configure.FlatStyle=FlatStyle.Flat;configure.BackColor=Color.FromArgb(43,48,59);
        configure.Click+=(s,e)=>OpenHotkeySettings();
        Controls.Add(configure);
        credit.SetBounds(180,301,245,18);credit.Text="made by SkaffaWilly";
        credit.Font=new Font("Segoe UI",8);credit.ForeColor=Color.FromArgb(153,160,174);credit.TextAlign=ContentAlignment.MiddleRight;
        Controls.Add(multiplier);Controls.Add(status);Controls.Add(detail);Controls.Add(credit);
        string[] labels={"F1  ·  1x","F2  ·  2x","F3  ·  3x","F4  ·  5x","F5  ·  10x","F6  ·  Reset"};
        for(int i=0;i<6;i++)
        {
            int index=i;var button=new Button();buttons[i]=button;
            button.SetBounds(20+(i%3)*137,105+(i/3)*38,130,32);button.Text=labels[i];
            button.FlatStyle=FlatStyle.Flat;button.BackColor=Color.FromArgb(43,48,59);button.Enabled=false;button.AutoEllipsis=true;
            button.Click+=(s,e)=>Request(rates[index]);Controls.Add(button);
        }
        UpdateHotkeyLabels();
        closeTimer.Interval=50;closeTimer.Tick+=(s,e)=>{if(workerDone.WaitOne(0)){closeTimer.Stop();canClose=true;Close();}};
        overlayTimer.Interval=100;overlayTimer.Tick+=(s,e)=>RefreshOverlay();
        Shown+=(s,e)=>{overlayTimer.Start();if(startController)Start();else workerDone.Set();};
    }
    void Start()
    {
        // Reserve every speed/reset key before allowing any game writes.
        try
        {
            hotkeys=CreateHotkeys(hotkeySettings);hotkeys.Start();
            Program.Log("Hotkeys registered: "+hotkeySettings.Serialize().Replace(Environment.NewLine,"; "));
            worker=new Thread(RunController){IsBackground=true,Name="F1 Speed Manager controller"};worker.Start();
        }
        catch(Exception e){Program.Log("ERROR: "+e.Message);status.Text="Hotkey registration failed; no game writes.";detail.Text=e.Message;configure.Enabled=false;Unregister();workerDone.Set();}
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x0312){HotkeyAction action;if(hotkeys!=null && hotkeys.StandardAction(m.WParam.ToInt32(),out action))HandleHotkey(action);return;}
        base.WndProc(ref m);
    }
    HotkeyManager CreateHotkeys(HotkeySettings settings)
    {
        HotkeyManager manager=null;
        manager=new HotkeyManager(Handle,settings,()=>overlaySnapshot,
            action=>QueueUi(()=>{if(hotkeys==manager)HandleHotkey(action);}),message=>QueueUi(()=>{if(hotkeys==manager)StopForHotkeyError(message);}));
        return manager;
    }
    void QueueUi(Action action)
    {try{if(!closing && !IsDisposed && IsHandleCreated)BeginInvoke(action);}catch(InvalidOperationException){}}
    void HandleHotkey(HotkeyAction action)
    {
        if(closing || editingHotkeys)return;
        if((int)action<6){Request(rates[(int)action]);return;}
        if(action==HotkeyAction.ToggleOverlay)overlayToggle.Checked=!overlayToggle.Checked;
        else overlayPosition.SelectedIndex=(overlayPosition.SelectedIndex+(action==HotkeyAction.PreviousPosition?overlayPosition.Items.Count-1:1))%overlayPosition.Items.Count;
    }
    void UpdateHotkeyLabels()
    {
        for(int i=0;i<buttons.Length;i++)if(buttons[i]!=null){buttons[i].Text=hotkeySettings[(HotkeyAction)i].Text+" · "+(i==5?"Reset":rates[i]+"x");hints.SetToolTip(buttons[i],HotkeySettings.Label((HotkeyAction)i)+": "+hotkeySettings[(HotkeyAction)i].Text);}
        string toggle=hotkeySettings[HotkeyAction.ToggleOverlay].Text;
        overlayToggle.Text="Overlay: "+(overlayToggle.Checked?"On":"Off")+(toggle.Length<=5?" · "+toggle:"");hints.SetToolTip(overlayToggle,"Show/hide: "+toggle+". Shared prefix: release alone to toggle.");
        overlay.ToggleHint=toggle;overlay.Invalidate();
        hints.SetToolTip(overlayPosition,"Previous: "+hotkeySettings[HotkeyAction.PreviousPosition].Text+"; next: "+hotkeySettings[HotkeyAction.NextPosition].Text);
        bool defaultPosition=hotkeySettings[HotkeyAction.PreviousPosition].Text=="F7+Up" && hotkeySettings[HotkeyAction.NextPosition].Text=="F7+Down";
        detail.Text="Extra multiplier × the game's own speed.\n"+(defaultPosition?toggle+": overlay. Hold F7 + ↑/↓: change position.":"Hotkeys…: customize speed, overlay and position.")+"\nClosing restores extra 1x. Game files stay unchanged.";
    }
    string ApplyBindings(HotkeySettings candidate)
    {
        HotkeySettings previous=hotkeySettings;Unregister();
        try
        {
            hotkeys=CreateHotkeys(candidate);hotkeys.Start();if(editingHotkeys)Unregister();candidate.Save();hotkeySettings=candidate;UpdateHotkeyLabels();
            Program.Log("Hotkeys saved: "+candidate.Serialize().Replace(Environment.NewLine,"; "));return null;
        }
        catch(Exception e)
        {
            Unregister();Program.Log("Hotkey changes rejected: "+e.Message);
            try{if(!editingHotkeys){hotkeys=CreateHotkeys(previous);hotkeys.Start();}}
            catch(Exception restore){Unregister();StopForHotkeyError("Previous hotkeys could not be restored: "+restore.Message);}
            return "Changes not saved: "+e.Message;
        }
    }
    void OpenHotkeySettings()
    {
        editingHotkeys=true;
        // Release OS reservations and the prefix hook so the focused field receives F keys.
        Unregister();Program.Log("Hotkeys suspended for shortcut recording; extra speed unchanged.");
        try{using(var dialog=new HotkeyDialog(hotkeySettings,ApplyBindings))dialog.ShowDialog(this);}
        finally
        {
            if(!closing && !shutdown.WaitOne(0))
            {
                try{hotkeys=CreateHotkeys(hotkeySettings);hotkeys.Start();Program.Log("Hotkeys resumed after settings.");}
                catch(Exception e){StopForHotkeyError("Hotkeys could not resume: "+e.Message);}
            }
            editingHotkeys=false;
        }
    }
    void StopForHotkeyError(string message)
    {
        Program.Log("ERROR: "+message);ready=false;overlaySnapshot=null;overlay.Hide();shutdown.Set();Unregister();
        foreach(var button in buttons)button.Enabled=false;configure.Enabled=false;status.Text="Stopped — shortcut error";
        detail.Text=message+"\nExtra speed resets to 1x. Close and reopen.";
    }
    void Request(int rate)
    {
        if(!ready || closing)return;
        Interlocked.Exchange(ref desired,rate);Interlocked.Increment(ref requestSerial);
    }
    void RefreshOverlay()
    {
        if(closing || IsDisposed)return;
        try{overlay.RefreshDisplay(overlaySnapshot,overlayToggle.Checked,(OverlayCorner)overlayPosition.SelectedIndex);}
        catch(Exception e){overlay.Hide();Program.Log("Overlay disabled: "+e.Message);overlayToggle.Checked=false;}
    }
    void Display(long active,string text,bool enabled,string extra)
    {
        if(IsDisposed || !IsHandleCreated || closing)return;
        try{BeginInvoke((Action)(()=>{if(closing || enabled && shutdown.WaitOne(0))return;multiplier.Text=active<0?"—":active+"x";status.Text=text;foreach(var b in buttons)b.Enabled=enabled;if(extra!=null)detail.Text=extra;}));}
        catch(InvalidOperationException){}
    }
    void RunController()
    {
        Engine engine=null;long lastActive=-1,lastRequested=-1;int handledSerial=0;bool waitingLogged=false;
        try
        {
            while(!shutdown.WaitOne(0))
            {
                if(engine==null)
                {
                    var candidate=new Engine(s=>{if(!s.StartsWith("Game not running") || !waitingLogged)Program.Log(s);});
                    try
                    {
                        if(!candidate.Discover()){candidate.Dispose();waitingLogged=true;shutdown.WaitOne(1500);continue;}
                        engine=candidate;
                    }
                    catch{candidate.Dispose();throw;}
                    if(shutdown.WaitOne(0))break;
                    engine.Install();engine.Set(1);
                    Interlocked.Exchange(ref desired,1);handledSerial=Volatile.Read(ref requestSerial);ready=true;waitingLogged=false;lastActive=-1;lastRequested=-1;
                }
                if(engine.Game.HasExited)
                {
                    ready=false;overlaySnapshot=null;Program.Log("Game exited; waiting for the next session at extra 1x.");
                    engine.Dispose();engine=null;Display(1,"Game closed — waiting…",false,null);continue;
                }
                int serial=Volatile.Read(ref requestSerial);
                if(serial!=handledSerial){engine.Set(Volatile.Read(ref desired));handledSerial=serial;}
                engine.Heartbeat();long active=engine.Active,requested=engine.Requested;
                overlaySnapshot=new OverlaySnapshot(engine.Game.Id,active,System.Diagnostics.Stopwatch.GetTimestamp());
                if(active!=lastActive || requested!=lastRequested)
                {
                    Program.Log("Active multiplier "+active+"x; requested "+requested+"x.");
                    Display(active,active==requested?"Connected · "+engine.Profile.DisplayName:"Applying "+requested+"x…",true,null);
                    lastActive=active;lastRequested=requested;
                }
                shutdown.WaitOne(100);
            }
        }
        catch(Exception e)
        {
            ready=false;overlaySnapshot=null;var win=e as Win32Exception;
            string message=e.Message+(win==null?"":" (Windows error "+win.NativeErrorCode+")");Program.Log("ERROR: "+message);
            Display(-1,"Stopped — control disabled",false,message+"\nClose and reopen after resolving this error.\nThe watchdog returns to extra 1x if disconnected.");
        }
        finally{ready=false;overlaySnapshot=null;try{if(engine!=null)engine.Dispose();}finally{workerDone.Set();}}
    }
    void Unregister(){if(hotkeys!=null){hotkeys.Dispose();hotkeys=null;}}
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        overlay.Hide();overlayTimer.Stop();
        if(!canClose && !workerDone.WaitOne(0))
        {
            e.Cancel=true;
            if(!closing){closing=true;ready=false;status.Text="Restoring extra 1x before closing…";foreach(var b in buttons)b.Enabled=false;shutdown.Set();Unregister();closeTimer.Start();}
        }
        base.OnFormClosing(e);
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {shutdown.Set();Unregister();closeTimer.Dispose();if(workerDone.WaitOne(0)){shutdown.Dispose();workerDone.Dispose();}base.OnFormClosed(e);}
    protected override void Dispose(bool disposing)
    {if(disposing){Unregister();hints.Dispose();overlayTimer.Dispose();overlay.Dispose();logo.Image=null;if(appLogo!=null)appLogo.Dispose();if(appIcon!=null)appIcon.Dispose();}base.Dispose(disposing);}
}
