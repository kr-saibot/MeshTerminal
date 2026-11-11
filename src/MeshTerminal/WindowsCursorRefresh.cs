using Terminal.Gui;
namespace MeshTerminal
{
    internal static class WindowsCursorRefresh
    {
        internal static void Install()
        {
            // Terminal.Gui's native Windows driver paints before moving its hardware cursor.
            // ConPTY exposes that previous cursor position in the redraw. Move it first.
            if(Application.Driver?.GetType().FullName=="Terminal.Gui.WindowsDriver")
                Application.Iteration+=PrepareInputCursor;
        }
        private static void PrepareInputCursor()
        {
            var current=Application.Current;
            var focused=current?.MostFocused;
            if((focused is TextField field&&!field.ReadOnly)||(focused is TextView text&&!text.ReadOnly))
            {
                current.PositionCursor();
                Application.Driver.UpdateCursor();
            }
        }
    }
}
