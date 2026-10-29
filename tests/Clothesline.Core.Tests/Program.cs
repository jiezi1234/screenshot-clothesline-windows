using Clothesline.Core;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
var gate = new RevealGate();
gate.Tick(0, true, false, false, false); gate.Tick(349, true, false, false, false);
Check(!gate.Visible, "short edge crossing does not reveal");
gate.Tick(350, true, false, false, false); Check(gate.Visible, "350ms dwell reveals");
gate.Tick(3500, false, false, false, false); gate.Tick(10349, false, false, false, false); Check(gate.Visible, "10-second show grace permits reaching cards");
gate.Tick(10350, false, false, false, false); gate.Tick(10899, false, false, false, false);
Check(gate.Visible, "leave grace is not prematurely cut off");
gate.Tick(10900, false, false, false, false); Check(!gate.Visible, "leave timeout hides after 10-second protection");
gate.Show(14000); gate.Hide(); gate.Tick(18000, true, false, false, false); gate.Tick(19000, true, false, false, false);
Check(!gate.Visible, "manual hide must leave trigger before reentering");
gate.Tick(19010, false, false, false, false); gate.Tick(19020, true, false, false, false); gate.Tick(19370, true, false, false, false);
Check(gate.Visible, "leaving trigger rearms reveal");
gate.Tick(20000, false, false, true, true); Check(gate.Visible, "active drag prevents fullscreen interruption");
gate.Tick(20001, false, false, false, true); Check(!gate.Visible, "fullscreen hides after interaction ends");
var repeatedShowGate = new RevealGate();
repeatedShowGate.Show(0); repeatedShowGate.Show(9000);
repeatedShowGate.Tick(10550, false, false, false, false); repeatedShowGate.Tick(18999, false, false, false, false);
Check(repeatedShowGate.Visible, "repeated show restarts the full 10-second protection");
repeatedShowGate.Tick(19000, false, false, false, false); repeatedShowGate.Tick(19550, false, false, false, false);
Check(!repeatedShowGate.Visible, "repeated show still auto-hides after protection and leave delay");
foreach (var seconds in RevealGate.ProtectionSecondsOptions)
{
    var configured = new RevealGate(); configured.ConfigureProtection(seconds, 0); configured.Show(100);
    configured.Tick(100 + seconds * 1000 - 1, false, false, false, false);
    Check(configured.Visible, $"selected {seconds}-second protection remains active before its boundary");
    configured.Tick(100 + seconds * 1000, false, false, false, false);
    configured.Tick(100 + seconds * 1000 + 549, false, false, false, false);
    Check(configured.Visible, $"selected {seconds}-second protection retains leave delay");
    configured.Tick(100 + seconds * 1000 + 550, false, false, false, false);
    Check(!configured.Visible, $"selected {seconds}-second protection expires with leave delay");
}
var changedProtection = new RevealGate(); changedProtection.Show(0); changedProtection.ConfigureProtection(5, 9000);
changedProtection.Tick(10550, false, false, false, false); changedProtection.Tick(13999, false, false, false, false);
Check(changedProtection.Visible, "changing protection restarts the visible shelf timer immediately");
changedProtection.ConfigureProtection(5, 13999); changedProtection.Tick(14000, false, false, false, false); changedProtection.Tick(14550, false, false, false, false);
Check(!changedProtection.Visible, "saving unchanged protection does not extend the visible shelf timer");
changedProtection.ConfigureProtection(30, 15000);
Check(!changedProtection.Visible, "changing protection does not open a hidden shelf");
var invalidProtectionRejected = false;
try { changedProtection.ConfigureProtection(7, 16000); } catch (ArgumentOutOfRangeException) { invalidProtectionRejected = true; }
Check(invalidProtectionRejected && changedProtection.ProtectionSeconds == 30, "invalid protection preserves the previous duration");
var dragGate = new RevealGate();
dragGate.Tick(0, true, false, true, false); dragGate.Tick(1000, true, false, true, false);
Check(!dragGate.Visible, "dragging another application does not reveal shelf");
foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
{
    var area = new PixelRect(-2560, -700, 2560, 1440);
    var cards = DesktopGeometry.Cards(area, scale, 3);
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
Check(ShortcutGesture.Parse(" alt + ctrl + t ").Display == "Ctrl+Alt+T" && ShortcutGesture.Parse("Ctrl+Alt+T").Modifiers == 3, "shortcut normalizes case order and whitespace");
Check(ShortcutGesture.Parse("").Key == 0, "empty shortcut disables registration");
Check(ShortcutGesture.Parse("Ctrl+Shift+F24").Key == 135 && ShortcutGesture.Parse("Alt+0").Key == 48, "function and digit virtual keys map correctly");
Check(ShortcutGesture.Parse("ctrl + ↓").Key == 0x28 && ShortcutGesture.Parse("ctrl + ↑").Key == 0x26, "arrow shortcuts map to native up and down virtual keys");
Check(ShortcutGesture.Parse("ctrl + down").Display == "Ctrl+Down" && ShortcutGesture.Parse("ctrl + ↑").Display == "Ctrl+Up", "arrow names and symbols normalize consistently");
var duplicateShortcut = false; try { ShortcutGesture.ParsePair("Ctrl+↑", "ctrl+up"); } catch (ArgumentException) { duplicateShortcut = true; }
Check(duplicateShortcut, "equivalent show and hide gestures are rejected");
Check(ShortcutGesture.ParsePair("", "").Show.Key == 0 && ShortcutGesture.ParsePair("", "Ctrl+Up").Hide.Key == 0x26, "paired shortcuts permit independent disabling");
foreach (var invalid in new[] { "T", "Shift+T", "Ctrl+Ctrl+T", "Win+T", "Ctrl+F12", "Ctrl+F25", "Ctrl+Alt+", "Ctrl++T" })
{
    var rejected = false; try { ShortcutGesture.Parse(invalid); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "unsafe or invalid shortcut rejected: " + invalid);
}
foreach (var dpi in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
foreach (var size in new[] { .8, 1.0, 1.2 })
{
    var area = new PixelRect(-1920, -223, 1920, 1080);
    var cards = DesktopGeometry.Cards(area, dpi, 100, size);
    Check(cards.All(r => r.X >= area.X && r.Right <= area.Right && r.Bottom <= area.Bottom) && cards.Zip(cards.Skip(1)).All(p => p.Second.X - p.First.X >= p.First.Width * .6), $"card size {size} at DPI {dpi} keeps at least 60% exposed");
    Check(cards[0].Width == (int)Math.Round(DesktopGeometry.CardWidth * dpi * size) && cards[0].Height == (int)Math.Round(DesktopGeometry.CardHeight * dpi * size), $"card size {size} scales both dimensions at DPI {dpi}");
}
foreach (var area in new[] { new PixelRect(-20, -10, 120, 100), new PixelRect(0, 0, 1, 1) })
{
    var cards = DesktopGeometry.Cards(area, 2, 100, 1.2);
    Check(cards.All(r => r.X >= area.X && r.Right <= area.Right && r.Y >= area.Y && r.Bottom <= area.Bottom && r.Width > 0 && r.Height > 0), "tiny work area contains complete card");
}
var normalArea = new PixelRect(0, 0, 1920, 1080);
Check(DesktopGeometry.Cards(normalArea, 1.5, 100, .8).Count > DesktopGeometry.Cards(normalArea, 1.5, 100, 1.2).Count, "larger cards lower automatic row capacity");
Check(DesktopGeometry.Cards(normalArea, 1.5, 0, 1.2).Count == 0, "empty shelf has no card slots");
foreach (var dpi in new[] { 1.0, 1.25, 1.5, 1.75, 2.0, 2.5 })
foreach (var size in new[] { .8, 1.0, 1.2 })
{
    var work = new PixelRect(-2560, -400, 2560, 1440);
    var sparse = DesktopGeometry.Cards(work, dpi, 2, size);
    var full = DesktopGeometry.Cards(work, dpi, 100, size);
    Check(!DesktopGeometry.Stacked(sparse) && sparse[0].Right < sparse[1].X, $"sparse cards retain normal gaps at {dpi}/{size}");
    Check(DesktopGeometry.Stacked(full) && full.Count > 2, $"crowded row overlaps before paging at {dpi}/{size}");
    var progressivelyTighter = true; var previousStep = int.MaxValue;
    for (var count = 2; count <= full.Count; count++)
    {
        var row = DesktopGeometry.Cards(work, dpi, count, size); var step = row[1].X - row[0].X;
        progressivelyTighter &= row.Count == count && step <= previousStep && step >= row[0].Width * .6;
        previousStep = step;
    }
    Check(progressivelyTighter, $"density increases gradually without losing items at {dpi}/{size}");
    var pop = DesktopGeometry.EmphasizedCard(full[1], work);
    Check(pop.Width > full[1].Width && pop.Y < full[1].Y && pop.X >= work.X && pop.Right <= work.Right && pop.Bottom <= work.Bottom, $"hover pop remains within negative-coordinate work area at {dpi}/{size}");
    Check(pop.Right < full[2].X + (full[3].X - full[2].X) && pop.X > full[0].X, $"both neighboring exposed strips remain reachable after pop at {dpi}/{size}");
    Check(DesktopGeometry.Cards(work, dpi, 1000, size).SequenceEqual(full), $"overflow keeps fixed page stride at {dpi}/{size}");
}
foreach (var work in new[] { new PixelRect(-10, -10, 120, 100), new PixelRect(0, 0, 1, 1) })
{
    var card = DesktopGeometry.Cards(work, 2, 100)[0]; var pop = DesktopGeometry.EmphasizedCard(card, work);
    Check(pop.Width > 0 && pop.Height > 0 && pop.X >= work.X && pop.Y >= work.Y && pop.Right <= work.Right && pop.Bottom <= work.Bottom, "tiny hover geometry stays in work area");
}
var sound = new CaptureSoundGate();
Check(!sound.ShouldPlay(0, false, false, false), "sound disabled by default does not consume throttle");
Check(sound.ShouldPlay(0, true, false, false), "first eligible capture sounds immediately");
Check(!sound.ShouldPlay(1999, true, false, false) && sound.ShouldPlay(2000, true, false, false), "capture sound limited to one per two seconds");
Check(!sound.ShouldPlay(4000, true, true, false) && !sound.ShouldPlay(4000, true, false, true) && sound.ShouldPlay(4000, true, false, false), "paused and fullscreen captures stay silent without delaying next eligible sound");
UiText.Language = "en";
Check(UiText.T("暂时没有图片") == "No images yet" && UiText.T("已取下 {0} 张图片到本地历史，原文件保留。", 3).Contains("3 images"), "English empty state and formatted status");
Check(UiText.English.All(p => !string.IsNullOrWhiteSpace(p.Value) && !System.Text.RegularExpressions.Regex.IsMatch(p.Value, @"\p{IsCJKUnifiedIdeographs}")), "all English resources have translated text");
Check(UiText.English.All(p => System.Text.CompositeFormat.Parse(p.Key).MinimumArgumentCount == System.Text.CompositeFormat.Parse(p.Value).MinimumArgumentCount), "translation format arguments match Chinese keys");
var textCalls = new System.Text.RegularExpressions.Regex("\\bT\\(\"((?:[^\"\\\\]|\\\\.)*)\"");
var sourceKeys = Directory.EnumerateFiles("src", "*.cs", SearchOption.AllDirectories).Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
    .SelectMany(p => textCalls.Matches(File.ReadAllText(p)).Select(m => System.Text.Json.JsonSerializer.Deserialize<string>("\"" + m.Groups[1].Value + "\"")!)).Distinct().ToArray();
Check(sourceKeys.All(UiText.English.ContainsKey), "all application translation keys exist in English resource");
Check(UiText.Languages.Count == 11 && UiText.Languages.Select(l => l.Code).Distinct().Count() == 11, "all original-baseline languages are selectable without duplicates");
foreach (var locale in UiText.Languages.Where(l => l.Code != "zh-CN"))
{
    var catalog = UiText.Catalog(locale.Code);
    Check(sourceKeys.All(catalog.ContainsKey) && catalog.Values.All(v => !string.IsNullOrWhiteSpace(v)), "complete nonempty catalog: " + locale.Code);
    Check(catalog.All(p => System.Text.CompositeFormat.Parse(p.Key).MinimumArgumentCount == System.Text.CompositeFormat.Parse(p.Value).MinimumArgumentCount), "format arguments retained: " + locale.Code);
    Check(catalog.Where(p => p.Key.Contains('|')).All(p => p.Key.Split('|').Where((_, i) => i % 2 == 1).SequenceEqual(p.Value.Split('|').Where((_, i) => i % 2 == 1))), "file dialog extension filters retained: " + locale.Code);
    UiText.Language = locale.Code;
    Check(UiText.T("暂时没有图片") == catalog["暂时没有图片"] && UiText.T("失败 {0} 个文件。", 3).Contains('3'), "selected language and format rendering: " + locale.Code);
}
UiText.Language = "zh-CN";
Check(UiText.T("暂时没有图片") == "暂时没有图片", "Chinese empty state preserves requested wording");
foreach (var extension in new[] { ".webp", ".WEBP", ".heic", ".HEIF" }) Check(MediaFiles.IsImage("photo" + extension) && MediaFiles.IsExtendedImage("photo" + extension) && !MediaFiles.IsVideo("photo" + extension), "optional image extension classified: " + extension);
Check(!MediaFiles.IsExtendedImage("photo.png") && MediaFiles.IsImage("photo.png"), "standard images retain existing decoder route");
Check(!MediaFiles.IsImage("photo.avif") && !MediaFiles.IsImage("photo.webp.exe"), "unverified and disguised extensions are not admitted");
foreach (var dpi in new[] { 1.0, 1.25, 1.5, 2.0 })
{
    var screen = new PixelRect(-1920, -1080, 1920, 1080); var work = new PixelRect(-1920, -1040, 1920, 1040);
    var activation = ShelfDropGeometry.Activation(screen, work, dpi);
    Check(activation.X >= work.X && activation.Right <= work.Right && activation.Y == screen.Y && activation.Bottom == work.Y + (int)Math.Round(80 * dpi), "drag activation handles top taskbar and negative origin at " + dpi);
    foreach (var cardScale in new[] { .8, 1.0, 1.2 })
    {
        var slots = DesktopGeometry.Cards(work, dpi, 25, cardScale);
        Check(ShelfDropGeometry.InsertionSlot(slots, slots.Count, slots[0].X - 20) == 0 && ShelfDropGeometry.InsertionSlot(slots, slots.Count, slots[^1].Right + 20) == slots.Count, $"insert before first and after last at {dpi}/{cardScale}");
        Check(ShelfDropGeometry.InsertionSlot(slots, 1, slots[^1].Right) == 1 && ShelfDropGeometry.StableSlot(slots, 1, 0, slots[^1].Right, (int)(8 * dpi)) == 1, $"short final page uses only visible cards at {dpi}/{cardScale}");
        Check(ShelfDropGeometry.InsertionSlot(slots, slots.Count, slots[0].X + slots[0].Width / 2) == 1, $"card midpoint selects following insertion slot at {dpi}/{cardScale}");
    }
}
Check(ShelfDropGeometry.InsertionSlot([], 0, 10) == 0, "empty shelf has first insertion slot");
var stableSlots = new[] { new PixelRect(-300, 20, 200, 180), new PixelRect(-60, 20, 200, 180), new PixelRect(180, 20, 200, 180) };
Check(ShelfDropGeometry.StableSlot(stableSlots, 3, -1, 0, 8) == 1, "initial gap uses cursor position");
Check(ShelfDropGeometry.StableSlot(stableSlots, 3, 1, 47, 8) == 1, "small pointer jitter does not push next card");
Check(ShelfDropGeometry.StableSlot(stableSlots, 3, 1, 49, 8) == 2, "crossing hysteresis opens next gap");
Check(ShelfDropGeometry.StableSlot(stableSlots, 3, 2, 33, 8) == 2 && ShelfDropGeometry.StableSlot(stableSlots, 3, 2, 31, 8) == 1, "return crossing has a separate stable boundary");
Check(ShelfDropGeometry.StableSlot(stableSlots, 3, 0, 500, 8) == 3 && ShelfDropGeometry.StableSlot(stableSlots, 3, 3, -500, 8) == 0, "fast drags can cross several slots in one frame");
Check(ShelfDropGeometry.StableSlot(stableSlots, 1, 3, 500, 8) == 1 && ShelfDropGeometry.StableSlot([], 0, 2, 500, 8) == 0, "page shrink and empty shelf clamp a stale gap");
foreach (var dpi in new[] { 1.0, 1.25, 1.5, 2.0, 2.5 })
{
    var work = new PixelRect(-2560, -1440, 2560, 1400);
    var row = DesktopGeometry.Cards(work, dpi, 25);
    foreach (var (width, height) in new[] { (4000, 2200), (1000, 4000), (6000, 100), (1, 1) })
    {
        var result = HoverPreviewGeometry.Place(row[row.Count / 2], work, dpi, width, height, row);
        Check(result is PixelRect p && p.X >= work.X && p.Right <= work.Right && p.Y >= work.Y && p.Bottom <= work.Bottom && row.All(r => !HoverPreviewGeometry.Overlaps(p, r)), $"hover fits negative monitor and avoids whole row at {dpi}, {width}x{height}");
    }
}
var previewWork = new PixelRect(0, 0, 1920, 1080);
var leftAnchor = new PixelRect(20, 20, 212, 190);
var rightAnchor = leftAnchor with { X = 1688 };
Check(HoverPreviewGeometry.Place(leftAnchor, previewWork, 1, 1600, 900, [leftAnchor]) is PixelRect rightPreview && rightPreview.X > leftAnchor.Right, "hover prefers free space beside card");
Check(HoverPreviewGeometry.Place(rightAnchor, previewWork, 1, 1600, 900, [rightAnchor]) is PixelRect leftPreview && leftPreview.Right < rightAnchor.X, "hover near right edge flips left");
var narrowWork = new PixelRect(-400, 0, 400, 500); var wideCard = new PixelRect(-390, 20, 380, 190);
Check(HoverPreviewGeometry.Place(wideCard, narrowWork, 1, 1600, 900, [wideCard]) is PixelRect downPreview && downPreview.Y > wideCard.Bottom && downPreview.Bottom <= narrowWork.Bottom, "hover shrinks below row on narrow work area");
Check(HoverPreviewGeometry.Place(new PixelRect(0, 0, 50, 50), new PixelRect(0, 0, 50, 50), 2, 1600, 900, []) is null, "no room does not cover source or leave screen");
foreach (var dpi in new[] { 1.0, 1.25, 1.5, 2.0, 2.5 })
foreach (var size in new[] { .8, 1.0, 1.2 })
foreach (var width in new[] { 400, 1920, 3840 })
foreach (var counts in new[] { (0, 0), (25, 0), (0, 25), (1, 20), (20, 1), (20, 20), (2, 3) })
{
    var work = new PixelRect(-1920, -100, width, 1040);
    var capacity = ShelfZones.DisplayCapacity(work, dpi, size);
    var layout = ShelfZones.Plan(work, dpi, counts.Item1, counts.Item2, size);
    var all = layout.Media.Concat(layout.Text).ToArray();
    Check(all.Length == Math.Min(capacity, counts.Item1 + counts.Item2)
        && layout.Media.Count <= counts.Item1 && layout.Text.Count <= counts.Item2
        && (counts.Item1 == 0 || layout.Media.Count > 0) && (counts.Item2 == 0 || layout.Text.Count > 0)
        && all.All(r => r.X >= work.X && r.Right <= work.Right && r.Y >= work.Y && r.Bottom <= work.Bottom)
        && all.Zip(all.Skip(1)).All(p => p.Second.X - p.First.X >= Math.Ceiling(p.First.Width * .6)),
        $"shared layout {counts} at {width}px, DPI {dpi}, card {size}");
}
var fluidWork = new PixelRect(0, 0, 1920, 1080);
var mediaHeavy = ShelfZones.Plan(fluidWork, 1, 12, 1); var textHeavy = ShelfZones.Plan(fluidWork, 1, 1, 12);
Check(mediaHeavy.Media[^1].X > 960 && textHeavy.Text[0].X < 960, "both types can extend beyond the screen midpoint as counts change");
Check(ShelfZones.Plan(fluidWork, 1, 100, 0).Media.Count == ShelfZones.DisplayCapacity(fluidWork, 1)
    && ShelfZones.Plan(fluidWork, 1, 0, 100).Text.Count == ShelfZones.DisplayCapacity(fluidWork, 1), "single type can occupy every screen slot");
var sparseAnchored = ShelfZones.Plan(fluidWork, 1, 1, 1);
Check(sparseAnchored.Media[0].X == 16 && sparseAnchored.Text[0].Right == 1904, "sparse media anchors left and sparse text anchors right");
Console.WriteLine($"{checks} checks passed.");
