using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed record PaperPalette(string Paper, string Edge, string Ink, string Quiet, string Font, string? Rule);

internal static class TextPaper
{
    internal static PaperPalette Palette(TextPaperStyle style) => style switch
    {
        TextPaperStyle.Sticky => new("#F7E7A7", "#DDCF97", "#514832", "#8B7B51", "KaiTi, STKaiti, Microsoft YaHei UI", null),
        TextPaperStyle.Journal => new("#F6F1E5", "#D9D2C1", "#444C40", "#7B8572", "SimSun, Songti SC, Microsoft YaHei UI", "#D1D9D1"),
        TextPaperStyle.Notebook => new("#FCFAF1", "#CCD3C5", "#384B43", "#7F9183", "Segoe UI, Microsoft YaHei UI", "#CEDCE0"),
        _ => new("#FFFDF6", "#D9DDD3", "#30483D", "#849389", "Segoe UI, Microsoft YaHei UI", null)
    };
    internal static Thickness Padding(TextPaperStyle style, bool compact) => style switch
    {
        TextPaperStyle.Sticky => new(compact ? 17 : 26, compact ? 27 : 34, compact ? 16 : 27, 20),
        TextPaperStyle.Journal => new(compact ? 31 : 42, compact ? 24 : 24, compact ? 15 : 25, 20),
        TextPaperStyle.Notebook => new(compact ? 35 : 46, compact ? 23 : 24, compact ? 15 : 26, 19),
        _ => new(compact ? 18 : 27, compact ? 38 : 25, compact ? 17 : 27, 20)
    };
    private static readonly DrawingBrush Grain = MakeGrain();
    private static DrawingBrush MakeGrain()
    {
        var random = new Random(20417); var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
            for (var i = 0; i < 270; i++)
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)random.Next(6, 19), 111, 100, 68)), null,
                    new Point(random.NextDouble() * 64, random.NextDouble() * 64), .25 + random.NextDouble() * .35, .22);
        drawing.Freeze();
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 64, 64), ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 64, 64), Stretch = Stretch.None };
        brush.Freeze(); return brush;
    }
    private static Brush Brush(string color) => DemoAssets.Brush(color);
    private static StreamGeometry Polygon(Point[] points)
    {
        var shape = new StreamGeometry();
        using (var path = shape.Open()) { path.BeginFigure(points[0], true, true); for (var i = 1; i < points.Length; i++) path.LineTo(points[i], true, false); }
        shape.Freeze(); return shape;
    }
    private static Geometry Shape(Rect paper, TextPaperStyle style)
    {
        if (style != TextPaperStyle.Journal) return new RectangleGeometry(paper, style == TextPaperStyle.Index ? 4 : 2, style == TextPaperStyle.Index ? 4 : 2);
        var points = new System.Collections.Generic.List<Point> { paper.TopLeft, paper.TopRight, new(paper.Right, paper.Bottom - 2) };
        for (var x = paper.Right - 6; x > paper.Left; x -= 7) points.Add(new Point(x, paper.Bottom - ((int)((paper.Right - x) / 7) % 3 == 0 ? 4 : 1)));
        points.Add(paper.BottomLeft); return Polygon(points.ToArray());
    }
    internal static void Draw(DrawingContext dc, Size size, TextPaperStyle style, bool compact, string? text = null, double pixelsPerDip = 1, Action<DrawingContext, Rect>? rules = null)
    {
        if (size.Width < 30 || size.Height < 30) return;
        var palette = Palette(style);
        var paper = new Rect(style == TextPaperStyle.Notebook ? 8 : 3, 3, size.Width - (style == TextPaperStyle.Notebook ? 14 : 8), size.Height - 10);
        var geometry = Shape(paper, style);
        dc.PushTransform(new TranslateTransform(1, 3)); dc.DrawGeometry(Brush("#16000000"), null, geometry); dc.Pop();
        dc.PushTransform(new TranslateTransform(1, 1.5)); dc.DrawGeometry(Brush(palette.Edge), null, geometry); dc.Pop();
        if (style == TextPaperStyle.Notebook) dc.DrawRoundedRectangle(Brush("#536859"), null, new Rect(paper.X - 2, paper.Y + 1, paper.Width + 5, paper.Height + 4), 4, 4);
        dc.DrawGeometry(Brush(palette.Paper), style == TextPaperStyle.Index ? new Pen(Brush("#E2E4D9"), .8) : null, geometry);
        dc.PushClip(geometry);
        if (style == TextPaperStyle.Sticky)
        {
            var wash = new LinearGradientBrush(Color.FromRgb(237, 219, 145), Color.FromRgb(247, 231, 167), 90);
            dc.DrawRectangle(wash, null, new Rect(paper.X, paper.Y, paper.Width, compact ? 24 : 30));
        }
        dc.DrawRectangle(Grain, null, paper);
        if (palette.Rule is not null)
        {
            var left = style == TextPaperStyle.Notebook ? 29d : 24d;
            if (rules is null)
            {
                var padding = Padding(style, compact); var step = compact ? 24d : 26d;
                for (var y = padding.Top + step - 2; y < paper.Bottom; y += step)
                    dc.DrawLine(new Pen(Brush(palette.Rule), .7), new Point(paper.Left + 1, y), new Point(paper.Right - 1, y));
            }
            else rules(dc, paper);
            dc.DrawLine(new Pen(Brush(style == TextPaperStyle.Notebook ? "#D9A8A1" : "#CDACA2"), .75), new Point(left, paper.Top), new Point(left, paper.Bottom));
        }
        if (style == TextPaperStyle.Index)
        {
            var y = compact ? 29 : 52;
            dc.DrawLine(new Pen(Brush(palette.Edge), .8), new Point(18, y), new Point(paper.Right - 16, y));
            if (compact)
                dc.DrawText(new FormattedText(T("文字内容"), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI, Microsoft YaHei UI"), 10, Brush(palette.Quiet), pixelsPerDip), new Point(18, 11));
        }
        if (text is not null)
        {
            var padding = Padding(style, compact); var length = Math.Min(1200, text.Length);
            if (length < text.Length && char.IsHighSurrogate(text[length - 1])) length--;
            var body = new Rect(padding.Left, padding.Top, Math.Max(1, size.Width - padding.Left - padding.Right), Math.Max(1, size.Height - padding.Top - padding.Bottom));
            var formatted = new FormattedText(text[..length], CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(palette.Font), style == TextPaperStyle.Sticky ? 16 : 14, Brush(palette.Ink), pixelsPerDip)
            { MaxTextWidth = body.Width, MaxTextHeight = body.Height, LineHeight = 24, Trimming = TextTrimming.CharacterEllipsis };
            dc.PushClip(new RectangleGeometry(body)); dc.DrawText(formatted, body.TopLeft); dc.Pop();
        }
        dc.Pop();
        if (style == TextPaperStyle.Sticky)
        {
            var right = paper.Right; var bottom = paper.Bottom; var fold = compact ? 20d : 27d;
            var shape = Polygon([new(right - fold, bottom), new(right, bottom - fold), new(right, bottom)]);
            var gradient = new LinearGradientBrush(); gradient.StartPoint = new Point(0, 0); gradient.EndPoint = new Point(1, 1);
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(221, 207, 151), .35)); gradient.GradientStops.Add(new GradientStop(Color.FromRgb(255, 241, 188), .65)); gradient.GradientStops.Add(new GradientStop(Color.FromRgb(227, 212, 154), 1));
            dc.DrawGeometry(gradient, new Pen(Brush("#25A88D44"), .5), shape);
        }
        if (style == TextPaperStyle.Journal)
        {
            dc.PushTransform(new RotateTransform(-7, 28, 8));
            dc.DrawRectangle(Brush("#B7A6B88E"), null, new Rect(14, 1, compact ? 43 : 56, 14));
            for (var x = 19; x < (compact ? 56 : 69); x += 7) dc.DrawLine(new Pen(Brush("#23758863"), .5), new Point(x, 1), new Point(x, 15));
            dc.Pop();
        }
        if (style == TextPaperStyle.Notebook)
            for (var y = 18d; y < paper.Bottom - 10; y += compact ? 26 : 33)
            {
                dc.DrawEllipse(Brush("#788D7B"), null, new Point(18, y), 3.4, 5);
                var metal = new LinearGradientBrush(Color.FromRgb(230, 236, 229), Color.FromRgb(170, 183, 173), 90);
                dc.DrawRoundedRectangle(metal, new Pen(Brush("#84928B"), 1.3), new Rect(1, y - 3, 19, 6), 3, 3);
            }
    }
}

