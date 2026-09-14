using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal sealed class HotkeyManager : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window,int id);
    [DllImport("user32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern IntPtr SetWindowsHookEx(int type,HookProc proc,IntPtr module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    readonly IntPtr mainWindow;
    readonly HotkeySettings settings;
    readonly Func<OverlaySnapshot> snapshot;
    readonly Action<HotkeyAction> post;
    readonly Action<string> error;
    readonly List<int> registered=new List<int>();
    readonly HashSet<string> prefixRoots=new HashSet<string>();
    readonly HashSet<Keys> relevant=new HashSet<Keys>();
    readonly PrefixTracker tracker;
    readonly HookProc callback;
    readonly ManualResetEvent started=new ManualResetEvent(false);
    Thread thread;Control dispatcher;ApplicationContext context;
    IntPtr hook,prefixWindow;
    Exception startupError;
    uint modifierKeys;
    volatile bool stopping;
    bool disposed;
    internal HotkeyManager(IntPtr window,HotkeySettings settings,Func<OverlaySnapshot> snapshot,Action<HotkeyAction> post,Action<string> error)
    {
        mainWindow=window;this.settings=settings;this.snapshot=snapshot;this.post=post;this.error=error;
        tracker=new PrefixTracker(settings);callback=Keyboard;
        foreach(HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
            if(settings[action].Prefix!=Keys.None){prefixRoots.Add(settings[action].Root);relevant.Add(settings[action].Prefix);relevant.Add(settings[action].Key);}
    }
    internal void Start()
    {
        try
        {
            var roots=new HashSet<string>();
            foreach(HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
            {
                var binding=settings[action];if(binding.Prefix!=Keys.None)continue;
                Register(100+(int)action,binding.Modifiers,binding.Key,binding.Text);roots.Add(binding.Root);
            }
            foreach(HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
            {
                var binding=settings[action];if(binding.Prefix==Keys.None || !roots.Add(binding.Root))continue;
                Register(200+(int)action,binding.Modifiers,binding.Prefix,binding.Text+" prefix");
            }
            if(prefixRoots.Count!=0)
            {
                thread=new Thread(Run){IsBackground=true,Name="F1 Speed Manager shortcut hook"};thread.SetApartmentState(ApartmentState.STA);thread.Start();
                if(!started.WaitOne(5000))throw new TimeoutException("Keyboard shortcut initialization timed out.");
                if(startupError!=null)throw new InvalidOperationException("Keyboard shortcuts could not start: "+startupError.Message,startupError);
            }
        }
        catch{Dispose();throw;}
    }
    void Register(int id,uint modifiers,Keys key,string text)
    {Native.Check(RegisterHotKey(mainWindow,id,modifiers|0x4000,(uint)key),"Shortcut unavailable: "+text);registered.Add(id);}
    internal bool StandardAction(int id,out HotkeyAction action)
    {
        int index=id-100;action=HotkeyAction.Normal;
        if(index<0 || index>=9)return false;
        action=(HotkeyAction)index;var binding=settings[action];
        // Shared prefixes are handled only by their release-aware hook.
        return binding.Prefix==Keys.None && !prefixRoots.Contains(binding.Root);
    }
    void Run()
    {
        bool initialized=false;
        try
        {
            using(var control=new Control())
            using(var app=new ApplicationContext())
            {
                IntPtr dispatchHandle=control.Handle;dispatcher=control;context=app;
                int[] modifiers={160,161,162,163,164,165,91,92};
                foreach(int key in modifiers)if(GetAsyncKeyState(key)<0)modifierKeys|=ModifierBit(key);
                hook=SetWindowsHookEx(13,callback,Native.GetModuleHandle(null),0);
                Native.Check(hook!=IntPtr.Zero,"Install shortcut keyboard hook");
                initialized=true;started.Set();if(!stopping)Application.Run(app);
            }
        }
        catch(Exception e)
        {startupError=e;if(!initialized)started.Set();else if(!stopping)error("Keyboard shortcut hook failed: "+e.Message);}
        finally{if(hook!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}dispatcher=null;context=null;}
    }
    static uint ModifierBit(int key)
    {
        switch(key){case 16:case 160:return 1;case 161:return 2;case 17:case 162:return 4;case 163:return 8;case 18:case 164:return 16;case 165:return 32;case 91:return 64;case 92:return 128;default:return 0;}
    }
    uint Modifiers {get{return ((modifierKeys&3)!=0?4u:0u)|((modifierKeys&12)!=0?2u:0u)|((modifierKeys&48)!=0?1u:0u)|((modifierKeys&192)!=0?8u:0u);}}
    IntPtr Keyboard(int code,IntPtr message,IntPtr data)
    {
        if(code<0 || stopping)return CallNextHookEx(hook,code,message,data);
        int kind=message.ToInt32();bool down=kind==0x100 || kind==0x104;
        if(!down && kind!=0x101 && kind!=0x105)return CallNextHookEx(hook,code,message,data);
        try
        {
            int key=Marshal.ReadInt32(data);uint bit=ModifierBit(key);
            if(bit!=0){if(down){modifierKeys|=bit;tracker.CancelFallback();}else modifierKeys&=~bit;return CallNextHookEx(hook,code,message,data);}
            if(!relevant.Contains((Keys)key))return CallNextHookEx(hook,code,message,data);
            IntPtr foreground=GetForegroundWindow();uint pid;GetWindowThreadProcessId(foreground,out pid);
            bool scope=foreground==mainWindow || SpeedOverlay.CanDisplay(snapshot(),true,(int)pid,Stopwatch.GetTimestamp(),Stopwatch.Frequency);
            bool heldBefore=tracker.Held;HotkeyAction? action;
            bool consume=tracker.Process((Keys)key,down,Modifiers,scope,!heldBefore || foreground==prefixWindow,out action);
            if(!heldBefore && tracker.Held)prefixWindow=foreground;
            // post only queues work; no UI, logging or disk operations run in this callback.
            if(action.HasValue)post(action.Value);
            if(consume)return new IntPtr(1);
        }
        catch(Exception e)
        {
            stopping=true;error("Keyboard shortcut processing stopped: "+e.Message);
            try{var d=dispatcher;var app=context;if(d!=null && app!=null)d.BeginInvoke((Action)(()=>app.ExitThread()));}catch(InvalidOperationException){}
        }
        return CallNextHookEx(hook,code,message,data);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;stopping=true;
        var d=dispatcher;var app=context;
        try{if(d!=null && app!=null)d.BeginInvoke((Action)(()=>app.ExitThread()));}catch(InvalidOperationException){}
        bool joined=thread==null || !thread.IsAlive || thread.Join(1000);
        foreach(int id in registered)UnregisterHotKey(mainWindow,id);registered.Clear();
        if(joined)started.Dispose();
    }
}
