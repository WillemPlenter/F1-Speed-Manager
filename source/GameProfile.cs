using System;
using System.Collections.Generic;
using System.IO;

internal sealed class GameProfile
{
    internal readonly string DisplayName, ProcessName, AppId, BuildId, Hash;
    internal readonly long Magic;
    internal static readonly GameProfile[] All = {
        new GameProfile("F1 Manager 2023", "F1Manager23", "2287220", "16843164",
            "d6e8f3ba892d65e947836f90e81ad590fef4720f6a2e6641832482427b8afc36", 0x31504351334D3146),
        new GameProfile("F1 Manager 2024", "F1Manager24", "2591280", "17356935",
            "1198feb7b1f39653fe51f04c9b0acb4d125a67d0e8ba6bda1fa353b3368ab356", 0x31504351344D3146)
    };
    GameProfile(string name,string process,string app,string build,string hash,long magic)
    { DisplayName=name;ProcessName=process;AppId=app;BuildId=build;Hash=hash;Magic=magic; }

    internal static GameProfile FromExecutable(string path)
    {
        if(String.IsNullOrEmpty(path))return null;
        string full=Path.GetFullPath(path);
        foreach(var profile in All)
            if(full.EndsWith("\\"+profile.ProcessName+"\\Binaries\\Win64\\"+profile.ProcessName+".exe",StringComparison.OrdinalIgnoreCase))return profile;
        return null;
    }
    // The launchers and unrelated executables do not count as game instances.
    internal static int SelectUnique(IList<string> paths)
    {
        int selected=-1;
        for(int i=0;i<paths.Count;i++)
        {
            if(FromExecutable(paths[i])==null)continue;
            if(selected>=0)throw new InvalidOperationException("Multiple game instances. Close all but one game, then reopen this tool.");
            selected=i;
        }
        return selected;
    }
    internal void ValidateManifest(string app,string build)
    {
        PeImage.Require(app==AppId,"Steam app identity mismatch for "+DisplayName);
        PeImage.Require(build==BuildId,"Unsupported Steam build for "+DisplayName+"; no writes allowed");
    }
    internal void ValidateHash(string hash)
    { PeImage.Require(hash==Hash,"Unsupported executable hash for "+DisplayName+"; no writes allowed"); }
}
