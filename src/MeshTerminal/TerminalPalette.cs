namespace MeshTerminal
{
    internal static class TerminalPalette
    {
        // ANSI order: black, red, green, yellow, blue, magenta, cyan, white; then bright variants.
        internal static readonly uint[] Rgb =
        {
            0x000000, 0x800000, 0x008000, 0x808000, 0x000080, 0x800080, 0x008080, 0xC0C0C0,
            0x808080, 0xFF0000, 0x00FF00, 0xFFFF00, 0x0000FF, 0xFF00FF, 0x00FFFF, 0xFFFFFF
        };
    }
}
