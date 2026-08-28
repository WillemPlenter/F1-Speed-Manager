using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal sealed class Engine : IDisposable
{
    internal GameProfile Profile { get; private set; }
    long Magic { get { PeImage.Require(Profile!=null,"Game profile required");return Profile.Magic; } }
    internal const int StateOffset=4096;
    internal static readonly byte[] Code=LoadCode();
    internal readonly Action<string> Log;
    internal Process Game;
    SafeProcessHandle memory;
    FileStream imageLock;
    internal long Block, Iat, Original, Frequency;
    internal long LastBeat;
    internal long Requested { get { lock(sync) {return Block==0?1:Read64(Block+StateOffset+48);} } }
    bool installed;
    long verifiedCreation;
    long lastTargetCheck;
    readonly object sync=new object();
    static byte[] LoadCode() { using(var s=typeof(Engine).Assembly.GetManifestResourceStream("Clock.bin")) { if(s==null)throw new InvalidDataException("Missing clock resource"); byte[] b=new byte[s.Length]; int n=s.Read(b,0,b.Length); PeImage.Require(n==b.Length && b.Length<4096,"Invalid clock resource"); return b; } }
    internal Engine(Action<string> log) { Log=log; Native.Check(Native.QueryPerformanceFrequency(out Frequency),"QPC frequency"); }
    internal byte[] Read(long address,int n)
    {
        byte[] b=new byte[n]; UIntPtr read;
        Native.Check(Native.ReadProcessMemory(memory,new IntPtr(address),b,(UIntPtr)n,out read) && read.ToUInt64()==(ulong)n,"ReadProcessMemory at 0x"+address.ToString("X"));
        return b;
    }
    internal long Read64(long address) { return BitConverter.ToInt64(Read(address,8),0); }
    void Write(long address,byte[] b)
    {
        UIntPtr count;
        Native.Check(Native.WriteProcessMemory(memory,new IntPtr(address),b,(UIntPtr)b.Length,out count) && count.ToUInt64()==(ulong)b.Length,"WriteProcessMemory at 0x"+address.ToString("X"));
    }
    void Write64(long address,long value) { Write(address,BitConverter.GetBytes(value)); }
    static string Hash(Stream s) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(s)).Replace("-","").ToLowerInvariant(); }
    static bool SamePath(string a,string b) { return String.Equals(Path.GetFullPath(a),Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase); }
    static string Field(string vdf,string name) { var m=Regex.Match(vdf,"\""+Regex.Escape(name)+"\"\\s+\"([^\"]*)\""); if(!m.Success)throw new InvalidDataException("Missing Steam field: "+name); return m.Groups[1].Value; }
    static Process FindRunningTarget(out string path,out GameProfile profile)
    {
        path=null;profile=null;Process chosen=null;
        var candidates=new List<Process>();
        try
        {
            foreach(var known in GameProfile.All)candidates.AddRange(Process.GetProcessesByName(known.ProcessName));
            var paths=candidates.Select(p=>p.MainModule.FileName).ToArray();
            int index=GameProfile.SelectUnique(paths);
            if(index<0)return null;
            path=paths[index];profile=GameProfile.FromExecutable(path);chosen=candidates[index];return chosen;
        }
        finally {foreach(var candidate in candidates)if(candidate!=chosen)candidate.Dispose();}
    }
    void VerifyTargetSelection()
    {
        string path;GameProfile profile;
        using(var current=FindRunningTarget(out path,out profile))
            PeImage.Require(current!=null && current.Id==Game.Id && profile==Profile && SamePath(path,imageLock.Name),"Running game selection changed; control stopped");
        lastTargetCheck=Native.Now();
    }
    internal bool Discover()
    {
        PeImage.Require(IntPtr.Size==8,"Run the 64-bit build");
        string path;GameProfile profile;
        Process chosen=FindRunningTarget(out path,out profile);
        if(chosen==null) {Log("Game not running (launcher alone is not a target).");return false;}
        Game=chosen;Profile=profile;
        Log("Detected "+Profile.DisplayName+"; PID "+Game.Id+": "+path);
        string install=Directory.GetParent(path).Parent.Parent.Parent.FullName;
        string steamapps=Directory.GetParent(install).Parent.FullName;
        PeImage.Require(Directory.GetParent(install).Name.Equals("common",StringComparison.OrdinalIgnoreCase),"Not a Steam common directory");
        string manifest=Path.Combine(steamapps,"appmanifest_"+Profile.AppId+".acf");
        string text=File.ReadAllText(manifest);
        Profile.ValidateManifest(Field(text,"appid"),Field(text,"buildid"));
        PeImage.Require(SamePath(Path.Combine(steamapps,"common",Field(text,"installdir")),install),"Steam install path mismatch");
        imageLock=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        string hash=Hash(imageLock);
        Log(Profile.DisplayName+"; Steam build "+Profile.BuildId+"; SHA256 "+hash);
        Profile.ValidateHash(hash);
        imageLock.Position=0;
        byte[] disk=new byte[checked((int)imageLock.Length)]; int total=0;
        while(total<disk.Length) { int n=imageLock.Read(disk,total,disk.Length-total); if(n==0)throw new EndOfStreamException(); total+=n; }
        var pe=new PeImage(disk);
        uint slot=pe.FindQpcImport(), sample=pe.FindConversion(slot);
        memory=Native.OpenProcess(Native.ReadAccess,false,Game.Id); Native.Check(!memory.IsInvalid,"OpenProcess read-only");
        long exitTime,kernelTime,userTime;
        Native.Check(Native.GetProcessTimes(memory,out verifiedCreation,out exitTime,out kernelTime,out userTime),"Read target creation time");
        bool wow; Native.Check(Native.IsWow64Process(memory,out wow),"Architecture check"); PeImage.Require(!wow,"Target is not 64-bit");
        long imageBase=Game.MainModule.BaseAddress.ToInt64();
        byte[] live=Read(imageBase,4096);
        PeImage.Require(live.Take(pe.Optional+112).SequenceEqual(disk.Take(pe.Optional+112)),"Loaded PE headers differ from verified executable");
        PeImage.Require(Game.MainModule.ModuleMemorySize==pe.ImageSize,"Loaded image size mismatch");
        byte[] expected=disk.Skip(pe.Offset(sample,32)).Take(32).ToArray();
        PeImage.Require(Read(imageBase+sample,32).SequenceEqual(expected),"Live timing conversion code differs from disk");
        Iat=imageBase+slot;
        Original=ResolveQpc();
        long current=Read64(Iat);
        Log("Named import discovered: QPC IAT 0x"+Iat.ToString("X")+"; conversion call RVA 0x"+sample.ToString("X"));
        Log("Verified system QPC export 0x"+Original.ToString("X"));
        if(current!=Original)
        {
            Block=current;
            ValidateBlock();
            Log("Recognized exact resident clock; active "+Read64(Block+StateOffset+40)+"x; requested "+Read64(Block+StateOffset+48)+"x; watchdog resets "+Read64(Block+StateOffset+72)+".");
        }
        Region(Iat,8,false,true);
        Log("Read-only preflight passed. No game memory has been written.");
        return true;
    }
    long ResolveQpc()
    {
        IntPtr qpc=Native.GetProcAddress(Native.GetModuleHandle("kernel32.dll"),"QueryPerformanceCounter");
        PeImage.Require(qpc!=IntPtr.Zero,"QPC export not found");
        using(var self=Process.GetCurrentProcess())
        {
            foreach(ProcessModule m in self.Modules)
            {
                long delta=qpc.ToInt64()-m.BaseAddress.ToInt64();
                if(delta<0 || delta>=m.ModuleMemorySize)continue;
                PeImage.Require(SamePath(Path.GetDirectoryName(m.FileName),Environment.SystemDirectory),"QPC is not in Windows System32");
                PeImage.Require(m.ModuleName.Equals("KernelBase.dll",StringComparison.OrdinalIgnoreCase)||m.ModuleName.Equals("kernel32.dll",StringComparison.OrdinalIgnoreCase),"Unexpected QPC implementation module");
                foreach(ProcessModule r in Game.Modules)
                {
                    if(!SamePath(r.FileName,m.FileName))continue;
                    PeImage.Require(r.ModuleMemorySize==m.ModuleMemorySize,"System module size differs");
                    long remote=r.BaseAddress.ToInt64()+delta;
                    byte[] local=new byte[32];Marshal.Copy(qpc,local,0,32);
                    PeImage.Require(Read(remote,32).SequenceEqual(local),"Target QPC prologue differs; another hook may be installed");
                    PeImage.Require(local[0]!=0xe9 && local[0]!=0xeb && !(local[0]==0xff && local[1]==0x25),"QPC begins with unsupported detour");
                    Region(remote,32,true,true);
                    return remote;
                }
            }
        }
        throw new InvalidDataException("Cannot positively resolve target system QPC");
    }
    void Region(long address,int size,bool executable,bool image)
    {
        Native.MemoryInfo info;
        Native.Check(Native.VirtualQueryEx(memory,new IntPtr(address),out info,(UIntPtr)Marshal.SizeOf(typeof(Native.MemoryInfo)))!=UIntPtr.Zero,"VirtualQueryEx");
        PeImage.Require(info.State==0x1000 && (info.Protect&0x101)==0 && address>=info.BaseAddress.ToInt64() && address+size<=info.BaseAddress.ToInt64()+(long)info.RegionSize.ToUInt64(),"Invalid memory region");
        if(image)PeImage.Require(info.Type==0x1000000,"Expected image memory");
        if(executable)PeImage.Require((info.Protect&0xf0)!=0,"Expected executable memory");
    }
    internal void ValidateBlock()
    {
        PeImage.Require(Block>0 && Block%4096==0,"Unrecognized QPC hook; stop");
        Region(Block,Code.Length,true,false); Region(Block+StateOffset,96,false,false);
        PeImage.Require(Read(Block,Code.Length).SequenceEqual(Code),"Unrecognized clock code; stop");
        PeImage.Require(Read64(Block+StateOffset)==Magic && Read64(Block+StateOffset+8)==Original,"Unrecognized clock state; stop");
        Native.MemoryInfo codeInfo,dataInfo;
        Native.Check(Native.VirtualQueryEx(memory,new IntPtr(Block),out codeInfo,(UIntPtr)Marshal.SizeOf(typeof(Native.MemoryInfo)))!=UIntPtr.Zero,"Validate clock allocation");
        Native.Check(Native.VirtualQueryEx(memory,new IntPtr(Block+StateOffset),out dataInfo,(UIntPtr)Marshal.SizeOf(typeof(Native.MemoryInfo)))!=UIntPtr.Zero,"Validate clock data allocation");
        PeImage.Require(codeInfo.AllocationBase.ToInt64()==Block && dataInfo.AllocationBase.ToInt64()==Block && codeInfo.Type==0x20000 && dataInfo.Type==0x20000 && codeInfo.Protect==0x20 && dataInfo.Protect==0x04,"Clock allocation/protection mismatch");
        PeImage.Require(Allowed(Read64(Block+StateOffset+40)) && Allowed(Read64(Block+StateOffset+48)),"Invalid resident multiplier state");
    }
    internal static bool Allowed(long value) { return value==1 || value==2 || value==3 || value==5 || value==10; }
    internal void Install()
    {
        lock(sync)
        {
            PeImage.Require(Game!=null && !Game.HasExited && memory!=null,"Preflight required");
            VerifyTargetSelection();
            memory.Dispose(); memory=Native.OpenProcess(Native.WriteAccess,false,Game.Id);Native.Check(!memory.IsInvalid,"OpenProcess clock control");
            long creation,exitTime,kernelTime,userTime;
            Native.Check(Native.GetProcessTimes(memory,out creation,out exitTime,out kernelTime,out userTime),"Revalidate process lifetime");
            PeImage.Require(creation==verifiedCreation,"Process identity changed after preflight; no writes allowed");
            if(Block!=0) {ValidateBlock(); installed=true;Reset();return;}
            PeImage.Require(Read64(Iat)==Original,"IAT changed after preflight");
            Block=Native.VirtualAllocEx(memory,IntPtr.Zero,(UIntPtr)8192,0x3000,0x04).ToInt64();
            Native.Check(Block!=0,"Allocate clock pages");
            bool threadStarted=false;
            try
            {
                Write(Block,Code);
                byte[] data=new byte[96];
                Action<int,long> put=(o,v)=>Buffer.BlockCopy(BitConverter.GetBytes(v),0,data,o,8);
                long now=Native.Now();put(0,Magic);put(8,Original);put(24,now);put(32,now);put(40,1);put(48,1);put(56,now);
                Write(Block+StateOffset,data);
                uint old;
                Native.Check(Native.VirtualProtectEx(memory,new IntPtr(Block),(UIntPtr)4096,0x20,out old),"Protect clock code RX");
                Native.Check(Native.FlushInstructionCache(memory,new IntPtr(Block),(UIntPtr)Code.Length),"Flush clock instructions");
                ValidateBlock();
                PeImage.Require(Read64(Iat)==Original,"IAT changed during allocation");
                Native.Check(Native.VirtualProtectEx(memory,new IntPtr(Iat),(UIntPtr)8,0x04,out old),"Temporarily open IAT page");
                try
                {
                    using(var thread=Native.CreateRemoteThread(memory,IntPtr.Zero,UIntPtr.Zero,new IntPtr(Block+512),new IntPtr(Iat),0,IntPtr.Zero))
                    {
                        Native.Check(!thread.IsInvalid,"Start atomic IAT installer"); threadStarted=true;
                        PeImage.Require(Native.WaitForSingleObject(thread,5000)==0,"Installer timeout; restart game before retrying");
                        uint result;Native.Check(Native.GetExitCodeThread(thread,out result),"Installer result");
                        PeImage.Require(result==1 && Read64(Iat)==Block,"Atomic install refused a changed IAT");
                        installed=true;
                    }
                }
                finally { uint ignored;Native.Check(Native.VirtualProtectEx(memory,new IntPtr(Iat),(UIntPtr)8,old,out ignored),"Restore IAT page protection"); }
                Log("Installed clock-only wrapper; active 1x. Code RX, separate data RW. Game files unchanged.");
            }
            catch
            {
                if(!threadStarted) {Native.VirtualFreeEx(memory,new IntPtr(Block),UIntPtr.Zero,0x8000);Block=0;}
                // After a thread starts, memory must remain resident even on uncertain completion.
                throw;
            }
        }
    }
    internal long Active { get { lock(sync) {return installed?Read64(Block+StateOffset+40):1;} } }
    internal void Heartbeat()
    {
        lock(sync)
        {
            PeImage.Require(installed && Read64(Iat)==Block,"Clock disconnected");
            PeImage.Require(Read64(Block+StateOffset)==Magic && Read64(Block+StateOffset+8)==Original,"Clock identity changed; refusing heartbeat write");
            long now=Native.Now();
            if(now-lastTargetCheck>=Frequency) {VerifyTargetSelection();now=Native.Now();}
            if(LastBeat!=0 && now-LastBeat>Frequency/2) {Write64(Block+StateOffset+48,1);Log("Late heartbeat: requested normal 1x.");}
            Write64(Block+StateOffset+56,now+Frequency);LastBeat=now;
        }
    }
    internal void Set(int rate)
    {
        PeImage.Require(Allowed(rate),"Unsupported multiplier");
        lock(sync)
        {
            PeImage.Require(installed,"Clock not installed");ValidateBlock();Heartbeat();
            Write64(Block+StateOffset+48,rate);Log("Requested multiplier "+rate+"x.");
        }
    }
    internal void Reset()
    {
        lock(sync)
        {
            if(!installed || Game.HasExited)return;
            ValidateBlock();PeImage.Require(Read64(Iat)==Block,"Clock ownership lost; refusing reset write");
            Write64(Block+StateOffset+48,1);
            Log("Reset requested: 1x. Resident clock keeps timestamp continuity until game exits.");
            var wait=Stopwatch.StartNew();
            while(wait.ElapsedMilliseconds<500) {if(Active==1) {Log("Reset confirmed: active 1x.");return;}Thread.Sleep(10);}
            Log("Reset pending next QPC call; one-second lease is the fallback.");
        }
    }
    public void Dispose()
    {
        lock(sync)
        {
            try {Reset();}catch(Exception e){Log("Reset error: "+e.Message+". Clock lease falls back to 1x without controller.");}
            installed=false;if(memory!=null){memory.Dispose();memory=null;} if(imageLock!=null)imageLock.Dispose();if(Game!=null)Game.Dispose();
        }
    }
}
