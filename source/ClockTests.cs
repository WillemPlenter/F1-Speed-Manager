using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

internal static class ClockTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int Qpc(out long value);
    static int assertions;
    static void Assert(bool result,string name){if(!result)throw new Exception("Test failed: "+name);assertions++;}
    internal static int Run()
    {
        CheckArguments();
        CheckProfiles();
        CheckOverlay();
        CheckHotkeys();
        CheckShortcutCapture();
        CheckLogo();
        using(var process=Process.GetCurrentProcess())
        using(var handle=Native.OpenProcess(Native.WriteAccess,false,process.Id))
        {
            IntPtr block=Native.VirtualAllocEx(handle,IntPtr.Zero,(UIntPtr)8192,0x3000,0x04);Native.Check(block!=IntPtr.Zero,"Test allocation");
            try
            {
                Marshal.Copy(Engine.Code,0,block,Engine.Code.Length);
                // Test-only deterministic QPC stub reads raw counter at state+96.
                byte[] stub={0x48,0x8b,0x05,0,0,0,0,0x48,0x89,0x01,0xb8,1,0,0,0,0xc3};
                Buffer.BlockCopy(BitConverter.GetBytes(4096+96-768-7),0,stub,3,4);
                Marshal.Copy(stub,0,IntPtr.Add(block,768),stub.Length);
                IntPtr state=IntPtr.Add(block,4096);
                Action<int,long> put=(o,v)=>Marshal.WriteInt64(state,o,v);
                Func<int,long> get=o=>Marshal.ReadInt64(state,o);
                put(0,GameProfile.All[0].Magic);put(8,block.ToInt64()+768);put(24,100);put(32,100);put(40,1);put(48,1);put(56,1000);
                uint old;Native.Check(Native.VirtualProtectEx(handle,block,(UIntPtr)4096,0x20,out old),"Test RX");
                Native.Check(Native.FlushInstructionCache(handle,block,(UIntPtr)1024),"Test flush");
                Qpc qpc=(Qpc)Marshal.GetDelegateForFunctionPointer(block,typeof(Qpc));
                Func<long,long> sample=real=>{put(96,real);long v;Assert(qpc(out v)==1,"QPC success");return v;};
                Assert(sample(110)==110,"normal speed");
                put(48,2);Assert(sample(120)==120,"continuous rate change");
                Assert(sample(130)==140,"2x interval");
                put(48,5);Assert(sample(140)==160,"2x to 5x continuous");
                Assert(sample(150)==210,"5x interval");
                Assert(sample(145)==210,"out-of-order clock samples monotonic");
                put(48,1);Assert(sample(160)==260,"reset continuous");
                Assert(sample(170)==270,"normal after reset");
                put(48,10);Assert(sample(180)==280,"10x change continuous");
                put(56,185);Assert(sample(190)==335,"lease expiry splits elapsed interval");
                Assert(get(40)==1 && get(48)==1 && get(72)==1,"expiry latches 1x");
                Assert(sample(200)==345,"normal after lease expiry");
                put(56,1000);put(48,999);sample(210);Assert(get(40)==1,"invalid rate rejected in native code");
                foreach(int rate in new[]{1,2,3,5,10}) {put(48,rate);long start=sample(220+rate*30);Assert(sample(221+rate*30)==start+rate,"supported rate "+rate);}
                put(96,600);put(48,1);long initial; qpc(out initial);
                Parallel.For(0,10000,i=>{long v; if(qpc(out v)!=1 || v!=initial)throw new Exception("Concurrent QPC failed");});
                Assert(get(16)==0,"native lock released after concurrent calls");
                // A rejected scalar or malformed PE must never reach a live-memory write.
                foreach(long invalid in new long[]{-1,0,4,6,long.MaxValue})Assert(!Engine.Allowed(invalid),"reject unsupported rate "+invalid);
                bool rejected=false;try{new PeImage(new byte[512]);}catch(System.IO.InvalidDataException){rejected=true;}
                Assert(rejected,"reject malformed executable");
                // Exercise the exact atomic installer against our own dummy slot.
                put(104,block.ToInt64()+768);
                using(var thread=Native.CreateRemoteThread(handle,IntPtr.Zero,UIntPtr.Zero,IntPtr.Add(block,512),IntPtr.Add(state,104),0,IntPtr.Zero))
                { Native.Check(!thread.IsInvalid,"Test installer thread"); Assert(Native.WaitForSingleObject(thread,5000)==0,"installer completion");uint result;Native.Check(Native.GetExitCodeThread(thread,out result),"installer result");Assert(result==1 && get(104)==block.ToInt64(),"atomic IAT installation"); }
                put(104,1234);
                using(var thread=Native.CreateRemoteThread(handle,IntPtr.Zero,UIntPtr.Zero,IntPtr.Add(block,512),IntPtr.Add(state,104),0,IntPtr.Zero))
                { Native.Check(!thread.IsInvalid,"Test refusal thread"); Assert(Native.WaitForSingleObject(thread,5000)==0,"refusal completion");uint result;Native.Check(Native.GetExitCodeThread(thread,out result),"refusal result");Assert(result==0 && get(104)==1234,"changed IAT is not written"); }
                Console.WriteLine("PASS: "+assertions+" assertions plus 10000 concurrent native calls. No game access.");
                GC.KeepAlive(qpc);
            }
            finally {Native.VirtualFreeEx(handle,block,UIntPtr.Zero,0x8000);}
        }
        return 0;
    }
    static void Rejected(Action action,string name)
    {
        bool rejected=false;
        try{action();}catch(System.IO.InvalidDataException){rejected=true;}catch(InvalidOperationException){rejected=true;}catch(ArgumentException){rejected=true;}
        Assert(rejected,name);
    }
    static void CheckArguments()
    {
        Assert(Program.ParseArguments(new string[0])==Program.Mode.Gui,"default is GUI");
        Assert(Program.ParseArguments(new[]{"--inspect"})==Program.Mode.Inspect,"read-only inspection accepted");
        Assert(Program.ParseArguments(new[]{"--self-test"})==Program.Mode.SelfTest,"local self-tests accepted");
        Rejected(()=>Program.ParseArguments(new[]{"--probe2"}),"legacy probe refused");
        Rejected(()=>Program.ParseArguments(new[]{"--probe","2","30"}),"live CLI probe refused");
        Rejected(()=>Program.ParseArguments(new[]{"--inspect","extra"}),"inspection with extra arguments refused");
        Rejected(()=>Program.ParseArguments(new[]{"--self-test","extra"}),"self-test with extra arguments refused");
        Rejected(()=>Program.ParseArguments(new[]{"--unknown"}),"unknown option refused");
    }
    static void CheckLogo()
    {
        using(var stream=typeof(SpeedForm).Assembly.GetManifestResourceStream("Logo.png"))
        {
            Assert(stream!=null,"standalone executable contains logo resource");
            using(var image=System.Drawing.Image.FromStream(stream))
                Assert(image.Width>0 && image.Height>0,"embedded logo decodes");
        }
        using(var form=new SpeedForm(false))
        {
            var version=(System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(typeof(SpeedForm).Assembly,typeof(System.Reflection.AssemblyInformationalVersionAttribute));
            Assert(form.Text=="F1 Speed Manager "+version.InformationalVersion,"window title matches executable product version");
            System.Windows.Forms.PictureBox logo=null;
            foreach(System.Windows.Forms.Control control in form.Controls)
                if(control is System.Windows.Forms.PictureBox)logo=(System.Windows.Forms.PictureBox)control;
            Assert(logo!=null && logo.Image!=null,"logo present in program window");
            Assert(form.ClientRectangle.Contains(logo.Bounds),"logo inside program window");
            foreach(System.Windows.Forms.Control control in form.Controls)
                if(control!=logo)Assert(!control.Bounds.IntersectsWith(logo.Bounds),"logo does not cover "+control.GetType().Name);
            System.Windows.Forms.ComboBox position=null;System.Windows.Forms.CheckBox toggle=null;
            foreach(System.Windows.Forms.Control control in form.Controls)
            {if(control is System.Windows.Forms.ComboBox)position=(System.Windows.Forms.ComboBox)control;if(control is System.Windows.Forms.CheckBox)toggle=(System.Windows.Forms.CheckBox)control;}
            Assert(position.Items.Count==5 && (string)position.Items[4]=="Top center","GUI includes top center as fifth position");
            var handler=typeof(SpeedForm).GetMethod("HandleHotkey",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            handler.Invoke(form,new object[]{HotkeyAction.PreviousPosition});
            Assert(position.SelectedIndex==4 && !toggle.Checked,"previous wraps to top center without enabling overlay");
            handler.Invoke(form,new object[]{HotkeyAction.NextPosition});
            Assert(position.SelectedIndex==0 && !toggle.Checked,"next wraps to top right without enabling overlay");
            handler.Invoke(form,new object[]{HotkeyAction.ToggleOverlay});handler.Invoke(form,new object[]{HotkeyAction.PreviousPosition});
            Assert(position.SelectedIndex==4 && toggle.Checked,"moving visible overlay keeps visibility");
            var desired=typeof(SpeedForm).GetField("desired",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            Assert((int)desired.GetValue(form)==1,"overlay input never changes extra multiplier");
        }
    }
    static void CheckOverlay()
    {
        var fresh=new OverlaySnapshot(42,5,1000);
        Assert(SpeedOverlay.CanDisplay(fresh,true,42,1100,1000),"fresh active overlay allowed");
        Assert(!SpeedOverlay.CanDisplay(fresh,false,42,1100,1000),"toggle hides overlay");
        Assert(!SpeedOverlay.CanDisplay(fresh,true,43,1100,1000),"other foreground process hides overlay");
        Assert(!SpeedOverlay.CanDisplay(null,true,42,1100,1000),"disconnection hides overlay");
        Assert(!SpeedOverlay.CanDisplay(fresh,true,42,1401,1000),"stale speed snapshot hides overlay");
        Assert(!SpeedOverlay.CanDisplay(fresh,true,42,999,1000),"future timestamp rejected");
        Assert(!SpeedOverlay.CanDisplay(new OverlaySnapshot(42,4,1000),true,42,1100,1000),"unknown speed not displayed");
        Assert(!SpeedOverlay.CanDisplay(new OverlaySnapshot(0,1,1000),true,0,1100,1000),"missing process rejected");
        var area=new System.Drawing.Rectangle(-1920,40,1920,1080);
        foreach(OverlayCorner corner in Enum.GetValues(typeof(OverlayCorner)))
        {
            var bounds=SpeedOverlay.CalculateBounds(area,corner,144);
            Assert(area.Contains(bounds) && bounds.Width==240 && bounds.Height==51,"DPI and negative monitor position "+corner);
        }
        var left=SpeedOverlay.CalculateBounds(area,OverlayCorner.TopLeft,96);
        var right=SpeedOverlay.CalculateBounds(area,OverlayCorner.TopRight,96);
        Assert(left.Left<right.Left && left.Top==right.Top,"corner selection moves overlay horizontally");
        var center=SpeedOverlay.CalculateBounds(area,OverlayCorner.TopCenter,96);
        Assert(center.Top==left.Top && center.Left==area.Left+(area.Width-center.Width)/2,"top center is horizontally centered near top, not screen center");
        Rejected(()=>SpeedOverlay.CalculateBounds(area,(OverlayCorner)99,96),"invalid overlay position rejected");
        Assert(SpeedOverlay.CalculateBounds(new System.Drawing.Rectangle(0,0,100,20),OverlayCorner.TopRight,96).IsEmpty,"overlay hidden when client is too small");
        var small=new System.Drawing.Rectangle(10,20,160,34);
        Assert(SpeedOverlay.CalculateBounds(small,OverlayCorner.BottomLeft,96)==small,"small valid client bounds clamped");
    }
    static void CheckHotkeys()
    {
        var defaults=HotkeySettings.Defaults;
        Assert(HotkeySettings.Parse(defaults.Serialize()).Serialize()==defaults.Serialize(),"hotkeys roundtrip");
        Assert(defaults[HotkeyAction.ToggleOverlay].Text=="F7" && defaults[HotkeyAction.NextPosition].Text=="F7+Down","shared F7 defaults");
        Assert(Shortcut.Parse("shift+control+F9+↑").Text=="Ctrl+Shift+F9+Up","canonical modifiers and arrow alias");
        foreach(string invalid in new[]{"","Up","Ctrl","F7+F7","Ctrl+Ctrl+F2","Win+F2","27","Escape","A+Up","F7+Up+Down","NoSuchKey"})
            Rejected(()=>Shortcut.Parse(invalid),"reject shortcut "+invalid);
        Rejected(()=>HotkeySettings.Parse(defaults.Serialize().Replace("Double=F2","Double=F1")),"duplicate shortcut refused");
        Rejected(()=>HotkeySettings.Parse(defaults.Serialize().Replace("Reset=F6", "")),"missing action refused");
        Rejected(()=>HotkeySettings.Parse(defaults.Serialize()+"Normal=F9"),"duplicate action refused");
        Rejected(()=>HotkeySettings.Parse("Mystery=F9"),"unknown action refused");
        var tracker=new PrefixTracker(defaults);HotkeyAction? action;
        Assert(!tracker.Process(System.Windows.Forms.Keys.Up,true,0,true,true,out action) && !action.HasValue,"plain arrow passes through");
        Assert(tracker.Process(System.Windows.Forms.Keys.F7,true,0,true,true,out action) && !action.HasValue,"F7 press waits for release");
        Assert(tracker.Process(System.Windows.Forms.Keys.F7,true,0,true,true,out action) && !action.HasValue,"F7 repeat does not toggle");
        Assert(tracker.Process(System.Windows.Forms.Keys.F7,false,0,true,true,out action) && action==HotkeyAction.ToggleOverlay,"F7 alone toggles on release");
        tracker.Process(System.Windows.Forms.Keys.F7,true,0,true,true,out action);
        Assert(tracker.Process(System.Windows.Forms.Keys.Up,true,0,true,true,out action) && action==HotkeyAction.PreviousPosition,"held F7 up moves once");
        Assert(tracker.Process(System.Windows.Forms.Keys.Up,true,0,true,true,out action) && !action.HasValue,"held arrow repeat consumed");
        Assert(tracker.Process(System.Windows.Forms.Keys.F7,false,0,true,true,out action) && !action.HasValue,"release prefix first never toggles after move");
        Assert(tracker.Process(System.Windows.Forms.Keys.Up,false,0,true,true,out action) && !action.HasValue,"arrow release still consumed after prefix release");
        Assert(!tracker.Process(System.Windows.Forms.Keys.Up,true,0,true,true,out action),"next plain arrow free again");
        tracker.Process(System.Windows.Forms.Keys.F7,true,0,true,true,out action);
        Assert(tracker.Process(System.Windows.Forms.Keys.Down,true,0,true,true,out action) && action==HotkeyAction.NextPosition,"held F7 down moves");
        tracker.Process(System.Windows.Forms.Keys.Down,false,0,true,true,out action);
        Assert(tracker.Process(System.Windows.Forms.Keys.F7,false,0,true,true,out action) && !action.HasValue,"normal release order no accidental toggle");
        tracker.Process(System.Windows.Forms.Keys.F7,true,0,false,true,out action);
        Assert(!tracker.Process(System.Windows.Forms.Keys.Up,true,0,false,true,out action) && !action.HasValue,"arrow outside game/tool not consumed");
        tracker.Process(System.Windows.Forms.Keys.F7,false,0,false,true,out action);
        Assert(!action.HasValue,"out of scope chord suppresses standalone toggle");
        tracker.Process(System.Windows.Forms.Keys.F7,true,0,true,true,out action);
        tracker.Process(System.Windows.Forms.Keys.F7,false,0,true,false,out action);
        Assert(!action.HasValue,"focus change cancels release action");
        tracker.Process(System.Windows.Forms.Keys.F7,true,0,true,true,out action);tracker.CancelFallback();
        tracker.Process(System.Windows.Forms.Keys.F7,false,0,true,true,out action);
        Assert(!action.HasValue,"modifier introduced cancels fallback");
        Assert(!tracker.Process(System.Windows.Forms.Keys.A,true,0,true,true,out action),"ordinary typing not captured");
        var custom=HotkeySettings.Parse(defaults.Serialize().Replace("F7","Ctrl+F9"));
        tracker=new PrefixTracker(custom);
        Assert(!tracker.Process(System.Windows.Forms.Keys.F9,true,0,true,true,out action),"custom prefix requires configured modifier");
        tracker.Process(System.Windows.Forms.Keys.F9,true,2,true,true,out action);
        Assert(tracker.Process(System.Windows.Forms.Keys.Down,true,2,true,true,out action) && action==HotkeyAction.NextPosition,"custom modifier prefix works");
        tracker.Process(System.Windows.Forms.Keys.Down,false,2,true,true,out action);
        tracker.Process(System.Windows.Forms.Keys.F9,false,2,true,true,out action);
        Assert(!action.HasValue,"custom chord does not toggle");
    }
    static void CheckShortcutCapture()
    {
        var capture=new ShortcutCapture();
        Assert(capture.Process(System.Windows.Forms.Keys.F8,true,0).Text=="F8","capture direct F key on press");
        Assert(capture.Process(System.Windows.Forms.Keys.F8,true,0)==null,"capture ignores function repeat");
        Assert(capture.Process(System.Windows.Forms.Keys.F8,false,0)==null,"capture release never overwrites shortcut");
        Assert(capture.Process(System.Windows.Forms.Keys.F9,true,0).Text=="F9","next gesture replaces previous F key");
        Assert(capture.Process(System.Windows.Forms.Keys.Up,true,0).Text=="F9+Up","capture held prefix and arrow");
        Assert(capture.Process(System.Windows.Forms.Keys.Up,true,0)==null,"capture ignores arrow repeat");
        capture.Process(System.Windows.Forms.Keys.F9,false,0);
        Assert(capture.Process(System.Windows.Forms.Keys.Up,false,0)==null,"prefix first release order keeps chord");
        capture.Reset();
        Assert(capture.Process(System.Windows.Forms.Keys.ControlKey,true,2)==null,"modifier alone does not record");
        Assert(capture.Process(System.Windows.Forms.Keys.F2,true,2).Text=="Ctrl+F2","capture Ctrl plus F key");
        capture.Process(System.Windows.Forms.Keys.F2,false,2);capture.Process(System.Windows.Forms.Keys.ControlKey,false,0);
        Assert(capture.Process(System.Windows.Forms.Keys.A,true,6).Text=="Ctrl+Shift+A","capture letter with modifiers");capture.Reset();
        Rejected(()=>capture.Process(System.Windows.Forms.Keys.B,true,0),"bare letter is rejected, not typed");capture.Reset();
        Assert(capture.Process(System.Windows.Forms.Keys.F7,true,5).Text=="Alt+Shift+F7","capture multiple modifiers");
        Assert(capture.Process(System.Windows.Forms.Keys.Down,true,5).Text=="Alt+Shift+F7+Down","capture modifier prefix chord");
        capture.Process(System.Windows.Forms.Keys.Down,false,5);
        Assert(capture.Process(System.Windows.Forms.Keys.Up,true,5)==null,"only first chord recorded until prefix released");capture.Reset();
        capture.Process(System.Windows.Forms.Keys.F7,true,0);
        Rejected(()=>capture.Process(System.Windows.Forms.Keys.Up,true,2),"modifier change during recording refused");capture.Reset();
        Assert(capture.Process(System.Windows.Forms.Keys.Escape,true,0)==null,"Escape resets recorder without binding");
        Assert(capture.Process(System.Windows.Forms.Keys.F4,true,0).Text=="F4","recorder works after reset");capture.Reset();
        using(var field=new ShortcutField())
        {
            field.Text="F1";string feedback=null;field.Feedback=value=>feedback=value;
            var press=System.Windows.Forms.Message.Create(IntPtr.Zero,0x100,(IntPtr)System.Windows.Forms.Keys.F9,IntPtr.Zero);
            Assert(field.PreProcessMessage(ref press) && field.Text=="F9","focused field records F key without text editing");
            var arrow=System.Windows.Forms.Message.Create(IntPtr.Zero,0x100,(IntPtr)System.Windows.Forms.Keys.Down,IntPtr.Zero);
            Assert(field.PreProcessMessage(ref arrow) && field.Text=="F9+Down","field intercepts navigation key as chord");
            var release=System.Windows.Forms.Message.Create(IntPtr.Zero,0x101,(IntPtr)System.Windows.Forms.Keys.F9,IntPtr.Zero);field.PreProcessMessage(ref release);
            var letter=System.Windows.Forms.Message.Create(IntPtr.Zero,0x100,(IntPtr)System.Windows.Forms.Keys.C,IntPtr.Zero);
            Assert(field.PreProcessMessage(ref letter) && field.Text=="F9+Down" && !string.IsNullOrEmpty(feedback),"invalid letter leaves binding unchanged with feedback");
            Assert(field.ReadOnly && !field.ShortcutsEnabled,"capture field does not accept arbitrary text");
        }
    }
    static void CheckProfiles()
    {
        string p23=@"C:\Steam\steamapps\common\F1 Manager 2023\F1Manager23\Binaries\Win64\F1Manager23.exe";
        string p24=@"D:\Steam\steamapps\common\F1 Manager 2024\F1Manager24\Binaries\Win64\F1Manager24.exe";
        string launcher=@"D:\Steam\steamapps\common\F1 Manager 2024\F1Manager24.exe";
        var a=GameProfile.FromExecutable(p23);var b=GameProfile.FromExecutable(p24);
        Assert(a!=null && b!=null && a!=b,"distinct game profiles");
        Assert(GameProfile.FromExecutable(p24.ToUpperInvariant())==b,"Windows path case ignored");
        Assert(GameProfile.FromExecutable(launcher)==null,"launcher not a game target");
        Assert(GameProfile.FromExecutable(p23.Replace("F1Manager23.exe","F1Manager24.exe"))==null,"mixed game path rejected");
        Assert(GameProfile.SelectUnique(new string[0])==-1,"no running game");
        Assert(GameProfile.SelectUnique(new[]{launcher})==-1,"launcher alone ignored");
        Assert(GameProfile.SelectUnique(new[]{launcher,p23})==1,"2023 selected automatically");
        Assert(GameProfile.SelectUnique(new[]{p24,launcher})==0,"2024 selected automatically");
        Rejected(()=>GameProfile.SelectUnique(new[]{p23,p24}),"two different games refused");
        Rejected(()=>GameProfile.SelectUnique(new[]{p24,p24}),"duplicate game instances refused");
        foreach(var profile in new[]{a,b})
        {
            profile.ValidateManifest(profile.AppId,profile.BuildId);profile.ValidateHash(profile.Hash);
            var other=profile==a?b:a;
            Rejected(()=>profile.ValidateManifest(other.AppId,profile.BuildId),"other game's app ID refused");
            Rejected(()=>profile.ValidateManifest(profile.AppId,other.BuildId),"other game's build refused");
            Rejected(()=>profile.ValidateHash(other.Hash),"other game's executable refused");
            Rejected(()=>profile.ValidateManifest(profile.AppId,"unknown"),"unknown build refused");
        }
        Assert(a.Magic!=b.Magic,"resident clock markers remain game-specific");
    }
}
