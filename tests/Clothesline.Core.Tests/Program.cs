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
Check(ShortcutGesture.Parse(" alt + ctrl + t ").Display == "Ctrl+Alt+T" && ShortcutGesture.Parse("Ctrl+Alt+T").Modifiers == 3, "shortcut normalizes case order and whitespace");
Check(ShortcutGesture.Parse("").Key == 0, "empty shortcut disables registration");
Check(ShortcutGesture.Parse("Ctrl+Shift+F24").Key == 135 && ShortcutGesture.Parse("Alt+0").Key == 48, "function and digit virtual keys map correctly");
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
    Check(cards.All(r => r.X >= area.X && r.Right <= area.Right && r.Bottom <= area.Bottom) && cards.Zip(cards.Skip(1)).All(p => p.First.Right < p.Second.X), $"card size {size} at DPI {dpi} fits with gaps");
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
Console.WriteLine($"{checks} checks passed.");
