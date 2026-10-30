using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

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
                cache[entry.Id] = (signature, new DemoItem(TextContent.Title(text), T("文字 · {0} 个字符", text.Length), entry.Path, TextCardImage.Create(text, TextPaperStyles.ForEntry(entry.Id)), entry.Id, text));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!disposed && Store.Find(entry.Id) is not null) { Store.SetError(entry.Id, ex.Message); Store.Archive(entry.Id, Now); Report(ex.Message); }
        }
        finally { imageLoads.Remove(entry.Id); if (!disposed) ScheduleRefresh(false); }
    }
    private void OpenTextPreview(LibraryEntry entry)
    {
        var existing = workspaces.OfType<TextPreviewWindow>().FirstOrDefault(w => w.EntryId == entry.Id);
        if (existing is not null) { _ = existing.RefreshText(); existing.Show(); existing.Activate(); return; }
        var window = new TextPreviewWindow(entry.Id, entry.Path, AcquireLease(entry.Path));
        workspaces.Add(window); window.Closed += (_, _) => workspaces.Remove(window); window.Show(); window.Activate();
    }
    private TextEditSession? CreateTextEditSession(DemoItem item)
    {
        var entry = item.EntryId is null ? null : Store.Find(item.EntryId);
        if (entry is null || !MediaFiles.IsText(entry.Path) || item.Text is null) throw new IOException(T("原文件已失效，无法打开。请在历史中检查路径。"));
        return new TextEditSession(entry.Id, item.Text, (text, hash) => SaveTextEntry(entry.Id, text, hash), AcquireLease(entry.Path));
    }
    private async Task<string> SaveTextEntry(string id, string text, string hash)
    {
        if (fileOperation > 0) throw new IOException(T("另一项文件操作进行中，请稍后再保存。"));
        fileOperation++;
        try { var saved = await Collector.SaveText(id, text, hash, Settings.MaxManagedBytes, closing.Token); cache.Remove(id); ScheduleRefresh(false); return saved; }
        finally { fileOperation--; }
    }
}
