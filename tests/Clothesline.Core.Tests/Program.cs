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
Check(TransferPolicy.Decide(false, true, true, 2) == TransferDisposition.Keep, "cancel never authorizes deletion, even stale move/recycle formats");
Check(TransferPolicy.Decide(true, true, false, 1) == TransferDisposition.Keep, "copy retains source");
Check(TransferPolicy.Decide(true, false, false, 2) == TransferDisposition.RemoveMissing, "completed shell move removes stale card");
Check(TransferPolicy.Decide(true, true, false, 2) == TransferDisposition.VerifyMove, "move effect alone cannot delete source");
Check(TransferPolicy.Decide(true, true, true, 1) == TransferDisposition.RecycleRequested, "recycle target recognized even when effect reports copy");
Console.WriteLine($"{checks} checks passed.");
