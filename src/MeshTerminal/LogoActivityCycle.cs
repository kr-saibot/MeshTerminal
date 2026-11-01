using System;
namespace MeshTerminal
{
    internal sealed class LogoActivityCycle
    {
        private sealed class Axis
        {
            internal int Offset, Maximum, Step;
            internal bool Returning, Done;
            internal void Advance(bool pingPong)
            {
                if (Done) return;
                if (Returning) { Offset = Math.Max(0, Offset - Step); Done = Offset == 0; return; }
                if (Offset == Maximum)
                {
                    if (pingPong) { Returning = true; Offset = Math.Max(0, Offset - Step); Done = Offset == 0; }
                    else { Offset = 0; Done = true; }
                }
                else Offset = (int)Math.Min(Maximum, (long)Offset + Step);
            }
        }
        private readonly Axis _vertical, _horizontal;
        private readonly bool _pingPong;
        internal int VerticalOffset { get { return _vertical.Offset; } }
        internal int HorizontalOffset { get { return _horizontal.Offset; } }
        internal bool Active { get { return !_vertical.Done || !_horizontal.Done; } }
        internal LogoActivityCycle(int maximumVertical, int maximumHorizontal, int verticalStep, int horizontalStep, bool pingPong)
        {
            _vertical = new Axis { Maximum = Math.Max(0, maximumVertical), Step = Math.Max(1, verticalStep), Done = maximumVertical <= 0 };
            _horizontal = new Axis { Maximum = Math.Max(0, maximumHorizontal), Step = Math.Max(1, horizontalStep), Done = maximumHorizontal <= 0 };
            _pingPong = pingPong;
        }
        internal void Advance() { _vertical.Advance(_pingPong); _horizontal.Advance(_pingPong); }
    }
}
