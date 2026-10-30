using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using PartyPilot.Desktop;

internal static class RadarMotionSmoke
{
    internal static void Run()
    {
        var now = DateTime.UtcNow;
        var full = new RadarFrame(123, now.AddMilliseconds(-500), 42, "0xABCDEF",
            10, 20, [new RadarMarker(7, "waypoint", "Metadata/MiscellaneousObjects/Waypoint",
                null, null, null, null, 30, 40)])
        {
            StateAreaAddress = "0x123456", GridX = 1, GridY = 2
        };
        byte[] Packet(int pid = 123, uint areaHash = 42, string? area = "0xABCDEF",
            string? stateArea = "0x123456", DateTime? seen = null,
            double x = 11, double y = 21, double? gridX = 2, double? gridY = 3) =>
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                Kind = "radar_motion", ProcessId = pid, SeenUtc = seen ?? now,
                AreaHash = areaHash, RadarAreaAddress = area,
                RadarStateAreaAddress = stateArea, X = x, Y = y,
                RadarGridX = gridX, RadarGridY = gridY
            });
        if (!RadarMotionFrame.TryParse(Packet(), 123, now, out var motion) ||
            motion is null || !motion.TryApply(full, out var moved) || moved is null ||
            moved.PlayerX != 11 || moved.PlayerY != 21 || moved.GridX != 2 ||
            moved.Markers.Count != 1 || moved.SeenUtc != now)
            throw new Exception("Fresh radar motion did not move the full marker frame.");
        var standaloneFull = full with { AreaAddress = null };
        if (!RadarMotionFrame.TryParse(Packet(area: null), 123, now,
                out var standaloneMotion) || standaloneMotion is null ||
            !standaloneMotion.TryApply(standaloneFull, out var standaloneMoved) ||
            standaloneMoved?.PlayerX != 11 ||
            standaloneMotion.TryApply(full, out _) ||
            RadarMotionFrame.TryParse(Packet(area: null, stateArea: null), 123, now, out _))
            throw new Exception("Standalone radar motion lost its state-area identity guard.");
        if (RadarMotionFrame.TryParse(Packet(pid: 124), 123, now, out _) ||
            RadarMotionFrame.TryParse(Packet(seen: now.AddSeconds(-3)), 123, now, out _) ||
            RadarMotionFrame.TryParse(System.Text.Encoding.UTF8.GetBytes(
                System.Text.Encoding.UTF8.GetString(Packet()).Replace("\"RadarGridX\":2", "\"RadarGridX\":\"bad\"")),
                123, now, out _) ||
            RadarMotionFrame.TryParse(Packet(gridY: null), 123, now, out _) ||
            !RadarMotionFrame.TryParse(Packet(areaHash: 43), 123, now, out var wrongHash) ||
            wrongHash!.TryApply(full, out _) ||
            !RadarMotionFrame.TryParse(Packet(area: "0xABCDE0"), 123, now, out var wrongArea) ||
            wrongArea!.TryApply(full, out _) ||
            !RadarMotionFrame.TryParse(Packet(stateArea: "0x123457"), 123, now,
                out var wrongStateArea) || wrongStateArea!.TryApply(full, out _) ||
            motion.TryApply(full with { SeenUtc = now.AddSeconds(-3) }, out _))
            throw new Exception("Radar motion accepted stale or mismatched game identity.");

        if (RadarSession.OverlayLatestMotion(full, motion).PlayerX != motion.PlayerX ||
            RadarSession.OverlayLatestMotion(full, wrongHash).PlayerX != full.PlayerX ||
            RadarSession.OverlayLatestMotion(full with { SeenUtc = now.AddMilliseconds(1) }, motion)
                .PlayerX != full.PlayerX)
            throw new Exception("A late full radar scan lost newer valid motion or reused another area.");

        var mailbox = new RadarMotionMailbox();
        var later = motion with { SeenUtc = now.AddMilliseconds(25), PlayerX = 12 };
        if (!mailbox.Offer(motion) || mailbox.Offer(later) ||
            mailbox.Offer(motion) || mailbox.TakeLatest() != later ||
            mailbox.TakeLatest() is not null || !mailbox.Offer(motion) ||
            mailbox.TakeLatest() != motion)
            throw new Exception("Radar motion dispatch did not retain only the newest pending sample.");

        var window = new RadarOverlayWindow(new LaunchedClient(123, 1, "C:/Game.exe"),
            2, new RadarVisibility(), renderOnly: true);
        window.Show();
        try
        {
            window.Apply(full);
            window.UpdateLayout();
            var label = window.GetLogicalDescendants().OfType<TextBlock>()
                .Single(item => item.Text == "传送点");
            var layer = label.Parent as Canvas ?? throw new Exception("Radar marker has no map layer.");
            window.ApplyMotion(moved, null);
            var shifted = window.GetLogicalDescendants().OfType<TextBlock>()
                .Single(item => item.Text == "传送点");
            if (!ReferenceEquals(label, shifted) ||
                !double.IsFinite(Canvas.GetLeft(layer)) ||
                !double.IsFinite(Canvas.GetTop(layer)) ||
                Math.Abs(Canvas.GetLeft(layer)) + Math.Abs(Canvas.GetTop(layer)) < 0.01)
                throw new Exception($"Motion did not move the existing map layer: " +
                    $"same={ReferenceEquals(label, shifted)}, left={Canvas.GetLeft(layer)}, " +
                    $"top={Canvas.GetTop(layer)}.");
            window.Apply(moved with { Markers = [] });
            if (window.GetLogicalDescendants().OfType<TextBlock>()
                .Any(item => item.Text == "传送点"))
                throw new Exception("A new full scan did not remove an obsolete marker.");
        }
        finally { window.Close(); }
    }
}
