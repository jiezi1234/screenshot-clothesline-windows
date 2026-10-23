using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clothesline.App;

internal enum EditorTool { Pan, Pen, Arrow, Rectangle, Text, Mosaic, Crop }
internal sealed record MosaicTile(Rect Area, Color Color);
internal sealed record ImageMark(EditorTool Tool, Point[] Points, Color Color, double Size, string Text, MosaicTile[] Tiles);
internal sealed record ImageEditState(Int32Rect Crop, ImageMark[] Marks, BitmapSource BaseImage);

/// Edits use original-image pixel coordinates. Undo states share immutable marks, not full bitmaps.
internal sealed class ImageDocument
{
    public BitmapSource Original { get; }
    public ImageEditState State { get; private set; }
    private readonly ImageEditState initial;
    private ImageEditState saved;
    private readonly List<ImageEditState> undo = new();
    private readonly List<ImageEditState> redo = new();
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public bool IsDirty => !ReferenceEquals(State, saved);
    public event Action? Changed;
    public ImageDocument(BitmapSource original)
    {
        if ((long)original.PixelWidth * original.PixelHeight > ImageFiles.MaxPixels) throw new ArgumentException(T("图片超过像素限制。"));
        Original = original.IsFrozen ? original : original.Clone(); Original.Freeze();
        initial = new(new Int32Rect(0, 0, Original.PixelWidth, Original.PixelHeight), [], Original);
        State = saved = initial;
    }
    private void Commit(ImageEditState state)
    {
        undo.Add(State); if (undo.Count > 100) undo.RemoveAt(0);
        redo.Clear(); State = state; Changed?.Invoke();
    }
    public void Undo() { if (!CanUndo) return; redo.Add(State); State = undo[^1]; undo.RemoveAt(undo.Count - 1); Changed?.Invoke(); }
    public void Redo() { if (!CanRedo) return; undo.Add(State); State = redo[^1]; redo.RemoveAt(redo.Count - 1); Changed?.Invoke(); }
    public void Reset() { if (!ReferenceEquals(State, initial)) Commit(initial); }
    public void RestoreOriginal(BitmapSource original)
    {
        if ((long)original.PixelWidth * original.PixelHeight > ImageFiles.MaxPixels) throw new ArgumentException(T("原版超过像素限制。"));
        var bitmap = original.IsFrozen ? original : original.Clone(); bitmap.Freeze();
        Commit(new(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight), [], bitmap));
    }
    public void MarkSaved() { saved = State; Changed?.Invoke(); }
    public Point Clamp(Point p) => new(Math.Clamp(p.X, State.Crop.X, State.Crop.X + State.Crop.Width), Math.Clamp(p.Y, State.Crop.Y, State.Crop.Y + State.Crop.Height));
    public ImageMark CreateMark(EditorTool tool, IEnumerable<Point> points, Color color, double size, string text = "")
    {
        var coordinates = points.Select(Clamp).Take(10000).ToArray();
        if (coordinates.Length == 0) throw new ArgumentException(T("缺少标注坐标。"));
        size = Math.Clamp(size, 1, 32);
        var tiles = tool == EditorTool.Mosaic && coordinates.Length > 1 ? Mosaic(new Rect(coordinates[0], coordinates[^1]), size) : [];
        return new(tool, coordinates, color, size, text.Length > 300 ? text[..300] : text, tiles);
    }
    public void Add(ImageMark mark)
    {
        if (mark.Tool is EditorTool.Pan or EditorTool.Crop) return;
        if (mark.Tool == EditorTool.Text && string.IsNullOrWhiteSpace(mark.Text)) return;
        if (mark.Tool == EditorTool.Mosaic && mark.Tiles.Length == 0) return;
        Commit(State with { Marks = [.. State.Marks, mark] });
    }
    public bool Crop(Rect selection)
    {
        selection.Intersect(new Rect(State.Crop.X, State.Crop.Y, State.Crop.Width, State.Crop.Height));
        if (selection.IsEmpty || selection.Width < 1 || selection.Height < 1) return false;
        var left = (int)Math.Floor(selection.Left); var top = (int)Math.Floor(selection.Top);
        var right = (int)Math.Ceiling(selection.Right); var bottom = (int)Math.Ceiling(selection.Bottom);
        var crop = new Int32Rect(left, top, right - left, bottom - top);
        if (crop == State.Crop) return false;
        Commit(State with { Crop = crop }); return true;
    }
    private MosaicTile[] Mosaic(Rect selection, double size)
    {
        selection.Intersect(new Rect(State.Crop.X, State.Crop.Y, State.Crop.Width, State.Crop.Height));
        if (selection.IsEmpty || selection.Width < 1 || selection.Height < 1) return [];
        var area = new Int32Rect((int)Math.Floor(selection.Left), (int)Math.Floor(selection.Top), (int)Math.Ceiling(selection.Right) - (int)Math.Floor(selection.Left), (int)Math.Ceiling(selection.Bottom) - (int)Math.Floor(selection.Top));
        var bitmap = new FormatConvertedBitmap(State.BaseImage, PixelFormats.Bgra32, null, 0);
        var stride = checked(area.Width * 4); var pixels = new byte[checked(stride * area.Height)];
        bitmap.CopyPixels(area, pixels, stride, 0);
        // Bound the number of tiles so large selections do not create huge undo states.
        var block = Math.Max(Math.Max(12, (int)size * 4), (int)Math.Ceiling(Math.Sqrt((double)area.Width * area.Height / 10000)));
        var tiles = new List<MosaicTile>();
        for (var y = 0; y < area.Height; y += block)
        for (var x = 0; x < area.Width; x += block)
        {
            var width = Math.Min(block, area.Width - x); var height = Math.Min(block, area.Height - y);
            long red = 0, green = 0, blue = 0;
            for (var row = y; row < y + height; row++)
            for (var column = x; column < x + width; column++)
            {
                var p = row * stride + column * 4; var alpha = pixels[p + 3];
                blue += (pixels[p] * alpha + 255 * (255 - alpha)) / 255;
                green += (pixels[p + 1] * alpha + 255 * (255 - alpha)) / 255;
                red += (pixels[p + 2] * alpha + 255 * (255 - alpha)) / 255;
            }
            var count = width * height;
            tiles.Add(new(new Rect(area.X + x, area.Y + y, width, height), Color.FromRgb((byte)(red / count), (byte)(green / count), (byte)(blue / count))));
        }
        return tiles.ToArray();
    }
    public void Draw(DrawingContext dc, ImageMark? pending = null)
    {
        var crop = State.Crop;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, crop.Width, crop.Height)));
        dc.PushTransform(new TranslateTransform(-crop.X, -crop.Y));
        dc.DrawImage(State.BaseImage, new Rect(0, 0, State.BaseImage.PixelWidth, State.BaseImage.PixelHeight));
        foreach (var mark in State.Marks) DrawMark(dc, mark);
        if (pending is not null) DrawMark(dc, pending);
        dc.Pop(); dc.Pop();
    }
    private static void DrawMark(DrawingContext dc, ImageMark mark)
    {
        var brush = new SolidColorBrush(mark.Color); brush.Freeze();
        var pen = new Pen(brush, mark.Size) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var start = mark.Points[0]; var end = mark.Points[^1];
        switch (mark.Tool)
        {
            case EditorTool.Pen:
                if (mark.Points.Length == 1) dc.DrawEllipse(brush, null, start, mark.Size / 2, mark.Size / 2);
                else { var line = new StreamGeometry(); using (var context = line.Open()) { context.BeginFigure(start, false, false); context.PolyLineTo(mark.Points.Skip(1).ToArray(), true, false); } dc.DrawGeometry(null, pen, line); }
                break;
            case EditorTool.Rectangle: dc.DrawRectangle(null, pen, new Rect(start, end)); break;
            case EditorTool.Arrow:
                dc.DrawLine(pen, start, end);
                var delta = end - start;
                if (delta.Length > 0.1)
                {
                    delta.Normalize(); var side = new Vector(-delta.Y, delta.X); var length = Math.Max(12, mark.Size * 3);
                    dc.DrawLine(pen, end, end - delta * length + side * length * .5);
                    dc.DrawLine(pen, end, end - delta * length - side * length * .5);
                }
                break;
            case EditorTool.Text:
                var text = new FormattedText(mark.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Microsoft YaHei UI"), Math.Max(14, mark.Size * 4), brush, 1);
                dc.DrawText(text, start); break;
            case EditorTool.Mosaic:
                foreach (var tile in mark.Tiles) dc.DrawRectangle(new SolidColorBrush(tile.Color), null, tile.Area);
                break;
        }
    }
    public BitmapSource Render()
    {
        var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen()) Draw(dc);
        var result = new RenderTargetBitmap(State.Crop.Width, State.Crop.Height, 96, 96, PixelFormats.Pbgra32);
        result.Render(drawing); result.Freeze(); return result;
    }
}
