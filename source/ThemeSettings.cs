using System;
using System.IO;
using System.Text;

internal sealed class ThemeSettings
{
    internal readonly ColorMode Mode;
    internal static string ConfigPath {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"F1 Speed Manager.theme.ini");}}
    internal ThemeSettings(ColorMode mode)
    {if(!Enum.IsDefined(typeof(ColorMode),mode))throw new ArgumentException("Unknown color mode.");Mode=mode;}
    internal static ThemeSettings Defaults {get{return new ThemeSettings(ColorMode.Red);}}
    internal string Serialize(){return "# F1 Speed Manager color mode. Use the menu in the application.\nColor="+Mode+"\n";}
    internal static ThemeSettings Parse(string text)
    {
        if(text==null || text.Length>1024)throw new ArgumentException("Invalid color settings size.");
        ColorMode mode=ColorMode.Red;bool found=false;
        foreach(string raw in text.Split('\n'))
        {
            string line=raw.Trim();if(line.Length==0 || line.StartsWith("#"))continue;
            int split=line.IndexOf('=');if(split<0 || !line.Substring(0,split).Trim().Equals("Color",StringComparison.OrdinalIgnoreCase) || found)
                throw new ArgumentException("Unknown color setting: "+line);
            string value=line.Substring(split+1).Trim();
            if(!Enum.TryParse<ColorMode>(value,true,out mode) || !Enum.IsDefined(typeof(ColorMode),mode) || !value.Equals(mode.ToString(),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Unknown color mode: "+value);
            found=true;
        }
        if(!found)throw new ArgumentException("Missing Color setting.");
        return new ThemeSettings(mode);
    }
    internal static ThemeSettings Load()
    {
        if(!File.Exists(ConfigPath))return Defaults;
        if(new FileInfo(ConfigPath).Length>1024)throw new InvalidDataException("Color settings file too large.");
        try{return Parse(File.ReadAllText(ConfigPath));}
        catch(ArgumentException e){throw new InvalidDataException("Invalid F1 Speed Manager.theme.ini: "+e.Message+" Rename that file to restore red.",e);}
    }
    internal void Save()
    {
        string temp=ConfigPath+".tmp";File.WriteAllText(temp,Serialize(),new UTF8Encoding(false));
        if(File.Exists(ConfigPath))File.Replace(temp,ConfigPath,null);else File.Move(temp,ConfigPath);
    }
}
