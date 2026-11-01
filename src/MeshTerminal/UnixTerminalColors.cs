using System;
using System.IO;
using System.Linq;

namespace MeshTerminal
{
    internal static class UnixTerminalColors
    {
        internal static string Normalize(string term, Func<string, bool> available)
        {
            if (String.Equals(term, "xterm", StringComparison.OrdinalIgnoreCase) || String.Equals(term, "xterm-color", StringComparison.OrdinalIgnoreCase))
                return available("xterm-256color") ? "xterm-256color" : term;
            if (String.Equals(term, "screen", StringComparison.OrdinalIgnoreCase))
                return available("screen-256color") ? "screen-256color" : term;
            if (String.Equals(term, "tmux", StringComparison.OrdinalIgnoreCase))
                return available("tmux-256color") ? "tmux-256color" : available("screen-256color") ? "screen-256color" : term;
            return term;
        }
        internal static bool HasDefinition(string term)
        {
            if (String.IsNullOrEmpty(term)) return false;
            var roots = new[] { Environment.GetEnvironmentVariable("TERMINFO"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".terminfo"),
                "/etc/terminfo", "/lib/terminfo", "/usr/lib/terminfo", "/usr/share/terminfo", "/usr/share/misc/terminfo", "/usr/local/share/terminfo" }
                .Concat((Environment.GetEnvironmentVariable("TERMINFO_DIRS") ?? "").Split(':')).Where(root => !String.IsNullOrEmpty(root));
            return roots.Any(root => File.Exists(Path.Combine(root, term[0].ToString(), term)) ||
                File.Exists(Path.Combine(root, ((int)term[0]).ToString("x"), term)));
        }
    }
}
