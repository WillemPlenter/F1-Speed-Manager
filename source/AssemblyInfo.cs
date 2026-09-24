using System.Reflection;
[assembly: AssemblyTitle("F1 Speed Manager")]
[assembly: AssemblyDescription("Clock-only speed controller for verified F1 Manager 2023 and 2024 Steam builds")]
[assembly: AssemblyProduct("F1 Speed Manager")]
[assembly: AssemblyCompany("SkaffaWilly")]
[assembly: AssemblyVersion(AppVersion.Numeric)]
[assembly: AssemblyFileVersion(AppVersion.Numeric)]
[assembly: AssemblyInformationalVersion(AppVersion.Number)]
[assembly: AssemblyCopyright("Copyright (c) 2026 Willem Plenter (SkaffaWilly)")]

internal static class AppVersion
{
    internal const string Number="2.1";
    internal const string Numeric="2.1.0.0";
    internal const string WindowTitle="F1 Speed Manager "+Number;
}
