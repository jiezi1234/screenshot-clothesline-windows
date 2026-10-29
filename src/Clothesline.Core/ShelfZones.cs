namespace Clothesline.Core;

public sealed record ShelfLayout(IReadOnlyList<PixelRect> Media, IReadOnlyList<PixelRect> Text);

public static class ShelfZones
{
    private sealed record Metrics(int Width, int Height, int Y, int Margin, int Capacity, int NormalStep);
    private static Metrics Measure(PixelRect work, double scale, double cardScale)
    {
        var baseline = DesktopGeometry.Cards(work, scale, 100, cardScale);
        var card = baseline[0]; var width = card.Width; var height = card.Height;
        var margin = Math.Min((int)(16 * scale), Math.Max(0, (work.Width - width) / 2));
        if (baseline.Count == 1 && work.Width >= 2)
        {
            // Very narrow desktops still expose both types instead of hiding one behind the other.
            margin = Math.Min((int)(16 * scale), (work.Width - 2) / 4);
            width = Math.Max(1, (int)((work.Width - 2 * margin) / 1.6));
            height = Math.Max(1, (int)Math.Round(card.Height * width / (double)card.Width));
        }
        var available = work.Width - 2 * margin;
        var capacity = Math.Min(100, 1 + (available - width) / Math.Max(1, (int)Math.Ceiling(width * .6)));
        return new(width, height, card.Y, margin, capacity, width + Math.Max(1, (int)Math.Round(28 * scale * cardScale)));
    }
    public static int DisplayCapacity(PixelRect work, double scale, double cardScale = 1) => Measure(work, scale, cardScale).Capacity;
    public static (int Media, int Text) Allocate(int capacity, int media, int text)
    {
        if (capacity < 1 || media < 0 || text < 0) throw new ArgumentOutOfRangeException();
        if ((long)media + text <= capacity) return (media, text);
        if (media == 0) return (0, Math.Min(capacity, text));
        if (text == 0) return (Math.Min(capacity, media), 0);
        if (capacity == 1) return media >= text ? (1, 0) : (0, 1);
        var left = Math.Clamp((int)Math.Round(capacity * (double)media / ((long)media + text)), 1, capacity - 1);
        return (left, capacity - left);
    }
    public static ShelfLayout Plan(PixelRect work, double scale, int mediaCount, int textCount, double cardScale = 1)
    {
        var size = Measure(work, scale, cardScale); var (media, text) = Allocate(size.Capacity, mediaCount, textCount);
        var count = media + text;
        var step = count <= 1 ? 0 : Math.Min(size.NormalStep, (work.Width - 2 * size.Margin - size.Width) / (count - 1));
        var left = work.X + size.Margin; var right = work.Right - size.Margin - size.Width;
        return new(Enumerable.Range(0, media).Select(i => new PixelRect(left + i * step, size.Y, size.Width, size.Height)).ToArray(),
            Enumerable.Range(0, text).Select(i => new PixelRect(right - (text - 1 - i) * step, size.Y, size.Width, size.Height)).ToArray());
    }
}
