using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal enum OverlayCorner { TopRight, TopLeft, BottomRight, BottomLeft, TopCenter }

// Only the verified controller publishes snapshots; the overlay never reads game memory.
internal sealed class OverlaySnapshot
{
    internal readonly int ProcessId;
    internal readonly long Active, Stamp;
    internal OverlaySnapshot(int processId,long active,long stamp)
    {ProcessId=processId;Active=active;Stamp=stamp;}
}

internal sealed class SpeedOverlay : Form
{
    [StructLayout(LayoutKind.Sequential)] struct Rect {internal int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct PointNative {internal int X,Y;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {internal uint Size;internal Rect Monitor,Work;internal uint Flags;}
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref PointNative point);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    const int Transparent=0x20,ToolWindow=0x80,NoActivate=0x08000000;
    long active=1;
    float scale=1;
    internal ColorMode ColorMode=ColorMode.Red;
    internal string ToggleHint="F7";
    readonly Font speedFont=new Font("Segoe UI",15,FontStyle.Bold,GraphicsUnit.Pixel);
    readonly Font keyFont=new Font("Segoe UI",10,FontStyle.Regular,GraphicsUnit.Pixel);

    internal SpeedOverlay()
    {
        Text="F1 Speed Manager overlay";FormBorderStyle=FormBorderStyle.None;
        ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
        AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(160,34);
        BackColor=F1Theme.Background;TransparencyKey=Color.Magenta;
        DoubleBuffered=true;
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    sealed class DpiScope : IDisposable
    {
        readonly IntPtr previous;
        internal DpiScope()
        {
            try
            {
                previous=SetThreadDpiAwarenessContext(new IntPtr(-4)); // Per-monitor v2
                if(previous==IntPtr.Zero)previous=SetThreadDpiAwarenessContext(new IntPtr(-3));
                if(previous==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Overlay DPI context unavailable");
            }
            catch(EntryPointNotFoundException){throw new NotSupportedException("The overlay requires Windows 10 version 1607 or later.");}
        }
        public void Dispose(){SetThreadDpiAwarenessContext(previous);}
    }
    protected override void CreateHandle()
    {using(var dpi=new DpiScope())base.CreateHandle();}
    protected override CreateParams CreateParams
    {get{var p=base.CreateParams;p.ExStyle|=Transparent|ToolWindow|NoActivate;return p;}}
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x0084){m.Result=new IntPtr(-1);return;} // HTTRANSPARENT
        if(m.Msg==0x0021){m.Result=new IntPtr(3);return;} // MA_NOACTIVATE
        if(m.Msg==0x02E0){m.Result=IntPtr.Zero;return;} // Size follows the current monitor on the next refresh.
        base.WndProc(ref m);
    }
    internal static bool CanDisplay(OverlaySnapshot snapshot,bool enabled,int foregroundPid,long now,long frequency)
    {
        return enabled && snapshot!=null && snapshot.ProcessId>0 && foregroundPid==snapshot.ProcessId &&
            Engine.Allowed(snapshot.Active) && frequency>0 && now>=snapshot.Stamp && now-snapshot.Stamp<=frequency*2/5;
    }
    internal static Rectangle CalculateBounds(Rectangle area,OverlayCorner corner,int dpi)
    {
        if(dpi<96 || dpi>768)dpi=96;
        double factor=dpi/96.0;
        int width=(int)Math.Round(160*factor),height=(int)Math.Round(34*factor);
        if(area.Width<width || area.Height<height)return Rectangle.Empty;
        if(corner<OverlayCorner.TopRight || corner>OverlayCorner.TopCenter)throw new ArgumentOutOfRangeException("corner");
        int horizontal=Math.Min((int)Math.Round(12*factor),(area.Width-width)/2);
        // Leave space for the race HUD along the top and bottom edges.
        int vertical=Math.Min((int)Math.Round(84*factor),(area.Height-height)/4);
        bool left=corner==OverlayCorner.TopLeft || corner==OverlayCorner.BottomLeft;
        bool top=corner==OverlayCorner.TopLeft || corner==OverlayCorner.TopRight || corner==OverlayCorner.TopCenter;
        return new Rectangle(corner==OverlayCorner.TopCenter?area.Left+(area.Width-width)/2:left?area.Left+horizontal:area.Right-horizontal-width,
            top?area.Top+vertical:area.Bottom-vertical-height,width,height);
    }
    internal void RefreshDisplay(OverlaySnapshot snapshot,bool enabled,OverlayCorner corner)
    {
        if(!enabled || snapshot==null){Hide();return;}
        using(var dpi=new DpiScope())RefreshCore(snapshot,corner);
    }
    void RefreshCore(OverlaySnapshot snapshot,OverlayCorner corner)
    {
        IntPtr target=GetForegroundWindow();uint pid;
        GetWindowThreadProcessId(target,out pid);
        if(!CanDisplay(snapshot,true,(int)pid,Stopwatch.GetTimestamp(),Stopwatch.Frequency) ||
            target==IntPtr.Zero || IsIconic(target) || !IsWindowVisible(target)) {Hide();return;}
        Rect rect;var origin=new PointNative();
        if(!GetClientRect(target,out rect) || !ClientToScreen(target,ref origin)) {Hide();return;}
        Rectangle area=new Rectangle(origin.X,origin.Y,rect.Right-rect.Left,rect.Bottom-rect.Top);
        IntPtr monitor=MonitorFromWindow(target,2);
        var info=new MonitorInfo{Size=(uint)Marshal.SizeOf(typeof(MonitorInfo))};
        if(monitor==IntPtr.Zero || !GetMonitorInfo(monitor,ref info)){Hide();return;}
        area=Rectangle.Intersect(area,new Rectangle(info.Monitor.Left,info.Monitor.Top,info.Monitor.Right-info.Monitor.Left,info.Monitor.Bottom-info.Monitor.Top));
        // Create only this window in a per-monitor context; the controller GUI keeps its normal scaling.
        IntPtr window=Handle;
        if(MonitorFromWindow(window,2)!=monitor && !SetWindowPos(window,IntPtr.Zero,area.Left,area.Top,0,0,0x0001|0x0004|0x0010))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Overlay monitor positioning failed");
        int dpi=(int)GetDpiForWindow(window);
        Rectangle bounds=CalculateBounds(area,corner,dpi);
        if(bounds.IsEmpty){Hide();return;}
        if(active!=snapshot.Active || scale!=bounds.Width/160f) {active=snapshot.Active;scale=bounds.Width/160f;Invalidate();}
        if(Bounds!=bounds)Bounds=bounds;
        if(!Visible)Show();
        if(!SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0010))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Overlay positioning failed");
    }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        int width=ClientSize.Width,height=ClientSize.Height;
        if(width<12 || height<12)return;
        float radius=Math.Min(height,12*width/160f);
        using(var path=new GraphicsPath())
        {
            path.AddArc(0,0,radius,radius,180,90);path.AddArc(width-radius,0,radius,radius,270,90);
            path.AddArc(width-radius,height-radius,radius,radius,0,90);path.AddArc(0,height-radius,radius,radius,90,90);path.CloseFigure();
            var old=Region;Region=new Region(path);if(old!=null)old.Dispose();
        }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(scale,scale);
        using(var path=new GraphicsPath())
        using(var background=new SolidBrush(F1Theme.Background))
        using(var border=new Pen(F1Theme.Edge))
        using(var accent=new SolidBrush(F1Theme.SpeedColor(ColorMode,active)))
        using(var text=new SolidBrush(Color.White))
        using(var hint=new SolidBrush(F1Theme.ActionColor(ColorMode,6)))
        {
            path.AddArc(0,0,12,12,180,90);path.AddArc(147,0,12,12,270,90);
            path.AddArc(147,21,12,12,0,90);path.AddArc(0,21,12,12,90,90);path.CloseFigure();
            g.FillPath(background,path);g.DrawPath(border,path);
            g.FillEllipse(accent,10,14,6,6);
            g.DrawString("Extra "+active+"×",speedFont,text,23,7);
            if(ToggleHint.Length<=3)g.DrawString(ToggleHint,keyFont,hint,131,11);
        }
    }
    protected override void Dispose(bool disposing)
    {if(disposing){speedFont.Dispose();keyFont.Dispose();}base.Dispose(disposing);}
}
