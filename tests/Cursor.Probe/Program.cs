using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using EasyWindowsTerminalControl;
using Terminal.Gui;
using MeshTerminal;
using System.Text.RegularExpressions;
internal static class Program
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern IntPtr CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll")]private static extern bool SetStdHandle(int kind,IntPtr handle);
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Contains("--child"))
        {
            // The diagnostic launcher has redirected stdio; bind its console child to its own pseudoconsole.
            var input=CreateFile("CONIN$",0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
            var output=CreateFile("CONOUT$",0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
            if(input==new IntPtr(-1)||output==new IntPtr(-1))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            SetStdHandle(-10,input);SetStdHandle(-11,output);SetStdHandle(-12,output);
            if(args.Contains("--net"))Application.UseSystemConsole=true;
            if(args.Contains("--wt"))Environment.SetEnvironmentVariable("WT_SESSION","cursor-probe");
            Application.Init();
            if(args.Contains("--preposition"))WindowsCursorRefresh.Install();
            View field=args.Contains("--multiline")
                ? (View)new TextView {X=2,Y=2,Width=30,Height=3,Text="0123456789",CursorPosition=new Point(5,0)}
                : new TextField("0123456789") {X=2,Y=2,Width=30,CursorPosition=5};
            if(args.Contains("--message-frame"))
            {
                var window=new Window("Chat") { X=0,Y=0,Width=Dim.Fill(),Height=Dim.Fill() };
                var frame=new FrameView("New message") {X=0,Y=0,Width=Dim.Fill(11),Height=5};
                var text=(TextView)field;text.X=0;text.Y=0;text.Width=Dim.Fill();text.Height=Dim.Fill();text.WordWrap=true;
                var counter=new Label("") {X=Pos.Right(frame)-1,Y=Pos.Top(frame),Width=0};
                Action update=()=> {var value=" "+text.Text.Length+" chars | "+text.Text.Length+"/160 bytes ";counter.Text=value;counter.Width=value.Length;counter.X=Pos.Right(frame)-value.Length-1;counter.SetNeedsDisplay();};
                text.TextChanged+=()=>update();frame.Add(field);window.Add(frame,counter);Application.Top.Add(window);update();
            }
            else Application.Top.Add(field);
            Application.Top.Ready+=()=> {if(field is TextView view)view.CursorPosition=new Point(5,0);else if(field is TextField edit)edit.CursorPosition=5;field.SetFocus();field.SetNeedsDisplay();};
            field.SetFocus();Application.Top.KeyPress+=e=> {if(e.KeyEvent.Key==Key.Esc){Application.RequestStop();e.Handled=true;}};
            try {Application.Run();}finally {Application.Shutdown();}return 0;
        }
        foreach(var mode in new[] {"legacy","--preposition","--multiline","--multiline --preposition","--multiline --message-frame","--multiline --message-frame --preposition"})Run(mode);
        return 0;
    }
    private static void Run(string mode)
    {
        Console.WriteLine("=== "+mode+" ===");
        var pty=new TermPTY();var frames=new List<string>();var watch=Stopwatch.StartNew();string stage="start";
        pty.InterceptOutputToUITerminal=(ref Span<char> data)=> {lock(frames)frames.Add(watch.ElapsedMilliseconds+" "+stage+" "+Escape(data.ToString()));};
        var path=typeof(Program).Assembly.Location;
        var run=Task.Run(()=>pty.Start("\""+path+"\" --child "+(mode=="legacy"?"":mode),80,25,false,null,Path.GetDirectoryName(path)));
        try
        {
            var deadline=DateTime.UtcNow.AddSeconds(10);
            while(!pty.TermProcIsStarted&&!run.IsCompleted&&DateTime.UtcNow<deadline)Thread.Sleep(10);
            if(!pty.TermProcIsStarted)throw new InvalidOperationException("Probe child failed to start",run.Exception);
            Thread.Sleep(700);
            stage="left";pty.WriteToTerm("\u001b[D".AsSpan());Thread.Sleep(250);
            stage="backspace";pty.WriteToTerm("\u001b[8;14;8;1;0;1_".AsSpan());Thread.Sleep(250);
            stage="right";pty.WriteToTerm("\u001b[C".AsSpan());Thread.Sleep(250);
            stage="delete";pty.WriteToTerm("\u001b[3~".AsSpan());Thread.Sleep(250);
            stage="exit";pty.WriteToTerm("\u001b".AsSpan());
            run.Wait(1500);
        }
        finally
        {
            if(!run.IsCompleted)try{pty.StopExternalTermOnly();}catch(ArgumentException){}
            try{run.Wait(3000);}catch(AggregateException e){Console.WriteLine(e.GetBaseException().Message);}
            lock(frames)
            {
                foreach(var frame in frames)Console.WriteLine(Regex.Replace(frame," {8,}","<spaces>"));
                if(mode.Contains("--preposition"))
                {
                    foreach(var sample in new[] { Tuple.Create("left",7,8),Tuple.Create("backspace",6,7),Tuple.Create("right",7,6) })
                    {
                        var output=String.Join("",frames.Where(f=>f.Contains(" "+sample.Item1+" ")));
                        var expected="<ESC>[3;"+sample.Item2+"H<ESC>[?25h";
                        var obsolete="<ESC>[3;"+sample.Item3+"H<ESC>[?25h";
                        if((!output.Contains(expected)&&!(sample.Item1=="right"&&output.Contains("<ESC>[1C")))||output.Contains(obsolete))throw new Exception("Cursor regression: "+mode+" / "+sample.Item1);
                    }
                    Console.WriteLine("Cursor regression passed: "+mode);
                }
            }
        }
    }
    private static string Escape(string text)
    {
        var result=new StringBuilder();foreach(char c in text) {if(c=='\u001b')result.Append("<ESC>");else if(c<' ')result.Append("<"+((int)c).ToString("X2")+">");else result.Append(c);}return result.ToString();
    }
}
