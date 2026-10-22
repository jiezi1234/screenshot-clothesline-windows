using Clothesline.Core;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
var gate = new RevealGate();
gate.Tick(0, true, false, false, false); gate.Tick(349, true, false, false, false);
Check(!gate.Visible, "short edge crossing does not reveal");
gate.Tick(350, true, false, false, false); Check(gate.Visible, "350ms dwell reveals");
gate.Tick(2600, false, false, false, false); Check(gate.Visible, "show grace permits reaching cards");
gate.Tick(2850, false, false, false, false); gate.Tick(3399, false, false, false, false);
Check(gate.Visible, "leave grace is not prematurely cut off");
gate.Tick(3400, false, false, false, false); Check(!gate.Visible, "leave timeout hides");
gate.Show(4000); gate.Hide(); gate.Tick(8000, true, false, false, false); gate.Tick(9000, true, false, false, false);
Check(!gate.Visible, "manual hide must leave trigger before reentering");
gate.Tick(9010, false, false, false, false); gate.Tick(9020, true, false, false, false); gate.Tick(9370, true, false, false, false);
Check(gate.Visible, "leaving trigger rearms reveal");
gate.Tick(10000, false, false, true, true); Check(gate.Visible, "active drag prevents fullscreen interruption");
gate.Tick(10001, false, false, false, true); Check(!gate.Visible, "fullscreen hides after interaction ends");
var dragGate = new RevealGate();
dragGate.Tick(0, true, false, true, false); dragGate.Tick(1000, true, false, true, false);
Check(!dragGate.Visible, "dragging another application does not reveal shelf");
foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
{
    var area = new PixelRect(-2560, -700, 2560, 1440);
    var cards = DesktopGeometry.Cards(area, scale, 8);
    Check(cards.All(r => r.X >= area.X && r.Right <= area.Right && r.Y >= area.Y && r.Bottom <= area.Bottom), $"negative monitor bounds at {scale * 100}%");
    Check(cards.Zip(cards.Skip(1)).All(pair => pair.First.Right < pair.Second.X), $"cards leave hit-test gaps at {scale * 100}%");
    var trigger = DesktopGeometry.Trigger(area, scale);
    Check(trigger.Contains(new PixelPoint(-1280, -700)) && !trigger.Contains(new PixelPoint(0, 0)), $"physical edge trigger at {scale * 100}%");
}
var monitor = new PixelRect(-1920, 0, 1920, 1080);
Check(DesktopGeometry.CoversMonitor(monitor, monitor, false), "borderless covering monitor is fullscreen");
Check(!DesktopGeometry.CoversMonitor(monitor, monitor, true), "framed maximized window excluded");
Check(!DesktopGeometry.CoversMonitor(new PixelRect(0, 0, 1920, 1080), monitor, false), "fullscreen on another monitor excluded");
Check(TransferPolicy.Decide(false, true, 2) == TransferDisposition.Keep, "cancel retains source despite stale move result");
Check(TransferPolicy.Decide(true, true, 1) == TransferDisposition.Keep, "copy retains source");
Check(TransferPolicy.Decide(true, false, 1) == TransferDisposition.RemoveMissing, "missing source is reported instead of pretending a copy remains usable");
Check(TransferPolicy.Decide(true, true, 2) == TransferDisposition.VerifyMove, "unexpected move result cannot authorize source deletion");
Check(TransferPolicy.Decide(true, true, 0x80000001) == TransferDisposition.Keep, "copy with scroll flag retains source");
string[] ordered = ["newest", "middle", "oldest", "other-page"];
Check(BatchSelection.Select(ordered, "middle", ArchiveScope.Left).SequenceEqual(["newest"]), "left archive preserves anchor and newer-first ordering");
Check(BatchSelection.Select(ordered, "middle", ArchiveScope.Right).SequenceEqual(["oldest", "other-page"]), "right archive includes later pages and excludes anchor");
Check(BatchSelection.Select(ordered, "middle", ArchiveScope.All).SequenceEqual(ordered), "all archive includes anchor and every page");
Check(BatchSelection.Select(ordered, "missing", ArchiveScope.All).Length == 0 && BatchSelection.Select([], "missing", ArchiveScope.Left).Length == 0, "stale anchor and empty shelf archive nothing");
Check(BatchSelection.Select(ordered, "newest", ArchiveScope.Left).Length == 0 && BatchSelection.Select(ordered, "other-page", ArchiveScope.Right).Length == 0, "batch boundaries do not wrap");
Console.WriteLine($"{checks} checks passed.");
