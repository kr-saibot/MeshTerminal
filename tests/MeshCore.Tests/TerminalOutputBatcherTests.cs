using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MeshTerminal.Host;

internal static class TerminalOutputBatcherTests
{
    private static void Check(bool condition, string text) { if (!condition) throw new Exception("Terminal output: " + text); }
    internal static void Run()
    {
        var output = new List<string>();
        using (var batcher = new TerminalOutputBatcher(text => output.Add(text)))
        {
            batcher.Append("\u001b[?25l\u001b[".AsSpan());
            batcher.Append("3;4Hhello".AsSpan());
            batcher.Append("\b!\u001b[?25h".AsSpan());
            batcher.Flush();
            Check(String.Join("", output) == "\u001b[?25l\u001b[3;4Hhello\b!\u001b[?25h", "Split cursor sequences and backspace must survive byte-for-byte");
        }
        using (var ready = new ManualResetEventSlim())
        {
            string startup = null;
            using (var batcher = new TerminalOutputBatcher(text => { startup = text; ready.Set(); }))
            {
                batcher.Append("Startup screen".AsSpan());
                Check(ready.Wait(2000) && startup == "Startup screen", "Initial screen is delivered automatically without further output or a UI dispatcher");
            }
        }
        output.Clear();
        var retiring = new TerminalOutputBatcher(text => output.Add(text));
        retiring.Append("Final screen".AsSpan()); retiring.Dispose(); retiring.Append("old connection".AsSpan());
        Check(String.Join("", output) == "Final screen", "Disposal flushes pending text and retires the old connection");
        output.Clear(); int attempts = 0, errors = 0;
        using (var batcher = new TerminalOutputBatcher(text => { if (++attempts == 1) throw new InvalidOperationException("temporary"); output.Add(text); }, ex => errors++))
        {
            batcher.Append("Keep startup".AsSpan()); batcher.Flush(); batcher.Flush();
            Check(errors == 1 && String.Join("", output) == "Keep startup", "A writer failure is reported and retains the complete screen for retry");
        }
        output.Clear();
        using (var batcher = new TerminalOutputBatcher(text => output.Add(text)))
        {
            batcher.Append(new string('x', 65536).AsSpan());
            Check(output.Sum(text => text.Length) == 65536, "Large screen updates flush without unbounded pending data");
        }
        Console.WriteLine("Terminal output tests passed: split frames, cursor controls, automatic startup, bounded chunks, retirement and error recovery.");
    }
}
