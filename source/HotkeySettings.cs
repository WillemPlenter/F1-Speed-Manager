using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

internal enum HotkeyAction { Normal, Double, Triple, Five, Ten, Reset, ToggleOverlay, PreviousPosition, NextPosition }

internal sealed class Shortcut
{
    internal readonly uint Modifiers;
    internal readonly Keys Key,Prefix;
    internal Shortcut(uint modifiers,Keys key,Keys prefix)
    {Modifiers=modifiers;Key=key;Prefix=prefix;Validate();}
    internal static bool FunctionKey(Keys key){return key>=Keys.F1 && key<=Keys.F24;}
    internal static bool ModifierKey(Keys key)
    {return key==Keys.ShiftKey || key==Keys.ControlKey || key==Keys.Menu || (key>=Keys.LShiftKey && key<=Keys.RMenu) || key==Keys.LWin || key==Keys.RWin;}
    void Validate()
    {
        if(Modifiers>7 || (int)Key<8 || (int)Key>254 || ModifierKey(Key) || Key==Keys.Escape || !Enum.IsDefined(typeof(Keys),Key))
            throw new ArgumentException("Use a valid key, not Escape or a modifier alone.");
        if(Prefix!=Keys.None && (!FunctionKey(Prefix) || Prefix==Key))throw new ArgumentException("A held prefix must be F1–F24 and different from the second key.");
        if(Prefix==Keys.None && Modifiers==0 && !FunctionKey(Key))throw new ArgumentException("Use Ctrl, Alt or Shift with a letter/navigation key, or use a function key.");
    }
    internal static Shortcut Parse(string text)
    {
        uint modifiers=0;var keys=new List<Keys>();
        foreach(string raw in (text??"").Split('+'))
        {
            string part=raw.Trim();uint bit=0;
            if(part.Equals("Ctrl",StringComparison.OrdinalIgnoreCase) || part.Equals("Control",StringComparison.OrdinalIgnoreCase))bit=2;
            else if(part.Equals("Alt",StringComparison.OrdinalIgnoreCase))bit=1;
            else if(part.Equals("Shift",StringComparison.OrdinalIgnoreCase))bit=4;
            if(bit!=0){if((modifiers&bit)!=0)throw new ArgumentException("Repeated modifier.");modifiers|=bit;continue;}
            if(part=="↑")part="Up";if(part=="↓")part="Down";
            Keys key;
            if(part.Length==0 || !Enum.TryParse<Keys>(part,true,out key) || part.Any(char.IsDigit) && part.All(char.IsDigit))
                throw new ArgumentException("Unknown key: "+part+". Example: Ctrl+F2 or F7+Up.");
            keys.Add(key);
        }
        if(keys.Count<1 || keys.Count>2)throw new ArgumentException("Use one key, or a held function key followed by a second key.");
        return new Shortcut(modifiers,keys[keys.Count-1],keys.Count==2?keys[0]:Keys.None);
    }
    internal string Text
    {
        get{return ((Modifiers&2)!=0?"Ctrl+":"")+((Modifiers&1)!=0?"Alt+":"")+((Modifiers&4)!=0?"Shift+":"")+(Prefix==Keys.None?"":Prefix+"+")+Key;}
    }
    internal string Root {get{return Modifiers+":"+(int)(Prefix==Keys.None?Key:Prefix);}}
    public override string ToString(){return Text;}
}

// Only receives key events from the focused settings field; it never listens globally.
internal sealed class ShortcutCapture
{
    readonly HashSet<Keys> pressed=new HashSet<Keys>();
    Keys prefix=Keys.None;uint prefixModifiers;bool chord;
    internal void Reset(){pressed.Clear();prefix=Keys.None;chord=false;}
    internal Shortcut Process(Keys key,bool down,uint modifiers)
    {
        if(!down){pressed.Remove(key);if(key==prefix){prefix=Keys.None;chord=false;}return null;}
        if(!pressed.Add(key) || Shortcut.ModifierKey(key))return null;
        if(key==Keys.Escape){Reset();return null;}
        if(prefix!=Keys.None)
        {
            if(chord)return null;
            if(modifiers!=prefixModifiers)throw new ArgumentException("Keep the same modifiers held while pressing the second key.");
            var value=new Shortcut(modifiers,key,prefix);chord=true;return value;
        }
        var single=new Shortcut(modifiers,key,Keys.None);
        if(Shortcut.FunctionKey(key)){prefix=key;prefixModifiers=modifiers;chord=false;}
        return single;
    }
}

