using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Clothesline.Core;
using Microsoft.Win32;

namespace Clothesline.App;

internal sealed class ShelfController : IDisposable
{
    private readonly RopeWindow rope = new();
    private readonly List<CardWindow> cards = new();
    private readonly RevealGate gate = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer timer;
    private MonitorInfo? activeMonitor;
    private PixelRect shelfArea;
    private double scale = 1;
    private long nextPrune;
    private bool presented;
    private int page;
    private bool sampleMode = true;
    public int DisplayCapacity { get; private set; } = 3;
    public bool Busy => cards.Any(c => c.InteractionActive);
    public event Action<DemoItem>? Archived;
    public event Action<DemoItem>? RecycleRequested;
    public event Action<DemoItem, bool>? ExportRequested;
    public event Action? FilesChanged;
    public event Action? EmptyRevealRequested;
    public bool ReduceMotion { get; set; }
    public bool SuppressFullscreen { get; set; } = true;
    public bool Visible => presented;
    public int Count => cards.Count;
    public double LastShowMilliseconds { get; private set; }
    public event Action<string>? Status;

    public ShelfController()
    {
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => Tick();
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
    }
    public void Start(bool samples = true) { sampleMode = samples; Prepare(Native.MonitorAt(Native.Cursor)); if (samples) ResetSamples(); timer.Start(); }
    public void SetItems(System.Collections.Generic.IEnumerable<DemoItem> items, bool reveal)
    {
        var desired = items.ToArray();
        if (cards.Select(c => c.Item).SequenceEqual(desired)) { if (reveal && !presented && cards.Count > 0) Toggle(); return; }
        var wasVisible = presented;
        var previous = cards.ToDictionary(c => c.Item.EntryId ?? c.Item.Path);
        foreach (var card in cards) if (!desired.Contains(card.Item)) card.Close();
        cards.Clear(); if (reveal) page = 0;
        foreach (var item in desired)
        {
            if (previous.TryGetValue(item.EntryId ?? item.Path, out var existing) && existing.Item == item) cards.Add(existing);
            else AddCard(item);
        }
        if (cards.Count == 0) { Hide(); return; }
        if (wasVisible) Present();
        else if (reveal) Toggle();
    }
    private void AddCard(DemoItem item)
    {
        var card = new CardWindow(item);
        card.Status += text => Status?.Invoke(text);
        card.TakeDown += Remove;
        card.RecycleRequested = sampleMode ? null : entry => RecycleRequested?.Invoke(entry);
        card.ExportRequested = (entry, desktop) => ExportRequested?.Invoke(entry, desktop);
        card.DragEnded += () => timer.Dispatcher.BeginInvoke((Action)(() => { Prune(); FilesChanged?.Invoke(); }));
        card.PreviewMouseWheel += (_, e) => { if (!Busy) { var count = Math.Max(1, (cards.Count + DisplayCapacity - 1) / DisplayCapacity); page = (page + (e.Delta < 0 ? 1 : count - 1)) % count; Present(); gate.Show(clock.ElapsedMilliseconds); e.Handled = true; } };
        cards.Add(card);
    }
    public void ResetSamples()
    {
        Hide();
        foreach (var card in cards) card.Close();
        cards.Clear();
        foreach (var item in DemoAssets.Create())
        {
            AddCard(item);
        }
        Status?.Invoke("已生成 3 张原创示例。取下会保留文件；恢复示例会生成新文件。");
    }
    public void Toggle()
    {
        if (Busy) return;
        if (presented) { Hide(); return; }
        if (cards.Count == 0)
        {
            Status?.Invoke("晾衣绳为空。点击“导入图片…”添加图片，或在“本地历史”中重新挂上。新截图保存到监听目录后也会自动加入。");
            EmptyRevealRequested?.Invoke();
            return;
        }
        var monitor = Native.MonitorAt(Native.Cursor);
        if (SuppressFullscreen && Native.IsFullscreen(monitor)) { Status?.Invoke("当前显示器为全屏窗口，已暂缓显示。"); return; }
        Prepare(monitor); gate.Show(clock.ElapsedMilliseconds); Present();
    }
    public void Hide() { gate.Hide(); Dismiss(); }
    private void Remove(CardWindow card)
    {
        cards.Remove(card); card.Close();
        if (!sampleMode) Archived?.Invoke(card.Item);
        Status?.Invoke(File.Exists(card.Item.Path) ? "已从晾衣绳取下；示例文件仍在本地。" : "文件已移走，已取下对应卡片。");
        if (cards.Count == 0) Hide();
        else if (presented) Present();
    }
    private void Prune()
    {
        if (Busy) return;
        foreach (var card in cards.Where(c => !File.Exists(c.Item.Path)).ToArray()) Remove(card);
    }
    private void Prepare(MonitorInfo monitor)
    {
        activeMonitor = monitor;
        scale = Native.MoveAndGetScale(rope, monitor);
        DisplayCapacity = DesktopGeometry.Cards(monitor.WorkArea, scale, 100).Count;
    }
    private void Tick()
    {
        var now = clock.ElapsedMilliseconds;
        if (now >= nextPrune) { nextPrune = now + 2000; Prune(); }
        if (cards.Count == 0) return;
        var cursor = Native.Cursor;
        var monitor = Native.MonitorAt(cursor);
        // Poll cursor only: hidden overlays have no hit-test surface at the top edge.
        if (!presented && activeMonitor?.Handle != monitor.Handle) Prepare(monitor);
        var target = activeMonitor ?? monitor;
        var busy = Busy || Native.GetAsyncKeyState(1) < 0;
        gate.Tick(now, DesktopGeometry.Trigger(target.Bounds, scale).Contains(cursor),
            presented && shelfArea.Contains(cursor), busy, SuppressFullscreen && Native.IsFullscreen(target));
        if (gate.Visible && !presented) Present();
        else if (!gate.Visible && presented) Dismiss();
    }
    private void Present()
    {
        var watch = Stopwatch.StartNew();
        var monitor = activeMonitor ?? Native.MonitorAt(Native.Cursor);
        Prepare(monitor);
        var rects = DesktopGeometry.Cards(monitor.WorkArea, scale, cards.Count);
        rope.Present(new PixelRect(monitor.WorkArea.X, monitor.WorkArea.Y, monitor.WorkArea.Width, (int)(70 * scale)), !presented && !ReduceMotion);
        for (var i = 0; i < cards.Count; i++)
        {
            page = Math.Min(page, (cards.Count - 1) / rects.Count);
            var slot = i - page * rects.Count;
            if (slot >= 0 && slot < rects.Count) { Native.MoveAndGetScale(cards[i], monitor); cards[i].Present(rects[slot], !presented && !ReduceMotion); }
            else cards[i].Dismiss();
        }
        shelfArea = new PixelRect(rects[0].X - (int)(16 * scale), monitor.WorkArea.Y,
            rects[^1].Right - rects[0].X + (int)(32 * scale), rects[0].Bottom - monitor.WorkArea.Y + (int)(14 * scale));
        presented = true; LastShowMilliseconds = watch.Elapsed.TotalMilliseconds;
    }
    private void Dismiss() { rope.Dismiss(); foreach (var card in cards) card.Dismiss(); presented = false; }
    private void DisplaysChanged(object? sender, EventArgs e) => timer.Dispatcher.BeginInvoke((Action)(() => { Hide(); activeMonitor = null; Status?.Invoke("显示器布局已变化，已重置晾衣绳位置。"); }));
    public void Dispose()
    {
        timer.Stop(); SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
        foreach (var card in cards) card.Close(); rope.Close();
    }
}
