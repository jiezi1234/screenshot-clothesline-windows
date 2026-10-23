using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Clothesline.Storage;

namespace Clothesline.App;

internal sealed class ClipboardCollector : IDisposable
{
    private readonly HwndSource source;
    private readonly DispatcherTimer retry;
    private readonly Func<byte[], string, Task> accept;
    private uint sequence;
    private int attempts;
    private bool enabled;
    public event Action<string>? Status;
    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    public ClipboardCollector(Func<byte[], string, Task> accept)
    {
        this.accept = accept;
        source = new HwndSource(new HwndSourceParameters("Clothesline.Clipboard") { ParentWindow = -3 });
        retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        retry.Tick += async (_, _) => await Read();
        source.AddHook((nint h, int message, nint w, nint l, ref bool handled) =>
        {
            if (message == 0x31D && enabled) { sequence = GetClipboardSequenceNumber(); attempts = 0; retry.Stop(); retry.Start(); }
            return 0;
        });
    }
    public void Enable(bool value)
    {
        if (enabled == value) return;
        retry.Stop();
        if (value && !AddClipboardFormatListener(source.Handle)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (!value) RemoveClipboardFormatListener(source.Handle);
        enabled = value; sequence = GetClipboardSequenceNumber();
    }
    private async Task Read()
    {
        retry.Stop();
        if (!enabled || sequence != GetClipboardSequenceNumber()) return;
        var current = sequence;
        try
        {
            var data = Clipboard.GetDataObject(); if (data is null) return;
            bool? Flag(string name)
            {
                if (!data.GetDataPresent(name, false)) return null;
                var value = data.GetData(name, false);
                if (value is MemoryStream stream) { var bytes = stream.ToArray(); return bytes.Length >= 4 ? BitConverter.ToUInt32(bytes) != 0 : false; }
                return value switch { int i => i != 0, uint i => i != 0, byte[] bytes when bytes.Length >= 4 => BitConverter.ToUInt32(bytes) != 0, _ => false };
            }
            if (!ClipboardPolicy.Accept(data.GetFormats(false), Flag)) return;
            BitmapSource? image = null;
            if (data.GetDataPresent("PNG", false) && data.GetData("PNG", false) is MemoryStream png)
            {
                if (png.Length > ImageFiles.MaxFileBytes) return;
                using var copy = new MemoryStream(png.ToArray());
                image = BitmapDecoder.Create(copy, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            }
            image ??= Clipboard.GetImage();
            if (image is null || current != GetClipboardSequenceNumber()) return;
            image.Freeze();
            var result = await Task.Run(() => (Bytes: ImageFiles.Png(image), Hash: ImageFiles.Hash(image)));
            if (enabled && current == GetClipboardSequenceNumber()) await accept(result.Bytes, result.Hash);
        }
        catch (COMException)
        {
            if (enabled && current == GetClipboardSequenceNumber() && ++attempts < 6) retry.Start();
            else Status?.Invoke(T("剪贴板忙，已跳过本次图片。"));
        }
        catch (Exception ex) { Status?.Invoke(T("剪贴板图片未收集：") + ex.Message); }
    }
    public void Dispose() { Enable(false); retry.Stop(); source.Dispose(); }
}
