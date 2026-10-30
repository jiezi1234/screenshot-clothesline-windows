using Clothesline.Core;

internal static class UnifiedShelfChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var work = new PixelRect(0, 0, 1920, 1080);
        var six = ShelfZones.Row(work, 1, 6);
        check(six[0].X == 254 && six[5].X - six[4].Right == 28 && six[5].Right == 1666,
            "six freely ordered cards share one centered row without a type gap");
        foreach (var (media, notes) in new[] { (6, 0), (5, 1), (3, 3), (1, 5), (0, 6) })
            check(ShelfZones.Row(work, 1, media + notes).SequenceEqual(DesktopGeometry.Cards(work, 1, 6)),
                $"six cards keep identical positions regardless of type mix {media}/{notes}");
        check(ShelfZones.Row(work, 1, 1).Single().X == 854, "a lone card uses the same centered slot regardless of type");
        var crowded = ShelfZones.Row(work, 1, 50);
        check(DesktopGeometry.Stacked(crowded) && crowded.Zip(crowded.Skip(1)).Select(p => p.Second.X - p.First.X).Distinct().Count() == 1,
            "mixed crowded row uses equal overlap across every neighboring pair");
        var total = 2 * ShelfZones.DisplayCapacity(work, 1) + 6;
        check(ShelfZones.Row(work, 1, total % ShelfZones.DisplayCapacity(work, 1)).SequenceEqual(six),
            "short final mixed page closes unused slots and recenters all cards");
        foreach (var (dpi, size) in new[] { (1.0, .8), (1.25, 1.0), (2.5, 1.2) })
        {
            var negativeWork = new PixelRect(-2560, -200, 2560, 1440);
            var count = Math.Min(5, ShelfZones.DisplayCapacity(negativeWork, dpi, size));
            var row = ShelfZones.Row(negativeWork, dpi, count, size);
            check(row.Count == count && row.All(r => r.X >= negativeWork.X && r.Right <= negativeWork.Right)
                && Math.Abs(row[0].X - negativeWork.X - (negativeWork.Right - row[^1].Right)) <= 1,
                $"free row stays centered at negative coordinates and DPI {dpi}, size {size}");
        }
        check(ShelfZones.TypeCapacity(20) == 10 && ShelfZones.TypeCapacity(21) == 10, "category quota rounds down half of current display capacity");
        check(ShelfZones.TypeCapacity(2) == 1 && ShelfZones.TypeCapacity(1) == 0 && ShelfZones.TypeCapacity(100) == 50, "category quota respects smallest and largest screen capacities");
    }
}
