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
