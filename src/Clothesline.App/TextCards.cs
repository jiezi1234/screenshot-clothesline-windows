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
