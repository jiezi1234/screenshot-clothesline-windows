using System;
using System.Collections.Generic;
using System.Linq;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed partial class ShelfController
{
    private int page;
    private int DragCapacity => Math.Max(1, DisplayCapacity);
    private PixelRect InteractionArea => activeMonitor!.WorkArea;
    private IReadOnlyList<PixelRect> RowSlots => ShelfZones.Row(activeMonitor!.WorkArea, scale,
        Math.Clamp(cards.Count - page * DragCapacity, 0, DragCapacity), cardScale);
    // Preview reorders this copy. The real list changes only after a successful commit.
    private List<CardWindow> DragCards => cards.ToList();
    private IReadOnlyList<PixelRect> DragSlots => RowSlots;
    private IEnumerable<(CardWindow Card, PixelRect Rect)> VisibleRow()
    {
        var slots = RowSlots; var offset = page * DragCapacity;
        for (var i = 0; i < slots.Count; i++) yield return (cards[offset + i], slots[i]);
    }
}
