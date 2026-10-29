using System;
using System.Collections.Generic;
using System.Linq;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed partial class ShelfController
{
    private int mediaPage, textPage;
    private bool DragText => (localReorder?.Source ?? draggedCard)?.Item.IsText == true;
    private int page { get => ZonePage(DragText); set => SetZonePage(DragText, value); }
    private (int Media, int Text) PageAllocation => ShelfZones.Allocate(DisplayCapacity, ZoneCards(false).Count, ZoneCards(true).Count);
    internal int ZoneCapacity(bool text) => Math.Max(1, text ? PageAllocation.Text : PageAllocation.Media);
    private int DragCapacity => ZoneCapacity(DragText);
    private List<CardWindow> ZoneCards(bool text) => cards.Where(c => c.Item.IsText == text).ToList();
    private int ZonePage(bool text) => text ? textPage : mediaPage;
    private void SetZonePage(bool text, int value) { if (text) textPage = value; else mediaPage = value; }
    private PixelRect InteractionArea => activeMonitor!.WorkArea;
    private IReadOnlyList<PixelRect> ZoneSlots(bool text)
    {
        int Visible(bool notes) => Math.Clamp(ZoneCards(notes).Count - ZonePage(notes) * ZoneCapacity(notes), 0, ZoneCapacity(notes));
        var layout = ShelfZones.Plan(activeMonitor!.WorkArea, scale, Visible(false), Visible(true), cardScale);
        return text ? layout.Text : layout.Media;
    }
    private List<CardWindow> DragCards => ZoneCards(DragText);
    private IReadOnlyList<PixelRect> DragSlots => ZoneSlots(DragText);
    private IEnumerable<(CardWindow Card, PixelRect Rect)> ZoneRow(bool text)
    {
        var group = ZoneCards(text); var slots = ZoneSlots(text);
        if (slots.Count == 0) yield break;
        var offset = ZonePage(text) * ZoneCapacity(text);
        for (var i = 0; i < slots.Count && offset + i < group.Count; i++) yield return (group[offset + i], slots[i]);
    }
    private void PreviewOtherZone(bool text)
    {
        foreach (var (card, rect) in ZoneRow(!text)) dropTarget.Preview(card, rect, rect, dropBounds, scale, 0, false);
    }
    private void NormalizeZones() { var ordered = cards.OrderBy(c => c.Item.IsText).ToArray(); cards.Clear(); cards.AddRange(ordered); }
}
