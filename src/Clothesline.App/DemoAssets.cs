using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clothesline.App;

internal sealed record DemoItem(string Title, string Subtitle, string Path, BitmapSource Image);

internal static class DemoAssets
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenshotClothesline", "Prototype", "Samples");
    public static List<DemoItem> Create(string? folder = null)
    {
        folder ??= Folder;
        Directory.CreateDirectory(folder);
        var items = new List<DemoItem>();
        var titles = new[] { "山间一刻", "灵感笔记", "海岸微风" };
        var subtitles = new[] { "MOUNTAIN / 01", "STUDIO / 02", "COAST / 03" };
        for (int i = 0; i < 3; i++)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) DrawSample(dc, i);
            var image = new RenderTargetBitmap(960, 600, 96, 96, PixelFormats.Pbgra32);
            image.Render(visual); image.Freeze();
            // Each restoration makes a new batch; never overwrite a demo the user may have edited.
            var path = Path.Combine(folder, $"demo-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}-{i + 1}.png");
            Save(image, path);
            items.Add(new(titles[i], subtitles[i], path, image));
        }
        return items;
    }
    public static void Save(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static void DrawSample(DrawingContext dc, int i)
    {
        if (i == 0)
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(31, 73, 72), Color.FromRgb(135, 171, 141), 90), null, new Rect(0, 0, 960, 600));
            dc.DrawEllipse(Brush("#F8E4A8"), null, new Point(745, 150), 65, 65);
            dc.DrawGeometry(Brush("#719D8E"), null, Geometry.Parse("M0,450 L260,160 L490,435 L715,245 L960,480 L960,600 L0,600 Z"));
            dc.DrawGeometry(Brush("#214E49"), null, Geometry.Parse("M0,520 L350,290 L610,545 L800,430 L960,530 L960,600 L0,600 Z"));
            Text(dc, "A little room to breathe.", 44, "#FFFFFF", 58, 505);
        }
        else if (i == 1)
        {
            dc.DrawRectangle(Brush("#F0E9DB"), null, new Rect(0, 0, 960, 600));
            for (int x = 0; x < 960; x += 40) dc.DrawLine(new Pen(Brush("#DCD5C8"), 1), new Point(x, 0), new Point(x, 600));
            for (int y = 0; y < 600; y += 40) dc.DrawLine(new Pen(Brush("#DCD5C8"), 1), new Point(0, y), new Point(960, y));
            dc.PushTransform(new RotateTransform(-7, 680, 270));
            dc.DrawRoundedRectangle(Brush("#E9AF5E"), null, new Rect(545, 110, 280, 310), 22, 22);
            dc.DrawEllipse(Brush("#F5D6A5"), null, new Point(685, 240), 80, 80); dc.Pop();
            Text(dc, "MAKE\nSPACE.", 90, "#293F39", 70, 115);
            Text(dc, "Keep the thought. Carry on.", 30, "#69746C", 75, 480);
        }
        else
        {
            dc.DrawRectangle(Brush("#C7E4E6"), null, new Rect(0, 0, 960, 600));
            dc.DrawEllipse(Brush("#FFF3D3"), null, new Point(200, 145), 58, 58);
            dc.DrawGeometry(Brush("#559799"), null, Geometry.Parse("M0,340 Q220,250 480,360 Q730,460 960,300 L960,600 L0,600 Z"));
            dc.DrawGeometry(Brush("#2B676C"), null, Geometry.Parse("M0,440 Q300,360 540,465 Q760,535 960,400 L960,600 L0,600 Z"));
            dc.DrawGeometry(Brush("#EED8AD"), null, Geometry.Parse("M0,540 Q450,470 960,580 L960,600 L0,600 Z"));
            Text(dc, "ONE GOOD MOMENT", 40, "#29575B", 380, 95);
        }
    }
    internal static SolidColorBrush Brush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    internal static void Text(DrawingContext dc, string text, double size, string color, double x, double y)
    {
        dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, Brush(color), 1), new Point(x, y));
    }
}
