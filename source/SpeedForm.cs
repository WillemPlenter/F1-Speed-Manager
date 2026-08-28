using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal sealed class SpeedForm : Form
{
    [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window,int id);
    readonly Label multiplier=new Label(),status=new Label(),detail=new Label(),credit=new Label();
    readonly Button[] buttons=new Button[6];
    readonly int[] rates={1,2,3,5,10,1};
    readonly ManualResetEvent shutdown=new ManualResetEvent(false),workerDone=new ManualResetEvent(false);
    readonly System.Windows.Forms.Timer closeTimer=new System.Windows.Forms.Timer();
    Thread worker;
    int registered,desired=1,requestSerial;
    volatile bool ready,closing,canClose;
    internal SpeedForm()
    {
        Text="F1 Speed Manager";ClientSize=new Size(444,282);
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(25,28,34);ForeColor=Color.White;Font=new Font("Segoe UI",10);
        multiplier.SetBounds(18,12,405,50);multiplier.Text="1x";multiplier.Font=new Font("Segoe UI",30,FontStyle.Bold);
        status.SetBounds(20,69,400,28);status.Text="Waiting for F1 Manager 2023 / 2024…";
        detail.SetBounds(20,185,405,67);detail.ForeColor=Color.Silver;detail.Font=new Font("Segoe UI",9);
        detail.Text="Extra multiplier × the game's own speed.\nF1–F6 are global while this window is open.\nClosing restores extra 1x. Game files stay unchanged.";
        credit.SetBounds(20,254,405,18);credit.Text="made by SkaffaWilly";
        credit.Font=new Font("Segoe UI",8);credit.ForeColor=Color.FromArgb(153,160,174);credit.TextAlign=ContentAlignment.MiddleRight;
        Controls.Add(multiplier);Controls.Add(status);Controls.Add(detail);Controls.Add(credit);
        string[] labels={"F1  ·  1x","F2  ·  2x","F3  ·  3x","F4  ·  5x","F5  ·  10x","F6  ·  Reset"};
        for(int i=0;i<6;i++)
        {
            int index=i;var button=new Button();buttons[i]=button;
            button.SetBounds(20+(i%3)*137,105+(i/3)*38,130,32);button.Text=labels[i];
            button.FlatStyle=FlatStyle.Flat;button.BackColor=Color.FromArgb(43,48,59);button.Enabled=false;
            button.Click+=(s,e)=>Request(rates[index]);Controls.Add(button);
        }
        closeTimer.Interval=50;closeTimer.Tick+=(s,e)=>{if(workerDone.WaitOne(0)){closeTimer.Stop();canClose=true;Close();}};
        Shown+=(s,e)=>Start();
    }
    void Start()
    {
        // Reserve every speed/reset key before allowing any game writes.
        try
        {
            for(int i=0;i<6;i++){Native.Check(RegisterHotKey(Handle,100+i,0x4000,(uint)Keys.F1+(uint)i),"Cannot register F"+(i+1)+"; another app may own it");registered++;}
            worker=new Thread(RunController){IsBackground=true,Name="F1 Speed Manager controller"};worker.Start();
        }
        catch(Exception e){Program.Log("ERROR: "+e.Message);status.Text="Hotkey registration failed; no game writes.";detail.Text=e.Message;Unregister();workerDone.Set();}
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x0312){int i=m.WParam.ToInt32()-100;if(i>=0 && i<rates.Length){Request(rates[i]);return;}}
        base.WndProc(ref m);
    }
    void Request(int rate)
    {
        if(!ready || closing)return;
        Interlocked.Exchange(ref desired,rate);Interlocked.Increment(ref requestSerial);
    }
    void Display(long active,string text,bool enabled,string extra)
    {
        if(IsDisposed || !IsHandleCreated || closing)return;
        try{BeginInvoke((Action)(()=>{if(closing)return;multiplier.Text=active<0?"—":active+"x";status.Text=text;foreach(var b in buttons)b.Enabled=enabled;if(extra!=null)detail.Text=extra;}));}
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
                    ready=false;Program.Log("Game exited; waiting for the next session at extra 1x.");
                    engine.Dispose();engine=null;Display(1,"Game closed — waiting…",false,null);continue;
                }
                int serial=Volatile.Read(ref requestSerial);
                if(serial!=handledSerial){engine.Set(Volatile.Read(ref desired));handledSerial=serial;}
                engine.Heartbeat();long active=engine.Active,requested=engine.Requested;
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
            ready=false;var win=e as Win32Exception;
            string message=e.Message+(win==null?"":" (Windows error "+win.NativeErrorCode+")");Program.Log("ERROR: "+message);
            Display(-1,"Stopped — control disabled",false,message+"\nClose and reopen after resolving this error.\nThe watchdog returns to extra 1x if disconnected.");
        }
        finally{ready=false;try{if(engine!=null)engine.Dispose();}finally{workerDone.Set();}}
    }
    void Unregister(){for(int i=0;i<registered;i++)UnregisterHotKey(Handle,100+i);registered=0;}
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if(!canClose && !workerDone.WaitOne(0))
        {
            e.Cancel=true;
            if(!closing){closing=true;ready=false;status.Text="Restoring extra 1x before closing…";foreach(var b in buttons)b.Enabled=false;shutdown.Set();Unregister();closeTimer.Start();}
        }
        base.OnFormClosing(e);
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {shutdown.Set();Unregister();closeTimer.Dispose();if(workerDone.WaitOne(0)){shutdown.Dispose();workerDone.Dispose();}base.OnFormClosed(e);}
}
