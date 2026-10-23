using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clothesline.App;

internal static class ImageFiles
{
    public const long MaxFileBytes = 64L * 1024 * 1024;
    public const long MaxPixels = 40_000_000;
    public static BitmapSource Read(string path, bool thumbnail = false)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try { return ReadStream(stream, thumbnail, Clothesline.Core.MediaFiles.IsExtendedImage(path)); }
        catch (Exception ex) when (Clothesline.Core.MediaFiles.IsExtendedImage(path) && ex is NotSupportedException or FileFormatException or COMException)
        { throw new IOException(T("无法解码 {0} 图片：文件可能损坏，或系统缺少兼容的图像组件。请查看“格式支持”，也可先转换为 PNG/JPEG。原文件保留。", Path.GetExtension(path).TrimStart('.').ToUpperInvariant()), ex); }
    }
    internal static BitmapSource ReadStream(Stream stream, bool thumbnail = false, bool preserveExtensionPixels = false)
    {
        if (stream.Length > MaxFileBytes) throw new IOException(T("图片超过 64 MiB 限制。"));
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > MaxPixels) throw new IOException(T("图片超过 4000 万像素限制。"));
        if (preserveExtensionPixels)
        {
            // Extension codecs can expose an unregistered WPF pixel format. BitmapImage may silently drop alpha.
            // Ask WIC to convert explicitly, then copy while the source stream is still open.
            BitmapSource converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var scale = 420.0 / Math.Max(frame.PixelWidth, frame.PixelHeight);
            if (thumbnail && scale < 1) converted = new TransformedBitmap(converted, new ScaleTransform(scale, scale));
            var stride = checked(converted.PixelWidth * 4); var pixels = new byte[checked(stride * converted.PixelHeight)];
            converted.CopyPixels(pixels, stride, 0);
            var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, frame.DpiX > 0 ? frame.DpiX : 96, frame.DpiY > 0 ? frame.DpiY : 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze(); return result;
        }
        var width = frame.PixelWidth; var height = frame.PixelHeight; stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream;
        if (thumbnail && Math.Max(width, height) > 420) { if (width >= height) image.DecodePixelWidth = 420; else image.DecodePixelHeight = 420; }
        image.EndInit();
        image.Freeze(); return image;
    }
    public static string Hash(string path) => Hash(Read(path));
    public static string Hash(BitmapSource source)
    {
        if ((long)source.PixelWidth * source.PixelHeight > MaxPixels) throw new IOException(T("图片超过像素限制。"));
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes($"{bitmap.PixelWidth}x{bitmap.PixelHeight}:"));
        var row = new byte[checked(bitmap.PixelWidth * 4)];
        for (var y = 0; y < bitmap.PixelHeight; y++) { bitmap.CopyPixels(new Int32Rect(0, y, bitmap.PixelWidth, 1), row, row.Length, 0); hash.AppendData(row); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static byte[] Png(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream(); encoder.Save(stream);
        if (stream.Length > MaxFileBytes) throw new IOException(T("图片超过 64 MiB 限制。"));
        return stream.ToArray();
    }
    public static string Export(string source, string requested)
        => WriteCopy(requested, output => { using var input = File.OpenRead(source); input.CopyTo(output); });
    public static string ExportPng(byte[] png, string requested)
    {
        if (!Path.GetExtension(requested).Equals(".png", StringComparison.OrdinalIgnoreCase)) requested += ".png";
        return WriteCopy(requested, output => output.Write(png));
    }
    private static string WriteCopy(string requested, Action<FileStream> write)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(requested))!;
        Directory.CreateDirectory(directory);
        var name = Path.GetFileNameWithoutExtension(requested); var extension = Path.GetExtension(requested);
        for (var n = 0; n < 10000; n++)
        {
            var target = Path.Combine(directory, name + (n == 0 ? "" : $" ({n})") + extension);
            if (File.Exists(target)) continue;
            var temporary = Path.Combine(directory, ".clothesline-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { write(output); output.Flush(true); }
                File.Move(temporary, target, false); return target;
            }
            catch (IOException) when (File.Exists(target)) { }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        throw new IOException(T("无法生成不重名的导出文件。"));
    }
}
