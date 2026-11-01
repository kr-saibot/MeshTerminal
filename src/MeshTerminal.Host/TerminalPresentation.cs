using System.Reflection;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EasyWindowsTerminalControl;
using Microsoft.Terminal.Wpf;

namespace MeshTerminal.Host
{
    internal static class TerminalPresentation
    {
        public static void HideScrollBar(EasyTerminalControl terminal)
        {
            if (terminal == null || terminal.Terminal == null)
                return;

            var field = terminal.Terminal.GetType().GetField("scrollbar", BindingFlags.Instance | BindingFlags.NonPublic);
            var scrollBar = field == null ? null : field.GetValue(terminal.Terminal) as ScrollBar;
            if (scrollBar != null)
                scrollBar.Visibility = Visibility.Collapsed;
        }

        public static void SetFontSize(EasyTerminalControl terminal, int size)
        {
            if (terminal == null || terminal.Terminal == null)
                return;

            terminal.FontSizeWhenSettingTheme = size;
            terminal.Terminal.SetTheme(CreateTheme(), terminal.FontFamilyWhenSettingTheme.Source, (short)size, Colors.Black);
            HideScrollBar(terminal);
        }

        private static TerminalTheme CreateTheme()
        {
            return new TerminalTheme
            {
                DefaultBackground = 0x00000000,
                DefaultForeground = 0x00C0C0C0,
                DefaultSelectionBackground = 0x00808080,
                CursorStyle = CursorStyle.BlinkingBlockDefault,
                // TerminalTheme uses COLORREF (BGR), the shared palette stores RGB.
                ColorTable = TerminalPalette.Rgb.Select(rgb => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | (rgb >> 16)).ToArray()
            };
        }
    }
}
