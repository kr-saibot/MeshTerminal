using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Meshtastic.Protobufs;

namespace Meshtastic.Client
{
    public sealed class ImportedContact
    {
        public string Name { get; internal set; }
        public string PublicKey { get; internal set; }
        public byte Type { get; internal set; }
        public SharedContact MeshtasticContact { get; internal set; }
        public byte[] MeshCoreCard { get; internal set; }
    }
    public static class SharedLinkImport
    {
        internal static Uri MeshtasticUri(string link, params string[] paths)
        {
            Uri uri;
            if (!Uri.TryCreate((link ?? "").Trim(), UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http") ||
                !String.Equals(uri.Host, "meshtastic.org", StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
                !paths.Contains(uri.AbsolutePath.TrimEnd('/')) || uri.Fragment.Length < 2)
                throw new FormatException("Invalid Meshtastic share link.");
            return uri;
        }
        internal static byte[] Payload(Uri uri)
        {
            var encoded = Uri.UnescapeDataString(uri.Fragment.Substring(1)).Replace('-', '+').Replace('_', '/');
            if (encoded.Length > 16384) throw new FormatException("Share link is too large.");
            try { return Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')); }
            catch (FormatException) { throw new FormatException("Invalid Base64 share data."); }
        }
        public static IList<Channel> MeshtasticChannels(string link)
        {
            try
            {
                var set = ChannelSet.Parser.ParseFrom(Payload(MeshtasticUri(link, "/e", "/d")));
                if (set.Settings.Count == 0 || set.Settings.Count > 8) throw new FormatException("Link must contain 1-8 channels.");
                foreach (var settings in set.Settings)
                {
                    if (settings.Name.IndexOf('\0') >= 0 || Encoding.UTF8.GetByteCount(settings.Name) > 11 ||
                        !new[] { 0, 1, 16, 32 }.Contains(settings.Psk.Length)) throw new FormatException("Invalid channel name or key length.");
                }
                return set.Settings.Select(s => new Channel { Role = Channel.Types.Role.Secondary, Settings = s.Clone() }).ToList();
            }
            catch (InvalidProtocolBufferException) { throw new FormatException("Invalid Meshtastic channel data."); }
        }
        public static ImportedContact MeshtasticContact(string link)
        {
            try
            {
                var contact = SharedContact.Parser.ParseFrom(Payload(MeshtasticUri(link, "/v")));
                if (contact.NodeNum == 0 || contact.NodeNum == UInt32.MaxValue || contact.User == null ||
                    contact.User.PublicKey.Length != 32 || contact.User.PublicKey.ToByteArray().All(b => b == 0) ||
                    contact.User.LongName.IndexOf('\0') >= 0 || Encoding.UTF8.GetByteCount(contact.User.LongName) > 39 ||
                    contact.User.ShortName.IndexOf('\0') >= 0 || Encoding.UTF8.GetByteCount(contact.User.ShortName) > 4)
                    throw new FormatException("Contact needs a valid node number, names and a 32-byte public key.");
                // A link must not silently mark a key verified or block a contact.
                contact.ManuallyVerified = false; contact.ShouldIgnore = false;
                return new ImportedContact { Name = String.IsNullOrWhiteSpace(contact.User.LongName) ? contact.User.ShortName : contact.User.LongName,
                    PublicKey = Convert.ToBase64String(contact.User.PublicKey.ToByteArray()), MeshtasticContact = contact };
            }
            catch (InvalidProtocolBufferException) { throw new FormatException("Invalid Meshtastic contact data."); }
        }
        public static ImportedContact MeshCoreContact(string link)
        {
            link = (link ?? "").Trim();
            const string prefix = "meshcore://";
            if (!link.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new FormatException("Expected a MeshCore contact link.");
            var card = link.Substring(prefix.Length);
            if (Regex.IsMatch(card, @"\A[0-9a-fA-F]{200,510}\z") && card.Length % 2 == 0)
            {
                var bytes = Enumerable.Range(0, card.Length / 2).Select(i => Convert.ToByte(card.Substring(i * 2, 2), 16)).ToArray();
                if (((bytes[0] >> 2) & 15) != 4 || (bytes[0] >> 6) != 0) throw new FormatException("Contact card must contain a supported advert packet.");
                var index = (bytes[0] & 3) == 0 || (bytes[0] & 3) == 3 ? 5 : 1;
                if (index >= bytes.Length) throw new FormatException("Truncated contact card.");
                var path = bytes[index++]; var length = (path & 63) * ((path >> 6) + 1);
                if ((path >> 6) == 3 || length > 64 || index + length + 100 >= bytes.Length) throw new FormatException("Truncated or invalid contact card.");
                index += length;
                return new ImportedContact { Name = "MeshCore contact card", PublicKey = String.Concat(bytes.Skip(index).Take(32).Select(b => b.ToString("x2"))), MeshCoreCard = bytes };
            }
            Uri uri;
            if (!Uri.TryCreate(link, UriKind.Absolute, out uri) || uri.Host != "contact" || uri.AbsolutePath != "/add" ||
                uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.Fragment.Length != 0) throw new FormatException("Invalid MeshCore contact link.");
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in uri.Query.TrimStart('?').Split('&'))
            {
                var pair = part.Split(new[] { '=' }, 2);
                if (pair.Length != 2) throw new FormatException("Invalid contact parameters.");
                var key = Uri.UnescapeDataString(pair[0]);
                if (fields.ContainsKey(key)) throw new FormatException("Duplicate contact parameter.");
                fields.Add(key, Uri.UnescapeDataString(pair[1].Replace("+", " ")));
            }
            string name, publicKey, typeText; byte type = 1;
            if (!fields.TryGetValue("name", out name) || String.IsNullOrWhiteSpace(name) || name.IndexOf('\0') >= 0 || Encoding.UTF8.GetByteCount(name) > 31 ||
                !fields.TryGetValue("public_key", out publicKey) || !Regex.IsMatch(publicKey, @"\A[0-9a-fA-F]{64}\z") || publicKey.All(c => c == '0'))
                throw new FormatException("Contact needs a name and a 64-character hexadecimal public key.");
            if (fields.TryGetValue("type", out typeText) && (!Byte.TryParse(typeText, out type) || type < 1 || type > 4)) throw new FormatException("Invalid MeshCore contact type.");
            return new ImportedContact { Name = name, PublicKey = publicKey.ToLowerInvariant(), Type = type };
        }
    }
}