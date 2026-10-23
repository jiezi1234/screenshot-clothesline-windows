using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Storage;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Clothesline.App;

internal static class VideoDiagnostics
{
    private static async Task<string> Generate(string folder, bool audio, bool portrait = false)
    {
        var file = await (await StorageFolder.GetFolderFromPathAsync(folder)).CreateFileAsync(portrait ? "portrait.mp4" : audio ? "audio.mp4" : "silent.mp4", CreationCollisionOption.FailIfExists);
        var composition = new MediaComposition();
        composition.Clips.Add(MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, 40, 140, 60), TimeSpan.FromSeconds(2)));
        composition.Clips.Add(MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, 30, 70, 190), TimeSpan.FromSeconds(2)));
        if (audio)
        {
            var wav = Path.Combine(folder, "silent-audio.wav");
            using (var writer = new BinaryWriter(File.Create(wav)))
            {
                const int size = 48000 * 2 * 4;
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(size + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(48000); writer.Write(96000); writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(size); writer.Write(new byte[size]);
            }
            composition.BackgroundAudioTracks.Add(await BackgroundAudioTrack.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(wav)));
        }
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p); profile.Video.Width = portrait ? 180u : 320u; profile.Video.Height = portrait ? 320u : 180u;
        profile.Video.Bitrate = 300000;
        if (!audio) profile.Audio = null;
        var reason = await composition.RenderToFileAsync(file, MediaTrimmingPreference.Precise, profile);
        if (reason != TranscodeFailureReason.None) throw new IOException("Fixture generation: " + reason);
        return file.Path;
    }
    public static int Run(Application app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool ok, object? details = null) { checks.Add(new { name, passed = ok, details }); passed &= ok; }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            VideoWorkspace? player = null; CardWindow? card = null; Dashboard? dashboard = null;
            try
            {
                var fixtures = Path.Combine(directory, "run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixtures);
                var audio = await Generate(fixtures, true); var silent = await Generate(fixtures, false);
                var catalog = new VideoCatalog(Path.Combine(fixtures, "probe"));
                var info = await catalog.Get(audio);
                Check("native-H264-AAC-metadata", info.VideoCodec == "H264" && info.AudioCodecs.SequenceEqual(new[] { "AAC" }) && info.Seconds > 3.8 && info.Seconds < 4.2 && info.Width == 320 && info.Height == 180, new { info.VideoCodec, info.AudioCodecs, info.Seconds, info.Width, info.Height });
                var silentInfo = await catalog.Get(silent); Check("native-H264-without-audio", silentInfo.AudioCodecs.Length == 0);
                var portrait = await Generate(fixtures, false, true); var portraitInfo = await catalog.Get(portrait);
                Check("portrait-video-thumbnail-preserves-aspect", portraitInfo.Width == 180 && portraitInfo.Height == 320 && portraitInfo.Image().PixelHeight == 420 && portraitInfo.Image().PixelWidth < 240);
                var thumbnail = new FormatConvertedBitmap(info.Image(), PixelFormats.Bgra32, null, 0);
                var pixel = new byte[4]; thumbnail.CopyPixels(new Int32Rect(thumbnail.PixelWidth / 2, thumbnail.PixelHeight / 2, 1, 1), pixel, 4, 0);
                Check("thumbnail-is-decoded-green-video-frame", thumbnail.PixelWidth <= 420 && thumbnail.PixelHeight <= 420 && pixel[1] > pixel[0] + 30 && pixel[1] > pixel[2] + 30, pixel);
                var reopened = await new VideoCatalog(Path.Combine(fixtures, "probe")).Get(audio);
                Check("metadata-cache-survives-restart", reopened.Hash == info.Hash && reopened.Thumbnail.SequenceEqual(info.Thumbnail));
                var replaced = Path.Combine(fixtures, "replaced.mp4"); File.Copy(audio, replaced); var old = await catalog.Get(replaced);
                File.Copy(silent, replaced, true); File.SetLastWriteTimeUtc(replaced, DateTime.UtcNow.AddSeconds(2)); var changed = await catalog.Get(replaced);
                Check("replaced-video-invalidates-cache", old.Hash != changed.Hash && changed.AudioCodecs.Length == 0);
                var corrupt = Path.Combine(fixtures, "corrupt.mp4"); File.WriteAllText(corrupt, "not a recording");
                try { await catalog.Get(corrupt); Check("corrupt-file-rejected-with-source-preserved", false); }
                catch (IOException) { Check("corrupt-file-rejected-with-source-preserved", File.ReadAllText(corrupt) == "not a recording"); }
                try { await new VideoCatalog(Path.Combine(fixtures, "timeout"), TimeSpan.FromMilliseconds(1)).Get(audio); Check("native-probe-timeout", false); }
                catch (IOException) { Check("native-probe-timeout", File.Exists(audio) && !Directory.GetFiles(Path.Combine(fixtures, "timeout", "VideoCache"), "*.tmp").Any()); }
                using (var cancel = new CancellationTokenSource())
                {
                    cancel.Cancel(); try { await new VideoCatalog(Path.Combine(fixtures, "cancel")).Get(audio, cancel.Token); Check("probe-cancellation", false); }
                    catch (OperationCanceledException) { Check("probe-cancellation", true); }
                }
                var copy = ShellTransfer.CopyData(audio);
                Check("video-clipboard-is-file-only", copy.GetFileDropList().Cast<string>().SequenceEqual(new[] { audio }) && !copy.GetDataPresent(DataFormats.Bitmap) && !copy.GetDataPresent("PNG") && copy.GetDataPresent("ScreenshotClothesline.OwnCopy"));
                Check("video-clipboard-prefers-copy", copy.GetData("Preferred DropEffect") is MemoryStream effectStream && BitConverter.ToInt32(effectStream.ToArray()) == 1);
                var root = Path.Combine(fixtures, "library"); var watched = Path.Combine(fixtures, "watched"); Directory.CreateDirectory(watched);
                File.Copy(audio, Path.Combine(watched, "old.mp4"));
                using var shelf = new ShelfController(); using var runtime = new LibraryRuntime(shelf, root);
                runtime.ApplySettings(new LibrarySettings { VideoFolder = watched, Capacity = 25, ShowNew = false, ReduceMotion = true }); runtime.Start();
                await Task.Delay(300); Check("video-folder-baseline-skips-old", runtime.Store.Entries().Count == 0);
                var incoming = Path.Combine(watched, "new.mp4"); File.Copy(silent, incoming);
                var until = DateTime.UtcNow.AddSeconds(20);
                while (runtime.Store.Entries().Count == 0 && DateTime.UtcNow < until) await Task.Delay(100);
                var entry = runtime.Store.Entries().Single(); runtime.RefreshShelf(false);
                Check("new-recording-watcher-to-shelf", entry.Path == incoming && entry.Ownership == Ownership.External && shelf.Count == 1);
                Check("video-setting-persists", runtime.Store.LoadSettings().VideoFolder == watched);
                var videoItem = new DemoItem(entry.Title, T("录像") + " · " + silentInfo.Duration, incoming, silentInfo.Image(), entry.Id);
                card = new CardWindow(videoItem); card.Show();
                var menu = Find<Border>(card).First(b => b.ContextMenu is not null).ContextMenu!;
                var labels = menu.Items.OfType<MenuItem>().Select(i => i.Header?.ToString()).ToArray();
                Check("video-menu-plays-without-image-editor", labels.Contains(T("播放录像")) && labels.Contains(T("复制录像文件")) && !labels.Contains(T("编辑图片…")));
                card.UpdateLayout(); var render = new RenderTargetBitmap((int)card.Width, (int)card.Height, 96, 96, PixelFormats.Pbgra32); render.Render((Visual)card.Content); DemoAssets.Save(render, Path.Combine(directory, "video-card.png"));
                card.Close(); card = null;
                var exported = ImageFiles.Export(incoming, Path.Combine(fixtures, "export.mp4"));
                var exportedAgain = ImageFiles.Export(incoming, exported);
                Check("export-copies-no-overwrite", exported != exportedAgain && File.ReadAllBytes(incoming).SequenceEqual(File.ReadAllBytes(exported)) && File.Exists(exportedAgain));
                ShellTransfer.Drag(0, videoItem, (nint owner, System.Runtime.InteropServices.ComTypes.IDataObject data, nint source, uint allowed, out uint effect) =>
                { Check("video-drag-copy-only", allowed == 1); effect = 2; return 0x40100; });
                Check("video-drag-retains-source-and-card", File.Exists(incoming) && runtime.Store.Find(entry.Id)?.State == EntryState.Shelf && shelf.Count == 1);
                runtime.Archive(entry.Id); Check("video-archive-keeps-source", runtime.Store.Find(entry.Id)?.State == EntryState.History && File.Exists(incoming));
                runtime.Restore(entry.Id); runtime.RefreshShelf(false); Check("video-restore", runtime.Store.Find(entry.Id)?.State == EntryState.Shelf && shelf.Count == 1);
                var released = false; player = new VideoWorkspace(incoming, path => new Lease(runtime.AcquireLease(path), () => released = true)); player.Show();
                Check("native-player-opens-generated-video", await player.Ready.WaitAsync(TimeSpan.FromSeconds(25)));
                Check("preview-starts-paused", !player.IsPlaying);
                player.TogglePlayback(); await Task.Delay(800); Check("playback-position-advances", player.Position.TotalSeconds > .2, player.Position.TotalSeconds);
                player.Pause(); var position = player.Position; await Task.Delay(300); Check("pause-keeps-position", Math.Abs((player.Position - position).TotalSeconds) < .15);
                player.Seek(2); Check("seek-updates-position", Math.Abs(player.Position.TotalSeconds - 2) < .2);
                runtime.Store.Archive(entry.Id, LibraryRuntime.Now - (long)TimeSpan.FromDays(8).TotalMilliseconds);
                runtime.ApplySettings(runtime.Settings with { RetentionDays = 7 });
                runtime.Cleanup(); Check("open-video-holds-history-lease", runtime.Store.Find(entry.Id) is not null);
                player.Close(); Check("close-stops-timer-and-releases-lease", !player.TimerRunning && released); player = null;
                player = new VideoWorkspace(corrupt, _ => new Lease(new EmptyLease(), () => { })); player.Show();
                Check("corrupt-player-fails-without-blocking-window", !await player.Ready.WaitAsync(TimeSpan.FromSeconds(25)) && !player.TimerRunning); player.Close(); player = null;
                // Force an old timestamp through the public restore/archive lifecycle.
                runtime.Store.Restore(entry.Id, 25, LibraryRuntime.Now); runtime.Store.Archive(entry.Id, LibraryRuntime.Now - (long)TimeSpan.FromDays(8).TotalMilliseconds);
                runtime.Cleanup(); Check("video-expiry-forgets-reference-keeps-file", runtime.Store.Find(entry.Id) is null && File.Exists(incoming));
                dashboard = new Dashboard(shelf, false, () => { }, runtime) { Exiting = true }; dashboard.Show(); dashboard.UpdateLayout();
                Check("recording-folder-control-accessible", Find<TextBox>(dashboard).Any(t => System.Windows.Automation.AutomationProperties.GetName(t) == T("录像目录")));
                Find<TextBox>(dashboard).First(t => System.Windows.Automation.AutomationProperties.GetName(t) == T("录像目录")).Text = @"C:\Clothesline-test\Recordings";
                dashboard.UpdateLayout();
                var screen = new RenderTargetBitmap((int)dashboard.ActualWidth, (int)dashboard.ActualHeight, 96, 96, PixelFormats.Pbgra32); screen.Render(dashboard); DemoAssets.Save(screen, Path.Combine(directory, "video-settings.png"));
                dashboard.Close(); dashboard = null;
            }
            catch (Exception ex) { Check("unhandled", false, ex.ToString()); }
            finally
            {
                player?.Close(); card?.Close(); dashboard?.Close();
                File.WriteAllText(Path.Combine(directory, "video-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
    private sealed class Lease(IDisposable inner, Action released) : IDisposable { public void Dispose() { inner.Dispose(); released(); } }
    private sealed class EmptyLease : IDisposable { public void Dispose() { } }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