internal sealed class HotkeySettings
{
    readonly Shortcut[] bindings;
    internal static string ConfigPath {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"F1 Speed Manager.hotkeys.ini");}}
    internal Shortcut this[HotkeyAction action] {get{return bindings[(int)action];}}
    internal HotkeySettings(Shortcut[] values)
    {bindings=(Shortcut[])values.Clone();Validate();}
    internal static HotkeySettings Defaults
    {get{return new HotkeySettings(new[]{Shortcut.Parse("F1"),Shortcut.Parse("F2"),Shortcut.Parse("F3"),Shortcut.Parse("F4"),Shortcut.Parse("F5"),Shortcut.Parse("F6"),Shortcut.Parse("F7"),Shortcut.Parse("F7+Up"),Shortcut.Parse("F7+Down")});}}
    void Validate()
    {
        if(bindings.Length!=9 || bindings.Any(b=>b==null))throw new ArgumentException("All nine shortcuts are required.");
        if(bindings.Select(b=>b.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=9)throw new ArgumentException("Two settings use the same shortcut. Choose unique combinations.");
    }
    internal string Serialize()
    {
        var lines=new List<string>{"# F1 Speed Manager hotkeys. Edit in the Hotkeys window."};
        foreach(HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))lines.Add(action+"="+this[action].Text);
        return string.Join(Environment.NewLine,lines)+Environment.NewLine;
    }
    internal static HotkeySettings Parse(string text)
    {
        if(text==null || text.Length>16384)throw new ArgumentException("Invalid hotkey settings size.");
        var result=new Shortcut[9];
        foreach(string raw in text.Split('\n'))
        {
            string line=raw.Trim();if(line.Length==0 || line.StartsWith("#"))continue;
            int split=line.IndexOf('=');HotkeyAction action;
            string name=split<0?"":line.Substring(0,split).Trim();
            if(split<0 || !Enum.TryParse<HotkeyAction>(name,true,out action) || !Enum.IsDefined(typeof(HotkeyAction),action) || !name.Equals(action.ToString(),StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Unknown hotkey setting: "+line);
            if(result[(int)action]!=null)throw new ArgumentException("Repeated hotkey setting: "+action);
            result[(int)action]=Shortcut.Parse(line.Substring(split+1));
        }
        return new HotkeySettings(result);
    }
    internal static HotkeySettings Load()
    {
        if(!File.Exists(ConfigPath))return Defaults;
        if(new FileInfo(ConfigPath).Length>16384)throw new InvalidDataException("Hotkey settings file too large.");
        try{return Parse(File.ReadAllText(ConfigPath));}
        catch(ArgumentException e){throw new InvalidDataException("Invalid F1 Speed Manager.hotkeys.ini: "+e.Message+" Rename that file to restore defaults.",e);}
    }
    internal void Save()
    {
        string temp=ConfigPath+".tmp";
        File.WriteAllText(temp,Serialize(),new UTF8Encoding(false));
        if(File.Exists(ConfigPath))File.Replace(temp,ConfigPath,null);else File.Move(temp,ConfigPath);
    }
    internal static string Label(HotkeyAction action)
    {
        string[] names={"Normal / 1x","Extra 2x","Extra 3x","Extra 5x","Extra 10x","Reset to 1x","Show / hide overlay","Previous overlay position","Next overlay position"};
        return names[(int)action];
    }
}

// Tracks only a configured held prefix and consumed second keys, never ordinary typing.
internal sealed class PrefixTracker
{
    readonly HotkeySettings settings;
    Keys held=Keys.None;uint modifiers;
    HotkeyAction? standalone;
    bool usedChord;
    internal bool Held {get{return held!=Keys.None;}}
    internal void CancelFallback(){if(Held)usedChord=true;}
    readonly HashSet<Keys> consumed=new HashSet<Keys>();
    internal PrefixTracker(HotkeySettings settings){this.settings=settings;}
    internal bool Process(Keys key,bool down,uint currentModifiers,bool inScope,bool sameFocus,out HotkeyAction? action)
    {
        action=null;
        if(consumed.Contains(key)){if(!down)consumed.Remove(key);return true;}
        if(held!=Keys.None && key==held)
        {
            if(!down){if(!usedChord && sameFocus && modifiers==currentModifiers)action=standalone;held=Keys.None;standalone=null;usedChord=false;}
            return true;
        }
        if(!down)return false;
        if(held!=Keys.None && modifiers==currentModifiers)
        {
            foreach(HotkeyAction candidate in Enum.GetValues(typeof(HotkeyAction)))
            {
                var binding=settings[candidate];
                if(binding.Prefix==held && binding.Key==key && binding.Modifiers==currentModifiers)
                {usedChord=true;if(!inScope || !sameFocus)return false;consumed.Add(key);action=candidate;return true;}
            }
        }
        if(held!=Keys.None)return false;
        foreach(HotkeyAction candidate in Enum.GetValues(typeof(HotkeyAction)))
        {
            var binding=settings[candidate];
            if(binding.Prefix!=Keys.None && binding.Prefix==key && binding.Modifiers==currentModifiers)
            {
                HotkeyAction? fallback=null;
                foreach(HotkeyAction basic in Enum.GetValues(typeof(HotkeyAction)))
                    if(settings[basic].Prefix==Keys.None && settings[basic].Key==key && settings[basic].Modifiers==currentModifiers)fallback=basic;
                if(!inScope && !fallback.HasValue)return false;
                held=key;modifiers=currentModifiers;standalone=fallback;usedChord=false;return true;
            }
        }
        return false;
    }
}
