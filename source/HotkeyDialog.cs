using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class ShortcutField : TextBox
{
    readonly ShortcutCapture capture=new ShortcutCapture();
    internal Action<string> Feedback;
    internal ShortcutField()
    {ReadOnly=true;ShortcutsEnabled=false;BackColor=F1Theme.Input;ForeColor=Color.White;BorderStyle=BorderStyle.FixedSingle;Cursor=Cursors.Hand;}
    protected override void OnEnter(EventArgs e){capture.Reset();SelectAll();base.OnEnter(e);}
    protected override void OnLeave(EventArgs e){capture.Reset();base.OnLeave(e);}
    public override bool PreProcessMessage(ref Message message)
    {
        int kind=message.Msg;bool down=kind==0x100 || kind==0x104;
        if(down || kind==0x101 || kind==0x105)
        {
            uint modifiers=((ModifierKeys&Keys.Control)!=0?2u:0u)|((ModifierKeys&Keys.Alt)!=0?1u:0u)|((ModifierKeys&Keys.Shift)!=0?4u:0u);
            try{var shortcut=capture.Process((Keys)message.WParam.ToInt32(),down,modifiers);if(shortcut!=null){Text=shortcut.Text;SelectAll();if(Feedback!=null)Feedback("");}}
            catch(ArgumentException e){if(Feedback!=null)Feedback(e.Message);}
            return true; // Includes navigation and dialog keys: capture, never edit text or submit.
        }
        return base.PreProcessMessage(ref message);
    }
}

internal sealed class HotkeyDialog : Form
{
    readonly ShortcutField[] fields=new ShortcutField[9];
    readonly Label error=new Label();
    readonly Func<HotkeySettings,string> apply;
    internal HotkeyDialog(HotkeySettings settings,ColorMode mode,Func<HotkeySettings,string> apply)
    {
        this.apply=apply;Text=AppVersion.WindowTitle+" — Hotkeys";ClientSize=new Size(470,455);
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
        StartPosition=FormStartPosition.CenterParent;BackColor=F1Theme.Background;ForeColor=Color.White;Font=new Font("Segoe UI",9);AutoScaleMode=AutoScaleMode.Dpi;
        var help=new Label{Text="Click a field, then press the desired key or combination.\nFor F7+Up: hold F7, press Up, then release both keys.",Bounds=new Rectangle(16,12,438,40),ForeColor=F1Theme.Muted};Controls.Add(help);
        foreach(HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
        {
            int index=(int)action;
            Controls.Add(new Label{Text=HotkeySettings.Label(action),Bounds=new Rectangle(16,60+index*33,200,26),TextAlign=ContentAlignment.MiddleLeft,ForeColor=F1Theme.ActionColor(mode,index)});
            fields[index]=new ShortcutField{Text=settings[action].Text,Bounds=new Rectangle(230,60+index*33,224,26),AccessibleName=HotkeySettings.Label(action),Feedback=text=>error.Text=text};Controls.Add(fields[index]);
        }
        error.SetBounds(16,359,438,48);error.ForeColor=F1Theme.Error;Controls.Add(error);
        var defaults=new Button{Text="Restore defaults",Bounds=new Rectangle(16,413,130,28)};
        F1Theme.StyleButton(defaults,F1Theme.ActionColor(mode,2));
        defaults.Click+=(s,e)=>{var values=HotkeySettings.Defaults;foreach(HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))fields[(int)action].Text=values[action].Text;error.Text="";};
        var save=new Button{Text="Save",Bounds=new Rectangle(258,413,94,28)};
        Color saveColor=mode==ColorMode.Multicolor?F1Theme.Green:F1Theme.ModeColor(mode);
        F1Theme.StyleButton(save,saveColor);save.BackColor=saveColor;save.ForeColor=F1Theme.Background;save.Font=new Font(Font,FontStyle.Bold);
        save.Click+=(s,e)=>Save();
        var cancel=new Button{Text="Cancel",Bounds=new Rectangle(360,413,94,28),DialogResult=DialogResult.Cancel};
        F1Theme.StyleButton(cancel,F1Theme.ActionColor(mode,6));
        Controls.Add(defaults);Controls.Add(save);Controls.Add(cancel);AcceptButton=save;CancelButton=cancel;
    }
    void Save()
    {
        try
        {
            var values=new Shortcut[9];foreach(HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))values[(int)action]=Shortcut.Parse(fields[(int)action].Text);
            string failure=apply(new HotkeySettings(values));
            if(failure!=null){error.Text=failure;return;}
            DialogResult=DialogResult.OK;Close();
        }
        catch(ArgumentException e){error.Text=e.Message;}
    }
}
