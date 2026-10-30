namespace Clothesline.Core;

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
            // Keep at least two usable slots on narrow desktops for the per-category quotas.
            margin = Math.Min((int)(16 * scale), (work.Width - 2) / 4);
            width = Math.Max(1, (int)((work.Width - 2 * margin) / 1.6));
            height = Math.Max(1, (int)Math.Round(card.Height * width / (double)card.Width));
        }
        var available = work.Width - 2 * margin;
        var capacity = Math.Min(100, 1 + (available - width) / Math.Max(1, (int)Math.Ceiling(width * .6)));
        return new(width, height, card.Y, margin, capacity, width + Math.Max(1, (int)Math.Round(28 * scale * cardScale)));
    }
    public static int DisplayCapacity(PixelRect work, double scale, double cardScale = 1) => Measure(work, scale, cardScale).Capacity;
    public static int TypeCapacity(int displayCapacity) => Math.Max(0, displayCapacity / 2);
    public static IReadOnlyList<PixelRect> Row(PixelRect work, double scale, int requestedCount, double cardScale = 1)
    {
        if (requestedCount < 0) throw new ArgumentOutOfRangeException(nameof(requestedCount));
        var size = Measure(work, scale, cardScale); var count = Math.Min(requestedCount, size.Capacity);
        var step = count <= 1 ? 0 : Math.Min(size.NormalStep, (work.Width - 2 * size.Margin - size.Width) / (count - 1));
        // Slots follow persisted order; media type never affects position or spacing.
        var rowWidth = size.Width + Math.Max(0, count - 1) * step;
        var start = work.X + (work.Width - rowWidth) / 2;
        return Enumerable.Range(0, count).Select(i => new PixelRect(start + i * step, size.Y, size.Width, size.Height)).ToArray();
    }
}