internal sealed class TextPaperSurface : Grid
{
    private TextPaperStyle paperStyle;
    internal bool Compact { get; }
    internal string? Excerpt { get; }
    internal TextPaperStyle PaperStyle { get => paperStyle; set { paperStyle = value; InvalidateVisual(); } }
    private TextBox? textView;
    internal TextPaperSurface(TextPaperStyle style, bool compact = false, string? excerpt = null)
    { paperStyle = style; Compact = compact; Excerpt = excerpt; Background = Brushes.Transparent; }
    internal void TrackTextLines(TextBox view)
    {
        textView = view;
        view.TextChanged += (_, _) => InvalidateVisual(); view.SizeChanged += (_, _) => InvalidateVisual();
        view.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => InvalidateVisual()));
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        TextPaper.Draw(dc, RenderSize, PaperStyle, Compact, Excerpt, VisualTreeHelper.GetDpi(this).PixelsPerDip, textView is null ? null : DrawTextRules);
    }
    private void DrawTextRules(DrawingContext dc, Rect paper)
    {
        var rule = TextPaper.Palette(PaperStyle).Rule;
        if (rule is null || textView is null || textView.LineCount == 0 || !textView.IsArrangeValid) return;
        var first = textView.GetFirstVisibleLineIndex(); var last = textView.GetLastVisibleLineIndex();
        if (first < 0 || last < first) return;
        var pen = new Pen(DemoAssets.Brush(rule), .7); double? previous = null; var step = 24d; var bottom = paper.Top;
        for (var line = first; line <= last && line < first + 100; line++)
        {
            var index = textView.GetCharacterIndexFromLineIndex(line); if (index < 0) continue;
            var rect = textView.GetRectFromCharacterIndex(index); if (rect.IsEmpty) continue;
            var y = textView.TranslatePoint(new Point(0, rect.Bottom + 2), this).Y;
            if (previous is double before && y > before) step = y - before;
            previous = y; bottom = y;
            if (y > paper.Top && y < paper.Bottom) dc.DrawLine(pen, new Point(paper.Left + 1, y), new Point(paper.Right - 1, y));
        }
        for (var y = bottom + Math.Max(12, step); previous is not null && y < paper.Bottom; y += Math.Max(12, step))
            dc.DrawLine(pen, new Point(paper.Left + 1, y), new Point(paper.Right - 1, y));
    }
}

internal static class TextCardImage
{
    internal const int Width = 400, Height = 330;
    internal static BitmapSource Create(string text, TextPaperStyle style = TextPaperStyle.Notebook)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) TextPaper.Draw(dc, new Size(Width / 2d, Height / 2d), style, true, text, 2);
        var bitmap = new RenderTargetBitmap(Width, Height, 192, 192, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}
