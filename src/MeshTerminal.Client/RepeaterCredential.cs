using System;
using System.Security.Cryptography;
using System.Text;

namespace MeshTerminal.Client
{
    public sealed class RepeaterCredential
    {
        public string PublicKey { get; set; }
        public string ProtectedPassword { get; set; }
        public static RepeaterCredential Create(string publicKey, string password)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(password ?? "");
            try { return new RepeaterCredential { PublicKey = publicKey, ProtectedPassword = Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)) }; }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
        public string Read()
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedPassword), null, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(bytes); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
    }
}
