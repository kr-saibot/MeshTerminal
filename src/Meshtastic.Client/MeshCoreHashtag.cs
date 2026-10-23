using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Meshtastic.Client
{
    public static class MeshCoreHashtag
    {
        // MeshCore companion protocol: first 16 bytes of SHA-256 of the UTF-8
        // name including '#'. Preserve case and spelling; do not normalize.
        public static byte[] DeriveKey(string name)
        {
            if (String.IsNullOrEmpty(name) || !name.StartsWith("#", StringComparison.Ordinal) || name.Length < 2 || name.IndexOf('\0') >= 0 || Encoding.UTF8.GetByteCount(name) > 31)
                throw new ArgumentException("Enter a hashtag name starting with # (2-31 UTF-8 bytes).");
            using (var hash = SHA256.Create()) return hash.ComputeHash(Encoding.UTF8.GetBytes(name)).Take(16).ToArray();
        }

        public static bool Matches(string name, byte[] key)
        {
            try { return key != null && DeriveKey(name).SequenceEqual(key); }
            catch (ArgumentException) { return false; }
        }
    }
}
