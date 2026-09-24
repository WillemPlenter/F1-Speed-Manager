using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal sealed class ThemeToggleButton : CheckBox
{
    internal bool ContrastPaint;
    internal Color ContrastBackColor=F1Theme.Surface;
    internal ThemeToggleButton()
    {SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnPaint(PaintEventArgs e)
    {
        if(!ContrastPaint){base.OnPaint(e);return;}
        using(var background=new SolidBrush(ContrastBackColor))e.Graphics.FillRectangle(background,ClientRectangle);
        ControlPaint.DrawBorder(e.Graphics,ClientRectangle,FlatAppearance.BorderColor,ButtonBorderStyle.Solid);
        Rectangle textBounds=Rectangle.Inflate(ClientRectangle,-2,-2);
        TextRenderer.DrawText(e.Graphics,Text,Font,textBounds,Enabled?ForeColor:F1Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-4,-4));
    }
}

internal sealed class SpeedForm : Form
{
    HotkeyManager hotkeys;
    HotkeySettings hotkeySettings;
    ThemeSettings themeSettings;
    bool editingHotkeys,changingTheme;
    readonly ToolTip hints=new ToolTip();
    readonly Button configure=new Button();
    readonly ComboBox colorMode=new ComboBox();
    readonly Label multiplier=new Label(),status=new Label(),detail=new Label(),credit=new Label();
    readonly Button[] buttons=new Button[6];
    readonly PictureBox logo=new PictureBox();
    readonly Image appLogo;
    readonly ThemeToggleButton overlayToggle=new ThemeToggleButton();
    readonly ComboBox overlayPosition=new ComboBox();
    readonly SpeedOverlay overlay=new SpeedOverlay();
    readonly System.Windows.Forms.Timer overlayTimer=new System.Windows.Forms.Timer();
    volatile OverlaySnapshot overlaySnapshot;
    readonly Icon appIcon;
    readonly int[] rates={1,2,3,5,10,1};
    readonly ManualResetEvent shutdown=new ManualResetEvent(false),workerDone=new ManualResetEvent(false);
    readonly System.Windows.Forms.Timer closeTimer=new System.Windows.Forms.Timer();
    Thread worker;
    Engine currentEngine;
    int desired=1,requestSerial;
    long inputGuardUntil,displayedRate=1;
    volatile bool ready,closing,canClose;
    internal SpeedForm():this(true){}
    // The unconnected constructor is used only by local UI tests, never a CLI mode.
    internal SpeedForm(bool startController)
    {
        hotkeySettings=startController?HotkeySettings.Load():HotkeySettings.Defaults;
        themeSettings=startController?ThemeSettings.Load():ThemeSettings.Defaults;
        using(var stream=typeof(SpeedForm).Assembly.GetManifestResourceStream("AppIcon.ico"))
        using(var icon=new Icon(stream)){appIcon=(Icon)icon.Clone();}
        Icon=appIcon;
        using(var stream=typeof(SpeedForm).Assembly.GetManifestResourceStream("Logo.png"))
        using(var image=Image.FromStream(stream)){appLogo=new Bitmap(image);}
        Text=AppVersion.WindowTitle;ClientSize=new Size(444,334);
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        BackColor=F1Theme.Background;ForeColor=Color.White;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        logo.SetBounds(336,8,88,88);logo.Image=appLogo;logo.SizeMode=PictureBoxSizeMode.Zoom;
        logo.TabStop=false;logo.AccessibleName="F1 Speed Manager logo";Controls.Add(logo);
        multiplier.SetBounds(18,12,310,50);multiplier.Text="1x";multiplier.Font=new Font("Segoe UI",30,FontStyle.Bold);multiplier.ForeColor=F1Theme.SpeedColor(themeSettings.Mode,1);
        status.SetBounds(20,69,312,28);status.Text="Waiting for F1 Manager 2023 / 2024…";status.ForeColor=F1Theme.Muted;
        overlayToggle.SetBounds(20,185,160,32);overlayToggle.Appearance=Appearance.Button;
        overlayToggle.Text="Overlay: Off · F7";overlayToggle.TextAlign=ContentAlignment.MiddleCenter;
        F1Theme.StyleButton(overlayToggle,F1Theme.Purple);
        overlayToggle.AccessibleName="In-game overlay";
        overlayPosition.SetBounds(195,187,230,28);overlayPosition.DropDownStyle=ComboBoxStyle.DropDownList;
        overlayPosition.Items.AddRange(new object[]{"Top right","Top left","Bottom right","Bottom left","Top center"});
        overlayPosition.BackColor=F1Theme.Surface;overlayPosition.ForeColor=Color.White;overlayPosition.FlatStyle=FlatStyle.Flat;
        overlayPosition.SelectedIndex=0;overlayPosition.AccessibleName="Overlay position";
        overlayToggle.CheckedChanged+=(s,e)=>{UpdateHotkeyLabels();ApplyOverlayAppearance(themeSettings.Mode);Program.Log("Overlay "+(overlayToggle.Checked?"enabled":"hidden")+".");RefreshOverlay();};
        overlayPosition.SelectedIndexChanged+=(s,e)=>{Program.Log("Overlay position: "+overlayPosition.Text+".");RefreshOverlay();};
        Controls.Add(overlayToggle);Controls.Add(overlayPosition);
        detail.SetBounds(20,227,405,67);detail.ForeColor=F1Theme.Muted;detail.Font=new Font("Segoe UI",9);
        detail.Text="Extra multiplier × the game's own speed.\nF1–F6: speed. F7: show/hide the in-game overlay.\nClosing restores extra 1x. Game files stay unchanged.";
        configure.SetBounds(20,296,126,26);configure.Text="Hotkeys…";F1Theme.StyleButton(configure,F1Theme.Blue);
        configure.Click+=(s,e)=>OpenHotkeySettings();
        colorMode.SetBounds(154,296,132,26);colorMode.DropDownStyle=ComboBoxStyle.DropDownList;colorMode.FlatStyle=FlatStyle.Flat;
        colorMode.BackColor=F1Theme.Surface;colorMode.ForeColor=Color.White;colorMode.MaxDropDownItems=4;colorMode.IntegralHeight=false;colorMode.DropDownHeight=106;
        colorMode.Items.AddRange(new object[]{"Red","Green","Orange","Blue","Purple","Cyan","Multicolor"});colorMode.AccessibleName="Color mode";
        colorMode.DrawMode=DrawMode.OwnerDrawFixed;colorMode.DrawItem+=DrawColorMode;
        changingTheme=true;colorMode.SelectedIndex=(int)themeSettings.Mode;changingTheme=false;colorMode.SelectedIndexChanged+=(s,e)=>SelectColorMode();
        Controls.Add(configure);Controls.Add(colorMode);
        credit.SetBounds(294,301,131,18);credit.Text="made by SkaffaWilly";
        credit.Font=new Font("Segoe UI",8);credit.ForeColor=F1Theme.Muted;credit.TextAlign=ContentAlignment.MiddleRight;
        Controls.Add(multiplier);Controls.Add(status);Controls.Add(detail);Controls.Add(credit);
        string[] labels={"F1  ·  1x","F2  ·  2x","F3  ·  3x","F4  ·  5x","F5  ·  10x","F6  ·  Reset"};
        for(int i=0;i<6;i++)
        {
            int index=i;var button=new Button();buttons[i]=button;
            button.SetBounds(20+(i%3)*137,105+(i/3)*38,130,32);button.Text=labels[i];
            F1Theme.StyleButton(button,F1Theme.ActionColor(themeSettings.Mode,i));button.Enabled=false;button.AutoEllipsis=true;
            button.Click+=(s,e)=>Request(rates[index]);Controls.Add(button);
        }
        ApplyTheme();
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
            action=>QueueUi(()=>{if(hotkeys==manager)HandleHotkey(action);}),()=>{if(hotkeys==manager)BeginInputGuard();},
            message=>QueueUi(()=>{if(hotkeys==manager)StopForHotkeyError(message);}));
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
        try{using(var dialog=new HotkeyDialog(hotkeySettings,themeSettings.Mode,ApplyBindings))dialog.ShowDialog(this);}
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
    void SelectColorMode()
    {
        if(changingTheme || colorMode.SelectedIndex<0)return;
        ThemeSettings previous=themeSettings,candidate=new ThemeSettings((ColorMode)colorMode.SelectedIndex);
        try
        {
            candidate.Save();themeSettings=candidate;ApplyTheme();Program.Log("Color mode: "+candidate.Mode+".");
        }
        catch(Exception e)
        {
            Program.Log("Color mode not saved: "+e.Message);changingTheme=true;colorMode.SelectedIndex=(int)previous.Mode;changingTheme=false;
            detail.Text="Color choice not saved: "+e.Message;
        }
    }
    void DrawColorMode(object sender,DrawItemEventArgs e)
    {
        if(e.Index<0 || e.Index>=colorMode.Items.Count)return;
        ColorMode mode=(ColorMode)e.Index;
        bool closed=(e.State&DrawItemState.ComboBoxEdit)!=0;
        Color background=ColorModeBackground(mode,e.State);
        using(var fill=new SolidBrush(background))e.Graphics.FillRectangle(fill,e.Bounds);
        if(mode==ColorMode.Multicolor)DrawMulticolorText(e.Graphics,(string)colorMode.Items[e.Index],e.Font,e.Bounds.Left+3,e.Bounds.Top+2);
        else using(var brush=new SolidBrush(F1Theme.ModeColor(mode)))e.Graphics.DrawString((string)colorMode.Items[e.Index],e.Font,brush,e.Bounds.Left+3,e.Bounds.Top+2);
        if(!closed && (e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();
    }
    static void DrawMulticolorText(Graphics graphics,string value,Font font,float x,float y)
    {
        using(var format=new StringFormat(StringFormat.GenericTypographic))
        {
            format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces;
            for(int i=0;i<value.Length;i++)
            {
                string character=value[i].ToString();
                using(var brush=new SolidBrush(MulticolorTextColor(i)))graphics.DrawString(character,font,brush,x,y,format);
                x+=graphics.MeasureString(character,font,PointF.Empty,format).Width;
            }
        }
    }
    internal static Color MulticolorTextColor(int index)
    {
        switch((index%6+6)%6){case 0:return F1Theme.Red;case 1:return F1Theme.Orange;case 2:return F1Theme.Green;case 3:return F1Theme.Cyan;case 4:return F1Theme.Blue;default:return F1Theme.Purple;}
    }
    internal static Color ColorModeBackground(ColorMode mode,DrawItemState state)
    {
        bool closed=(state&DrawItemState.ComboBoxEdit)!=0,selected=(state&DrawItemState.Selected)!=0;
        return closed?F1Theme.Surface:selected?F1Theme.Tint(F1Theme.ModeColor(mode),0.24f):F1Theme.Surface;
    }
    internal static Color OverlayTextColor(ColorMode mode,bool enabled)
    {
        if(!enabled)return Color.White;
        return mode==ColorMode.Green || mode==ColorMode.Orange?F1Theme.Background:F1Theme.ActionColor(mode,6);
    }
    internal static Color OverlayBackgroundColor(ColorMode mode,bool enabled)
    {
        if(!enabled)return F1Theme.Surface;
        Color accent=F1Theme.ActionColor(mode,6);
        return mode==ColorMode.Green || mode==ColorMode.Orange?accent:F1Theme.Tint(accent,0.28f);
    }
    internal static Color OverlayCheckedBackColor(ColorMode mode)
    {return mode==ColorMode.Green || mode==ColorMode.Orange?F1Theme.ActionColor(mode,6):Color.Empty;}
    void ApplyOverlayAppearance(ColorMode mode)
    {
        overlayToggle.ContrastPaint=overlayToggle.Checked && (mode==ColorMode.Green || mode==ColorMode.Orange);
        overlayToggle.ContrastBackColor=F1Theme.ActionColor(mode,6);
        overlayToggle.FlatAppearance.CheckedBackColor=OverlayCheckedBackColor(mode);
        overlayToggle.BackColor=OverlayBackgroundColor(mode,overlayToggle.Checked);
        overlayToggle.ForeColor=OverlayTextColor(mode,overlayToggle.Checked);
        overlayToggle.Invalidate();
    }
    void ApplyTheme()
    {
        ColorMode mode=themeSettings.Mode;
        F1Theme.StyleButton(overlayToggle,F1Theme.ActionColor(mode,6));
        ApplyOverlayAppearance(mode);
        F1Theme.StyleButton(configure,F1Theme.ActionColor(mode,8));
        for(int i=0;i<buttons.Length;i++)if(buttons[i]!=null)F1Theme.StyleButton(buttons[i],F1Theme.ActionColor(mode,i));
        multiplier.ForeColor=displayedRate<0?F1Theme.Error:F1Theme.SpeedColor(mode,displayedRate);
        if(displayedRate>=0)status.ForeColor=ready?F1Theme.ModeColor(mode):F1Theme.Muted;
        overlay.ColorMode=mode;overlay.Invalidate();colorMode.Invalidate();UpdateSpeedAppearance(displayedRate);
    }
    void BeginInputGuard()
    {
        if(closing || editingHotkeys || !ready || Volatile.Read(ref desired)<=1)return;
        Engine engine=Interlocked.CompareExchange(ref currentEngine,null,null);if(engine==null)return;
        long until=System.Diagnostics.Stopwatch.GetTimestamp()+System.Diagnostics.Stopwatch.Frequency*HotkeyManager.InputGuardMilliseconds/1000;
        Interlocked.Exchange(ref inputGuardUntil,until);
        if(!engine.TrySetInputGuard(1))Interlocked.CompareExchange(ref inputGuardUntil,0,until);
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
        try{BeginInvoke((Action)(()=>{if(closing || enabled && shutdown.WaitOne(0))return;displayedRate=active;multiplier.Text=active<0?"—":active+"x";multiplier.ForeColor=active<0?F1Theme.Error:F1Theme.SpeedColor(themeSettings.Mode,active);status.Text=text;status.ForeColor=active<0?F1Theme.Error:enabled?F1Theme.ModeColor(themeSettings.Mode):F1Theme.Muted;foreach(var b in buttons)b.Enabled=enabled;UpdateSpeedAppearance(active);if(extra!=null)detail.Text=extra;}));}
        catch(InvalidOperationException){}
    }
    void UpdateSpeedAppearance(long active)
    {
        for(int i=0;i<buttons.Length;i++)if(buttons[i]!=null)
        {
            bool selected=i<5 && rates[i]==active;Color color=F1Theme.ActionColor(themeSettings.Mode,i);
            buttons[i].BackColor=selected?F1Theme.Tint(color,0.26f):F1Theme.Surface;
            buttons[i].ForeColor=selected?color:Color.White;
        }
    }
    void RunController()
    {
        Engine engine=null;long lastActive=-1,lastRequested=-1;int handledSerial=0;bool waitingLogged=false,guardWasActive=false;
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
                    Interlocked.Exchange(ref desired,1);Interlocked.Exchange(ref inputGuardUntil,0);handledSerial=Volatile.Read(ref requestSerial);
                    Interlocked.Exchange(ref currentEngine,engine);ready=true;waitingLogged=false;lastActive=-1;lastRequested=-1;guardWasActive=false;
                }
                if(engine.Game.HasExited)
                {
                    ready=false;Interlocked.Exchange(ref currentEngine,null);Interlocked.Exchange(ref inputGuardUntil,0);overlaySnapshot=null;Program.Log("Game exited; waiting for the next session at extra 1x.");
                    engine.Dispose();engine=null;Display(1,"Game closed — waiting…",false,null);continue;
                }
                int serial=Volatile.Read(ref requestSerial);
                int wanted=Volatile.Read(ref desired);long guardUntil=Interlocked.Read(ref inputGuardUntil);
                bool guarded=wanted>1 && System.Diagnostics.Stopwatch.GetTimestamp()<guardUntil;
                int target=guarded?1:wanted;long before=engine.Requested;
                bool restoringGuard=!guarded && guardWasActive && serial==handledSerial;
                if(before!=target)
                {
                    if(restoringGuard)engine.TrySetInputGuard(target);
                    else engine.Set(target);
                }
                if(!guarded)handledSerial=serial;
                guardWasActive=guarded;
                engine.Heartbeat();long active=engine.Active,requested=engine.Requested;
                overlaySnapshot=new OverlaySnapshot(engine.Game.Id,guarded?wanted:active,System.Diagnostics.Stopwatch.GetTimestamp());
                if(!guarded && !restoringGuard && (active!=lastActive || requested!=lastRequested))
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
        finally{ready=false;Interlocked.Exchange(ref currentEngine,null);Interlocked.Exchange(ref inputGuardUntil,0);overlaySnapshot=null;try{if(engine!=null)engine.Dispose();}finally{workerDone.Set();}}
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
