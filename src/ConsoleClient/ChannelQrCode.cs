using System;
using System.Collections.Generic;
using System.Collections;
using QRCoder;

namespace ConsoleClient
{
    internal static class ChannelQrCode
    {
        public static QRCodeData Create(string uri)
        {
            using (var generator = new QRCodeGenerator())
                return generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        }

        public static string[] HalfBlockRows(IList<BitArray> matrix)
        {
            var rows = new string[(matrix.Count + 1) / 2];
            for (var y = 0; y < rows.Length; y++)
            {
                var line = new char[matrix.Count];
                for (var x = 0; x < line.Length; x++)
                {
                    var upper = matrix[y * 2][x];
                    var lower = y * 2 + 1 < matrix.Count && matrix[y * 2 + 1][x];
                    line[x] = upper ? (lower ? '█' : '▀') : (lower ? '▄' : ' ');
                }
                rows[y] = new string(line);
            }
            return rows;
        }

        public static byte[] Png(QRCodeData data)
        { return new PngByteQRCode(data).GetGraphic(12); }
    }
}
