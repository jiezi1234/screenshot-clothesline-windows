using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Animation;
using Clothesline.Core;

namespace Clothesline.App;

internal static class ShelfPresentation
{
    internal static void Present(IReadOnlyList<(OverlayWindow Window, PixelRect Bounds)> row, MonitorInfo monitor, bool animate)
    {
        try
        {
            foreach (var (window, bounds) in row) window.PrepareShelfPresentation(bounds, monitor);
            // A single clock gives every entering window exactly the same fade progress.
            var fade = animate && row.Any(item => item.Window.EntrancePrepared)
                ? new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop }.CreateClock()
                : null;
            foreach (var (window, _) in row) window.RevealPreparedEntrance(fade);
        }
        catch
        {
            // A failed preparation must not leave invisible, interactive windows behind.
            foreach (var (window, _) in row)
                if (window.EntrancePrepared) window.Dismiss();
            throw;
        }
    }
}
