using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

internal static class TextCardImage
{
    internal static BitmapSource Create(string text)
    {
        // Fixed thumbnail work regardless of document length; the preview/editor retain all text.
        var length = Math.Min(1200, text.Length);
        if (length < text.Length && char.IsHighSurrogate(text[length - 1])) length--;
        var excerpt = text[..length];
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(DemoAssets.Brush("#F5F1E7"), null, new Rect(0, 0, 400, 270));
            var formatted = new FormattedText(excerpt, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI, Microsoft YaHei UI"), 25, DemoAssets.Brush("#294535"), 1)
            { MaxTextWidth = 364, MaxTextHeight = 232, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(formatted, new Point(18, 16));
        }
        var image = new RenderTargetBitmap(400, 270, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
    }
}

internal sealed partial class LibraryRuntime
{
    private async Task LoadText(LibraryEntry entry, string signature)
    {
        try
        {
            var text = await Task.Run(() => TextContent.Read(entry.Path), closing.Token);
            if (!disposed && Store.Find(entry.Id)?.State == EntryState.Shelf && CollectionEngine.Signature(entry.Path) == signature)
            {
                Store.UpdateTextMetadata(entry.Id, text);
                cache[entry.Id] = (signature, new DemoItem(TextContent.Title(text), T("文字 · {0} 个字符", text.Length), entry.Path, TextCardImage.Create(text), entry.Id, text));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!disposed && Store.Find(entry.Id) is not null) { Store.SetError(entry.Id, ex.Message); Store.Archive(entry.Id, Now); Report(ex.Message); }
        }
        finally { imageLoads.Remove(entry.Id); if (!disposed) ScheduleRefresh(false); }
    }
    private void OpenTextWorkspace(LibraryEntry entry)
    {
        var existing = workspaces.OfType<TextWorkspace>().FirstOrDefault(w => w.EntryId == entry.Id);
        if (existing is not null) { existing.Show(); existing.Activate(); return; }
        var window = new TextWorkspace(entry.Id, entry.Path, async (text, hash) =>
        {
            if (fileOperation > 0) throw new IOException(T("另一项文件操作进行中，请稍后再保存。"));
            fileOperation++;
            try { var saved = await Collector.SaveText(entry.Id, text, hash, Settings.MaxManagedBytes, closing.Token); cache.Remove(entry.Id); ScheduleRefresh(false); return saved; }
            finally { fileOperation--; }
        }, AcquireLease(entry.Path));
        workspaces.Add(window); window.Closed += (_, _) => workspaces.Remove(window); window.Show(); window.Activate();
    }
}
