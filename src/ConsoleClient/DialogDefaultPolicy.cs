using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleClient
{
    internal static class DialogDefaultPolicy
    {
        internal static int Preferred(IList<string> labels)
        {
            foreach (var label in new[] { "cancel", "close", "back", "no", "keep" })
                for (int i = 0; i < labels.Count; i++)
                    if ((labels[i] ?? "").Replace("_", "").Trim().Equals(label, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }
}
