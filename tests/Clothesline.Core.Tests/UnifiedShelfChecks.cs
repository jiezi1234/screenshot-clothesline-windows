using Clothesline.Core;

internal static class UnifiedShelfChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var work = new PixelRect(0, 0, 1920, 1080);
        var screenshot = ShelfZones.Plan(work, 1, 5, 1);
        check(screenshot.Media[0].X == 254 && screenshot.Text[0].X - screenshot.Media[^1].Right == 28
            && screenshot.Text[0].Right == 1666, "five screenshots and one note share the same gap without an empty zone");
        foreach (var (media, notes) in new[] { (6, 0), (5, 1), (3, 3), (1, 5), (0, 6) })
        {
            var layout = ShelfZones.Plan(work, 1, media, notes);
            var row = layout.Media.Concat(layout.Text);
            check(row.SequenceEqual(DesktopGeometry.Cards(work, 1, 6)), $"six cards keep identical positions regardless of type mix {media}/{notes}");
        }
        var onePhoto = ShelfZones.Plan(work, 1, 1, 0).Media.Single();
        var oneNote = ShelfZones.Plan(work, 1, 0, 1).Text.Single();
        check(onePhoto == oneNote && oneNote.X == 854, "a lone photo or note uses the same centered slot");
        var crowded = ShelfZones.Plan(work, 1, 25, 25);
        var crowdedRow = crowded.Media.Concat(crowded.Text).ToArray();
        check(DesktopGeometry.Stacked(crowdedRow) && crowded.Text[0].X - crowded.Media[^1].X == crowdedRow[1].X - crowdedRow[0].X,
            "crowded media-text boundary shares the same overlap as other cards");
        var allocation = ShelfZones.Allocate(ShelfZones.DisplayCapacity(work, 1), 5, 10);
        var lastPage = ShelfZones.Plan(work, 1, 5, 10 % allocation.Text);
        var lastRow = lastPage.Media.Concat(lastPage.Text).ToArray();
        check(lastPage.Text.Count == 1 && lastRow.SequenceEqual(DesktopGeometry.Cards(work, 1, 6)),
            "short final text page closes its unused slots and recenters both types");
        foreach (var (dpi, size) in new[] { (1.0, .8), (1.25, 1.0), (2.5, 1.2) })
        {
            var negativeWork = new PixelRect(-2560, -200, 2560, 1440);
            var mixed = ShelfZones.Plan(negativeWork, dpi, 3, 2, size);
            var sameType = ShelfZones.Plan(negativeWork, dpi, 5, 0, size);
            check(mixed.Media.Concat(mixed.Text).SequenceEqual(sameType.Media), $"type-neutral placement survives negative coordinates and DPI {dpi}, size {size}");
        }
    }
}
