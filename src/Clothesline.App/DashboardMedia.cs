using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clothesline.Core;
using Clothesline.Storage;
using Microsoft.Web.WebView2.Core;

namespace Clothesline.App;

internal sealed partial class Dashboard
{
    private const string ThumbnailOrigin = "https://thumbnails.cordnote.invalid/";
    private LibraryEntry[] mediaShelf = [], mediaHistory = [];
    private Dictionary<string, LibraryEntry> mediaById = new();
    private readonly CancellationTokenSource mediaClosing = new();

    private void SetDashboardMedia(LibraryEntry[] shelfEntries, LibraryEntry[] historyEntries)
    {
        if (mediaShelf.SequenceEqual(shelfEntries) && mediaHistory.SequenceEqual(historyEntries)) return;
        mediaShelf = shelfEntries;
        mediaHistory = historyEntries;
        mediaById = historyEntries.ToDictionary(entry => entry.Id);
        // The shelf is read last. A collection update may move an entry between the two reads.
        foreach (var entry in shelfEntries) mediaById[entry.Id] = entry;
        SendDashboardMedia();
    }

    private void SendDashboardMedia()
    {
        if (!webConnected) return;
        object Slide(LibraryEntry entry)
        {
            var kind = MediaFiles.IsText(entry.Path) ? "text" : MediaFiles.IsVideo(entry.Path) ? "video" : "image";
            return new {
                id = entry.Id, title = entry.Title, path = entry.Path, kind, missing = entry.Missing,
                textVersion = kind == "text" ? entry.Hash : null,
                detail = entry.State == EntryState.History ? new HistoryRow(entry).Label.Trim() : "",
                src = kind == "text" || entry.Missing ? null : ThumbnailOrigin + Uri.EscapeDataString(entry.Id) + "?v=" + Uri.EscapeDataString(entry.Hash)
            };
        }
        PostSettings(new { type = "library", shelf = mediaShelf.Select(Slide), history = mediaHistory.Where(entry => mediaById[entry.Id].State == EntryState.History).Select(Slide) });
    }

    private async Task SendTextPreview(JsonElement msg)
    {
        var id = msg.GetProperty("id").GetString();
        var version = msg.GetProperty("version").GetString();
        if (library is null || id is null || !mediaById.TryGetValue(id, out var entry) ||
            entry.Missing || entry.Hash != version || !MediaFiles.IsText(entry.Path)) return;
        string? text = null;
        using var lease = library.AcquireLease(entry.Path);
        try
        {
            text = await Task.Run(() => TextContent.Read(entry.Path), mediaClosing.Token);
            if (text.Length > 512)
            {
                var end = char.IsHighSurrogate(text[511]) ? 511 : 512;
                text = text[..end] + "…";
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        PostSettings(new { type = "text-preview", id, version, text });
    }

    private void RegisterThumbnailRequests(CoreWebView2 core)
    {
        core.AddWebResourceRequestedFilter(ThumbnailOrigin + "*", CoreWebView2WebResourceContext.Image);
        core.WebResourceRequested += async (_, e) =>
        {
            using var deferral = e.GetDeferral();
            try
            {
                var id = Uri.UnescapeDataString(new Uri(e.Request.Uri).AbsolutePath.TrimStart('/'));
                byte[]? bytes = null;
                if (library is not null && mediaById.TryGetValue(id, out var entry) && !entry.Missing && !MediaFiles.IsText(entry.Path))
                {
                    // Only decoded thumbnails for current library IDs cross the WebView boundary.
                    using var lease = library.AcquireLease(entry.Path);
                    var token = mediaClosing.Token;
                    var image = MediaFiles.IsVideo(entry.Path)
                        ? (await library.Videos.Get(entry.Path, token)).Image()
                        : await ImageDecoder.Read(entry.Path, true, token);
                    bytes = await Task.Run(() => ImageFiles.Png(image), token);
                }
                if (!webClosed)
                    e.Response = bytes is null ? core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "")
                        : core.Environment.CreateWebResourceResponse(new MemoryStream(bytes, false), 200, "OK", "Content-Type: image/png\r\nCache-Control: no-store");
            }
            catch (Exception) when (!webClosed)
            {
                e.Response = core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
            }
            catch (Exception) when (webClosed) { }
        };
    }

    private void RunMediaAction(JsonElement msg)
    {
        if (library is null) return;
        var scope = msg.GetProperty("scope").GetString();
        var id = msg.GetProperty("id").GetString();
        if (id is null || scope is not ("shelf" or "history")) return;
        var expected = scope == "shelf" ? EntryState.Shelf : EntryState.History;
        if (library.Store.Find(id) is not { } entry || entry.State != expected) return;
        switch (msg.GetProperty("action").GetString())
        {
            case "preview": Preview(entry); break;
            case "edit" when MediaFiles.IsImage(entry.Path): library.OpenWorkspace(id, true); break;
            case "export": library.Export(entry.Path); break;
            case "reveal": ShellTransfer.Reveal(entry.Path); break;
            case "copy" when expected == EntryState.Shelf: _ = CopyEntry(entry); break;
            case "archive" when expected == EntryState.Shelf: library.Archive(id); break;
            case "archiveAll" when expected == EntryState.Shelf: library.ArchiveBatch(); break;
            case "restore" when expected == EntryState.History: library.Restore(id); break;
            case "forget" when expected == EntryState.History: library.Forget(id); break;
            case "recycle" when expected == EntryState.History: library.Recycle(id); break;
        }
    }
}
