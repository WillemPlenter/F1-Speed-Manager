using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.ComponentModel;
using System.Windows.Forms;

internal static class Program
{
    internal enum Mode { Gui, Inspect, SelfTest }
    internal static Mode ParseArguments(string[] args)
    {
        if(args.Length==0)return Mode.Gui;
        if(args.Length==1 && args[0]=="--inspect")return Mode.Inspect;
        if(args.Length==1 && args[0]=="--self-test")return Mode.SelfTest;
        throw new ArgumentException("Use no arguments for the window, --inspect, or --self-test.");
    }
    static readonly object logLock=new object();
    internal static void Log(string text)
    {
        lock(logLock)
        {
            string line=DateTimeOffset.Now.ToString("o")+" "+text;Console.WriteLine(line);
            try {File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"F1 Speed Manager.log"),line+Environment.NewLine);}
            catch(IOException e){Debug.WriteLine("Log failed: "+e.Message);}
            catch(UnauthorizedAccessException e){Debug.WriteLine("Log failed: "+e.Message);}
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            // Reject unsupported command lines before constructing a game controller.
            Mode mode=ParseArguments(args);
            if(mode==Mode.SelfTest)return ClockTests.Run();
            // Fail before attaching if this folder cannot store diagnostic logs.
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"F1 Speed Manager.log"),DateTimeOffset.Now.ToString("o")+" Starting F1 Speed Manager 1.0.0"+Environment.NewLine);
            bool created;
            // Keep the legacy name to prevent older editions running alongside this one.
            using(var mutex=new Mutex(true,@"Local\F1Manager2023SpeedOnly",out created))
            {
                if(!created)throw new InvalidOperationException("Another controller is already running");
                if(mode==Mode.Gui)
                {
                    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                    using(var form=new SpeedForm())Application.Run(form);
                    return 0;
                }
                using(var engine=new Engine(Log))
                {
                    return engine.Discover()?0:2;
                }
            }
        }
        catch(Exception e)
        {
            var win=e as Win32Exception;string message=e.Message+(win==null?"":" (Windows error "+win.NativeErrorCode+")");
            Log("ERROR: "+message);
            if(args.Length==0)MessageBox.Show(message,"F1 Speed Manager — stopped safely",MessageBoxButtons.OK,MessageBoxIcon.Error);
            return 1;
        }
    }
}
