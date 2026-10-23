using MessageBox = MeshTerminal.SafeMessageBox;
using Dialog = MeshTerminal.SafeDialog;
using System;
using System.Linq;
using System.Threading.Tasks;
using MeshTerminal.Client;
using Terminal.Gui;
namespace MeshTerminal
{
    internal static partial class Program
    {
        private static string PadPathText(string text) { return "\n" + String.Join("\n", (text ?? "").Split('\n').Select(line => " " + line)); }
        private static void ShowMeshCoreNodePaths(StoredMeshNode node)
        {
            var dialog = new Dialog("MeshCore paths: " + (node.LongName ?? node.NodeId), 110, 30);
            var outgoingFrame = new FrameView("Route TO node") { X = 1, Y = 1, Width = Dim.Percent(50) - 2, Height = Dim.Fill(4) };
            var incomingFrame = new FrameView("Advert path FROM node") { X = Pos.Right(outgoingFrame) + 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(4) };
            var outgoing = new TextView { Width = Dim.Fill(), Height = Dim.Fill(), ReadOnly = true, WordWrap = false, Text = PadPathText("Reading paths...") };
            var incoming = new TextView { Width = Dim.Fill(), Height = Dim.Fill(), ReadOnly = true, WordWrap = false, Text = PadPathText("Reading paths...") };
            outgoingFrame.Add(outgoing); incomingFrame.Add(incoming);
            var status = new Label("Reading paths from the device...") { X = 1, Y = Pos.AnchorEnd(4), Width = Dim.Fill(2) };
            var note = new Label("Distances are straight-line distances. Unknown positions leave only an arrow.") { X = 1, Y = Pos.AnchorEnd(3), Width = Dim.Fill(2) };
            MeshCoreNodePaths loadedPaths = null; var showMapRequested = false;
            var map = new Button("Show on map") { Enabled = false };
            var refresh = new Button("Refresh"); var closed = false; var busy = false;
            Action read = async delegate
            {
                if (closed || busy) return;
                busy = true; refresh.Enabled = false; map.Enabled = false; status.Text = "Reading stored paths...";
                MeshCoreNodePaths readPaths = null;
                string to, from, information;
                try
                {
                    var paths = await Task.Run(() => _mesh.GetMeshCoreNodePathsAsync(node.NodeNumber)).ConfigureAwait(false);
                    readPaths = paths;
                    to = paths.OutgoingSteps == null ? paths.Outgoing : MeshtasticClient.FormatMeshCorePathVertical(paths.OutgoingSteps);
                    from = paths.IncomingAdvertSteps == null ? paths.IncomingAdvert : MeshtasticClient.FormatMeshCorePathVertical(paths.IncomingAdvertSteps);
                    information = paths.AdvertReceivedUtc.HasValue ? "Advert received (device clock): " + paths.AdvertReceivedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "Stored paths loaded; no fresh route test.";
                }
                catch (Exception ex) { to = from = ex.Message; information = "Could not read paths."; }
                Ui(() => { if (closed) return; outgoing.Text = PadPathText(to); incoming.Text = PadPathText(from); loadedPaths = readPaths; map.Enabled = loadedPaths != null; status.Text = information; busy = false; refresh.Enabled = true; });
            };
            refresh.Clicked += read;
            var copy = new Button("Copy"); copy.Clicked += () => CopyTextWithFallback("Route TO node\n" + outgoing.Text.ToString() + "\n\nAdvert path FROM node\n" + incoming.Text.ToString() + "\n" + status.Text.ToString(), "Paths copied.");
            var close = new Button("Close", true); close.Clicked += () => Application.RequestStop();
            map.Clicked += () => { if (loadedPaths == null) return; var points = MeshCorePathMap.Build(loadedPaths); if (!points.Any(point => point.Shape == "Point")) { MessageBox.Query("Paths", "No known positions are available in these paths.", "OK"); return; } TemporaryMeshCorePaths.Clear(); TemporaryMeshCorePaths.AddRange(points); _temporaryMeshCorePathsVisible = true; showMapRequested = true; Application.RequestStop(); };
            dialog.Add(outgoingFrame, incomingFrame, status, note); dialog.AddButton(refresh); dialog.AddButton(copy); dialog.AddButton(map); dialog.AddButton(close);
            // Schedule on the modal main loop: Ready may have already fired
            // for the enclosing application when this nested dialog opens.
            Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(50), loop => { if (!closed) read(); return false; });
            try { Application.Run(dialog, HandleTerminalGuiException); } finally { closed = true; }
            if (showMapRequested)
            {
                ShowMapPage();
                var points = TemporaryMeshCorePaths.Where(point => point.Shape == "Point").ToList();
                var minLat = points.Min(point => point.Latitude); var maxLat = points.Max(point => point.Latitude);
                var minLon = points.Min(point => point.Longitude); var maxLon = points.Max(point => point.Longitude);
                var latitude = (minLat + maxLat) / 2; var longitude = (minLon + maxLon) / 2;
                var north = MeshtasticClient.GetDistanceMeters(minLat, longitude, maxLat, longitude).GetValueOrDefault();
                var east = MeshtasticClient.GetDistanceMeters(latitude, minLon, latitude, maxLon).GetValueOrDefault();
                var scale = Math.Max(10, Math.Max(north / Math.Max(1, _nodeMap.Frame.Height - 6), east / Math.Max(1, (_nodeMap.Frame.Width - 12) * .5)) * 1.3);
                _nodeMap.RestoreView(latitude, longitude, scale);
                SaveMapViewState();
                ShowBriefMapPopup("7: MeshCore paths | FROM yellow | TO white");
            }
        }
    }
}
