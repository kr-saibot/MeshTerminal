using System;
using System.Security.Cryptography;
namespace MeshTerminal.Client
{
    public static class TelnetPassword
    {
        public static string Hash(string password)
        {
            if (String.IsNullOrEmpty(password)) throw new ArgumentException("Enter a password.");
            var salt = new byte[16]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(salt);
            using (var derive = new Rfc2898DeriveBytes(password, salt, 100000))
                return "pbkdf2:100000:" + Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(derive.GetBytes(32));
        }
        public static bool Verify(string password, string hash)
        {
            try
            {
                var parts = (hash ?? "").Split(':');
                if (parts.Length != 4 || parts[0] != "pbkdf2" || parts[1] != "100000") return false;
                var salt = Convert.FromBase64String(parts[2]); var expected = Convert.FromBase64String(parts[3]);
                if (salt.Length != 16 || expected.Length != 32) return false;
                using (var derive = new Rfc2898DeriveBytes(password, salt, 100000))
                {
                    var actual = derive.GetBytes(32); int difference = 0;
                    for (int i = 0; i < 32; i++) difference |= actual[i] ^ expected[i];
                    return difference == 0;
                }
            }
            catch { return false; }
        }
    }
}