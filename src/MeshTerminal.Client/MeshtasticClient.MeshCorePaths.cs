using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace MeshTerminal.Client
{
    public sealed class MeshCorePathStep
    {
        public string Name { get; internal set; }
        public double? Latitude { get; internal set; }
        public double? Longitude { get; internal set; }
    }
    public sealed class MeshCoreNodePaths
    {
        public IList<MeshCorePathStep> OutgoingSteps { get; internal set; }
        public IList<MeshCorePathStep> IncomingAdvertSteps { get; internal set; }
        public string Outgoing { get; internal set; }
        public string IncomingAdvert { get; internal set; }
        public DateTime? AdvertReceivedUtc { get; internal set; }
    }
    public sealed partial class MeshtasticClient
    {
        internal static string DescribeMeshCorePath(byte encoded, byte[] data, IEnumerable<MeshNode> nodes, string start, string end)
        {
            var count = DecodeMeshCoreHops(encoded);
            if (!count.HasValue) return "Not available (no valid stored path).";
            var size = (encoded >> 6) + 1;
            if (data.Length < count.Value * size) throw new InvalidDataException("Truncated MeshCore path.");
            var parts = new List<string> { start };
            for (var index = 0; index < count.Value; index++)
            {
                var hash = Hex(data, index * size, size);
                var matches = nodes.Where(node => node.MeshCoreContactType == 2 && node.Id != null && node.Id.StartsWith(hash, StringComparison.OrdinalIgnoreCase)).ToArray();
                parts.Add(matches.Length == 1 ? (String.IsNullOrWhiteSpace(matches[0].LongName) ? "Repeater" : matches[0].LongName) + " [" + hash + "]" : "[" + hash + (matches.Length > 1 ? "; ambiguous" : "; unknown") + "]");
            }
            parts.Add(end);
            return String.Join(" -> ", parts) + "\nRepeaters: " + count.Value + (count.Value == 0 ? " (direct)" : "") + " | identifier size: " + size + " byte(s)";
        }
        internal static IList<MeshCorePathStep> DecodeMeshCorePathSteps(byte encoded, byte[] data, IEnumerable<MeshNode> nodes, MeshCorePathStep start, MeshCorePathStep end)
        {
            var count = DecodeMeshCoreHops(encoded);
            if (!count.HasValue) return null;
            var size = (encoded >> 6) + 1;
            if (data.Length < count.Value * size) throw new InvalidDataException("Truncated MeshCore path.");
            var parts = new List<MeshCorePathStep> { start };
            for (var index = 0; index < count.Value; index++)
            {
                var hash = Hex(data, index * size, size);
                var matches = nodes.Where(node => node.MeshCoreContactType == 2 && node.Id != null && node.Id.StartsWith(hash, StringComparison.OrdinalIgnoreCase)).ToArray();
                var match = matches.Length == 1 ? matches[0] : null;
                parts.Add(new MeshCorePathStep { Name = match == null ? "[" + hash + (matches.Length > 1 ? "; ambiguous" : "; unknown") + "]" : (String.IsNullOrWhiteSpace(match.LongName) ? "Repeater" : match.LongName) + " [" + hash + "]", Latitude = match?.Latitude, Longitude = match?.Longitude });
            }
            parts.Add(end);
            return parts;
        }
        public static string FormatMeshCorePathVertical(IList<MeshCorePathStep> steps)
        {
            if (steps == null) return "Not available (no valid stored path).";
            var lines = new List<string>();
            for (var index = 0; index < steps.Count; index++)
            {
                if (index > 0)
                {
                    var previous = steps[index - 1]; var current = steps[index];
                    var distance = GetDistanceMeters(previous.Latitude, previous.Longitude, current.Latitude, current.Longitude);
                    var label = distance.HasValue ? (distance.Value < 1000 ? Math.Round(distance.Value).ToString(System.Globalization.CultureInfo.InvariantCulture) + " m" : (distance.Value / 1000).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km") : null;
                    lines.Add("↓" + (label == null ? "" : "  " + label));
                }
                lines.Add(steps[index].Name);
            }
            return String.Join("\n", lines);
        }
        public async Task<MeshCoreNodePaths> GetMeshCoreNodePathsAsync(uint number, CancellationToken token = default(CancellationToken))
        {
            if (Protocol != MeshProtocol.MeshCore || !IsConnected) throw new InvalidOperationException("Connect to MeshCore first.");
            var key = GetMeshCoreContactKey(number);
            var session = _meshCore;
            var nodes = Nodes.ToArray();
            var target = nodes.FirstOrDefault(node => node.Number == number);
            var name = target == null || String.IsNullOrWhiteSpace(target.LongName) ? Hex(key, 0, 8) : target.LongName;
            var self = String.IsNullOrWhiteSpace(Device.NodeName) ? "This device" : Device.NodeName;
            var result = new MeshCoreNodePaths();
            var selfStep = new MeshCorePathStep { Name = self, Latitude = Device.Latitude, Longitude = Device.Longitude };
            var targetStep = new MeshCorePathStep { Name = name, Latitude = target?.Latitude, Longitude = target?.Longitude };
            try
            {
                var contact = await session.CommandAsync(new byte[] { 30 }.Concat(key).ToArray(), new byte[] { 3 }, token).ConfigureAwait(false);
                RequireMeshCoreLength(contact, 100);
                if (Hex(contact, 1, 32) != Hex(key, 0, 32)) throw new InvalidDataException("Contact reply belongs to another node.");
                result.OutgoingSteps = DecodeMeshCorePathSteps(contact[35], contact.Skip(36).Take(64).ToArray(), nodes, selfStep, targetStep);
                result.Outgoing = DescribeMeshCorePath(contact[35], contact.Skip(36).Take(64).ToArray(), nodes, self, name);
            }
            catch (IOException ex) { result.Outgoing = "Not available: " + ex.Message; }
            try
            {
                var advert = await session.CommandAsync(new byte[] { 42, 0 }.Concat(key).ToArray(), new byte[] { 22 }, token).ConfigureAwait(false);
                RequireMeshCoreLength(advert, 6);
                result.IncomingAdvertSteps = DecodeMeshCorePathSteps(advert[5], advert.Skip(6).ToArray(), nodes, targetStep, selfStep);
                result.IncomingAdvert = DescribeMeshCorePath(advert[5], advert.Skip(6).ToArray(), nodes, name, self);
                var timestamp = MeshCoreSession.ReadUInt32(advert, 1);
                if (timestamp != 0) result.AdvertReceivedUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(timestamp);
            }
            catch (IOException ex) { result.IncomingAdvert = "Not available: " + ex.Message; }
            return result;
        }
    }
}
