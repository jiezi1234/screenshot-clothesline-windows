using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clothesline.App;

internal sealed record ImageFormatCapability(string Name, bool CanDecode, string Detail)
{
    internal string Summary => Name + " · " + (CanDecode ? T("本机样本可解码") : T("本机样本无法解码"));
}

internal static class ImageFormatSupport
{
    internal static byte[] Fixture(string name)
    {
        using var stream = typeof(ImageFormatSupport).Assembly.GetManifestResourceStream("Clothesline.Fixtures." + name) ?? throw new IOException("Missing format fixture.");
        using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
    }
    internal static BitmapSource DecodeFixture(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "Clothesline-probe-" + Guid.NewGuid().ToString("N") + Path.GetExtension(name));
        try { File.WriteAllBytes(path, Fixture(name)); return ImageFiles.Read(path); }
        finally { File.Delete(path); }
    }
    internal static ImageFormatCapability Probe(string name, string fixture, Func<string, BitmapSource>? decode = null)
    {
        try
        {
            var bitmap = (decode ?? DecodeFixture)(fixture);
            if (bitmap.PixelWidth != 32 || bitmap.PixelHeight != 24) throw new IOException(T("样本尺寸验证失败。"));
            // Metadata alone is insufficient: force pixels through the exact WPF decoder used for real images.
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0); var green = new byte[4]; var blue = new byte[4];
            converted.CopyPixels(new Int32Rect(4, 12, 1, 1), green, 4, 0); converted.CopyPixels(new Int32Rect(28, 12, 1, 1), blue, 4, 0);
            if (green[1] < green[0] + 40 || green[1] < green[2] + 40 || blue[0] < blue[1] + 50 || blue[0] < blue[2] + 50 || green[3] != 255 || blue[3] != 255)
                throw new IOException(T("样本像素验证失败。"));
            return new(name, true, "");
        }
        catch (Exception ex) { return new(name, false, ex.Message); }
    }
    internal static ImageFormatCapability[] ProbeAll() => [Probe("WebP", "format-probe.webp"), Probe("HEIC / HEIF", "format-probe.heic")];
}
