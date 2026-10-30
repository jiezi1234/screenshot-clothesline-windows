using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;

namespace Clothesline.App;

// Render only our generated controls. No windows are shown, no hotkeys registered,
// and no system clipboard or user library is accessed by this diagnostic.
internal static class PaperStyleDiagnostics
{
    internal static int Run(Application app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool result) { passed &= result; checks.Add(new { name, passed = result }); }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            try
            {
                const string sample = "把想法，留在手边。\n\n不必急着整理，\n先留住这一刻的灵感。";
                var longText = string.Join("\n", Enumerable.Range(1, 900).Select(i => $"{i:D4} 完整文字 · preserve every line 🧺"));
                var pictures = new List<BitmapSource>(); var hashes = new HashSet<string>();
                var preview = new TextHoverWindow();
                try
                {
                    foreach (var style in Enum.GetValues<TextPaperStyle>())
                    {
                        var id = Enumerable.Range(0, 100).Select(i => i.ToString("x32")).First(value => TextPaperStyles.ForEntry(value) == style);
                        var image = TextCardImage.Create(longText, style);
                        Check("bounded-frozen-thumbnail-" + style, image.IsFrozen && image.PixelWidth == 400 && image.PixelHeight == 330);
                        var item = new DemoItem("Example text", "Example subtitle", Path.Combine(directory, "sample.txt"), image, id, sample);
                        var card = new CardWindow(item);
                        try
                        {
                            var content = (FrameworkElement)card.Content;
                            var rendered = Render(content, 212, 190, 2); pictures.Add(rendered);
                            DemoAssets.Save(rendered, Path.Combine(directory, "card-" + style + ".png"));
                            var papers = UiAccessibility.Descendants(content).OfType<TextPaperSurface>().ToArray();
                            Check("card-is-paper-without-photo-caption-" + style, papers.Length == 1 && papers[0].PaperStyle == style
                                && !UiAccessibility.Descendants(content).OfType<TextBlock>().Any(t => t.Visibility == Visibility.Visible && t.Text == item.Title));
                            Check("card-retains-full-text-and-stable-style-" + style, item.PaperStyle == style && (item with { Text = "changed", Title = "edited" }).PaperStyle == style);
                            var pixels = new byte[rendered.PixelWidth * rendered.PixelHeight * 4]; rendered.CopyPixels(pixels, rendered.PixelWidth * 4, 0);
                            hashes.Add(Convert.ToHexString(SHA256.HashData(pixels)));
                            var layoutOk = true;
                            foreach (var scale in new[] { .8, 1, 1.2, 1.5, 2.5 })
                            {
                                var variant = Render(content, 212 * scale, 190 * scale, 1);
                                layoutOk &= variant.PixelWidth == (int)Math.Ceiling(212 * scale) && variant.PixelHeight == (int)Math.Ceiling(190 * scale);
                            }
                            Check("paper-renders-at-small-large-and-high-dpi-sizes-" + style, layoutOk);
                        }
                        finally { card.Close(); }
                        preview.SetText(sample, style);
                        var paper = (FrameworkElement)preview.Content;
                        Render(paper, 430, 350, 1); await Task.Delay(20);
                        DemoAssets.Save(Render(paper, 430, 350, 2), Path.Combine(directory, "preview-" + style + ".png"));
                        Check("preview-matches-card-font-and-style-" + style, preview.PaperStyle == style && preview.PreviewText == sample && preview.TextView.IsReadOnly
                            && preview.TextView.FontFamily.Source == TextPaper.Palette(style).Font);
                        preview.SetText(longText, style); Render(paper, 320, 240, 1); await Task.Delay(20);
                        preview.TextView.ScrollToEnd(); await Task.Delay(20); Render(paper, 320, 240, 1);
                        Check("long-preview-keeps-and-scrolls-complete-text-" + style, preview.PreviewText == longText
                            && preview.TextView.GetFirstVisibleLineIndex() > 0 && preview.TextView.GetLastVisibleLineIndex() >= preview.TextView.LineCount - 2);
                        preview.Dismiss(); Check("dismissal-clears-full-text-" + style, preview.PreviewText.Length == 0);
                    }
                    Check("four-styles-have-distinct-rendered-surfaces", hashes.Count == 4);
                    var own = ShellTransfer.TextData(sample);
                    Check("paper-decorations-do-not-enter-copied-text", own.GetText() == sample && own.GetDataPresent("ScreenshotClothesline.OwnCopy"));
                    var gallery = new DrawingVisual();
                    using (var dc = gallery.RenderOpen())
                    {
                        dc.DrawRectangle(DemoAssets.Brush("#EDF0E8"), null, new Rect(0, 0, 928, 234));
                        dc.DrawLine(new Pen(DemoAssets.Brush("#8B9C86"), 1.5), new Point(8, 30), new Point(920, 30));
                        for (var i = 0; i < pictures.Count; i++) dc.DrawImage(pictures[i], new Rect(16 + i * 228, 23, 212, 190));
                    }
                    var row = new RenderTargetBitmap(1856, 468, 192, 192, PixelFormats.Pbgra32); row.Render(gallery);
                    DemoAssets.Save(row, Path.Combine(directory, "four-paper-styles.png"));
                    Check("diagnostics-do-not-show-windows", app.Windows.Cast<Window>().All(window => !window.IsVisible));
                }
                finally { preview.Close(); }
            }
            catch (Exception ex) { passed = false; checks.Add(new { name = "paper-style-diagnostics-completed", passed = false, error = ex.ToString() }); }
            finally
            {
                File.WriteAllText(Path.Combine(directory, "paper-style-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
    private static BitmapSource Render(FrameworkElement content, double width, double height, double scale)
    {
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(content); bitmap.Freeze(); return bitmap;
    }
}
