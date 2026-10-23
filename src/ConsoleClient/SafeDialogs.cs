using System;
using System.Collections.Generic;
using System.Linq;
using NStack;
using Terminal.Gui;

namespace ConsoleClient
{
    internal sealed class SafeDialog : Terminal.Gui.Dialog
    {
        internal SafeDialog() : base() { Ready += ApplySafeDefault; }
        internal SafeDialog(ustring title, int width, int height, params Button[] buttons) : base(title, width, height, buttons) { Ready += ApplySafeDefault; }
        internal SafeDialog(ustring title, params Button[] buttons) : base(title, buttons) { Ready += ApplySafeDefault; }
        private static IEnumerable<Button> Buttons(View view)
        {
            foreach (var child in view.Subviews)
            {
                var button = child as Button;
                if (button != null) yield return button;
                foreach (var nested in Buttons(child)) yield return nested;
            }
        }
        private void ApplySafeDefault()
        {
            var buttons = Buttons(this).Where(b => b.Visible && b.Enabled).ToList();
            var index = DialogDefaultPolicy.Preferred(buttons.Select(b => b.Text.ToString()).ToList());
            if (index < 0) return;
            foreach (var button in buttons) button.IsDefault = false;
            buttons[index].IsDefault = true;
            buttons[index].SetFocus();
        }
    }
    internal static class SafeMessageBox
    {
        private static int Index(ustring[] buttons)
        { return Math.Max(0, DialogDefaultPolicy.Preferred(buttons.Select(b => b == null ? "" : b.ToString()).ToList())); }
        public static int Query(ustring title, ustring message, params ustring[] buttons)
        { return Terminal.Gui.MessageBox.Query(title, message, Index(buttons), buttons); }
        public static int ErrorQuery(ustring title, ustring message, params ustring[] buttons)
        { return Terminal.Gui.MessageBox.ErrorQuery(title, message, Index(buttons), buttons); }
        public static int Query(int width, int height, ustring title, ustring message, params ustring[] buttons)
        { return Terminal.Gui.MessageBox.Query(width, height, title, message, Index(buttons), buttons); }
        public static int ErrorQuery(int width, int height, ustring title, ustring message, params ustring[] buttons)
        { return Terminal.Gui.MessageBox.ErrorQuery(width, height, title, message, Index(buttons), buttons); }
    }
}
