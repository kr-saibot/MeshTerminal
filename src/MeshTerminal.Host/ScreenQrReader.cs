using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ZXing;
using ZXing.Common;
namespace MeshTerminal.Host
{
    internal static class ScreenQrReader
    {
        internal static string[] Decode(Bitmap image)
        {
            var reader = new BarcodeReader { AutoRotate = true, Options = new DecodingOptions { TryHarder = true, TryInverted = true, PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE } } };
            var results = reader.DecodeMultiple(image);
            if (results == null || results.Length == 0)
            {
                var single = reader.Decode(image);
                results = single == null ? new Result[0] : new[] { single };
            }
            return results.Where(r => !String.IsNullOrWhiteSpace(r.Text)).Select(r => r.Text).Distinct(StringComparer.Ordinal).ToArray();
        }
    }
}
