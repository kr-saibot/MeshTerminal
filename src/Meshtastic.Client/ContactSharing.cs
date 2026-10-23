using System;
using System.Linq;
using Google.Protobuf;
using Meshtastic.Protobufs;
namespace Meshtastic.Client
{
    public static class ContactSharing
    {
        public static string CreateMeshtasticUri(uint nodeNumber, User user)
        {
            if (user == null) throw new ArgumentNullException("user");
            if (nodeNumber == 0 || nodeNumber == UInt32.MaxValue) throw new ArgumentOutOfRangeException("nodeNumber");
            var contact = new SharedContact { NodeNum = nodeNumber, User = user.Clone() };
            return "https://meshtastic.org/v/#" + Convert.ToBase64String(contact.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        public static string CreateMeshCoreUri(string name, byte[] publicKey, int type = 1)
        {
            if (publicKey == null || publicKey.Length != 32) throw new ArgumentException("A 32-byte public key is required.", "publicKey");
            if (type < 1 || type > 4) throw new ArgumentOutOfRangeException("type");
            return "meshcore://contact/add?name=" + Uri.EscapeDataString(name ?? "") + "&public_key=" + String.Concat(publicKey.Select(value => value.ToString("x2"))) + "&type=" + type;
        }
    }
}
