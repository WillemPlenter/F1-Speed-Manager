using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static class Native
{
    internal const uint ReadAccess = 0x410, WriteAccess = 0x43A;
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool ReadProcessMemory(SafeProcessHandle p, IntPtr address, byte[] data, UIntPtr size, out UIntPtr read);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool WriteProcessMemory(SafeProcessHandle p, IntPtr address, byte[] data, UIntPtr size, out UIntPtr written);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr VirtualAllocEx(SafeProcessHandle p, IntPtr address, UIntPtr size, uint type, uint protection);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool VirtualFreeEx(SafeProcessHandle p, IntPtr address, UIntPtr size, uint type);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool VirtualProtectEx(SafeProcessHandle p, IntPtr address, UIntPtr size, uint protection, out uint old);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern UIntPtr VirtualQueryEx(SafeProcessHandle p, IntPtr address, out MemoryInfo info, UIntPtr size);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool FlushInstructionCache(SafeProcessHandle p, IntPtr address, UIntPtr size);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern SafeWaitHandle CreateRemoteThread(SafeProcessHandle p, IntPtr attrs, UIntPtr stack, IntPtr start, IntPtr parameter, uint flags, IntPtr tid);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern uint WaitForSingleObject(SafeWaitHandle handle, uint timeout);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool GetExitCodeThread(SafeWaitHandle handle, out uint code);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true)] internal static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")] internal static extern bool QueryPerformanceCounter(out long value);
    [DllImport("kernel32.dll")] internal static extern bool QueryPerformanceFrequency(out long value);
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool IsWow64Process(SafeProcessHandle p, out bool wow64);
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool GetProcessTimes(SafeProcessHandle p,out long creation,out long exit,out long kernel,out long user);
    [StructLayout(LayoutKind.Sequential)] internal struct MemoryInfo
    { internal IntPtr BaseAddress, AllocationBase; internal uint AllocationProtect; internal ushort PartitionId; internal UIntPtr RegionSize; internal uint State, Protect, Type; }
    internal static void Check(bool ok, string operation) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), operation); }
    internal static long Now() { long n; Check(QueryPerformanceCounter(out n), "QPC"); return n; }
}

internal sealed class SafeProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeProcessHandle() : base(true) {}
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    protected override bool ReleaseHandle() { return CloseHandle(handle); }
}
