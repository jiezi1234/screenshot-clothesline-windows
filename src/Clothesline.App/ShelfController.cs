using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using System.Windows;
using Clothesline.Core;
using Microsoft.Win32;

namespace Clothesline.App;

internal sealed partial class ShelfController : IDisposable
{
    private readonly RopeWindow rope = new();
    private readonly List<CardWindow> cards = new();
    private readonly HashSet<CardWindow> retiring = new();
    private readonly Func<bool> systemMotion;
    private bool reduceMotion, disposed;
    private readonly RevealGate gate = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer timer;
    private MonitorInfo? activeMonitor;
    private PixelRect shelfArea;
    private double scale = 1;
    private double cardScale = 1;
    private long nextPrune;
    private bool presented;
    private bool suspended, locked, disconnected;
    internal bool SessionBlocked => suspended || locked || disconnected;
    public event Action? EnvironmentRecovered;
    private int page;
    private bool sampleMode = true;
    public int DisplayCapacity { get; private set; } = 3;
    public bool Busy => localReorder is not null || previewCards.Count > 0 || importingDrop || cards.Any(c => c.InteractionActive);
    public event Action<DemoItem>? Archived;
    public event Action<DemoItem>? RecycleRequested;
    public event Action<DemoItem, bool>? ExportRequested;
    public event Action<DemoItem, bool>? WorkspaceRequested;
    public event Action<DemoItem, ArchiveScope>? BatchArchiveRequested;
    public event Action? FilesChanged;
    public bool ReduceMotion { get => reduceMotion; set { reduceMotion = value; RefreshMotion(); } }
    internal bool MotionAllowed => !ReduceMotion && systemMotion();
    internal int RetiringCount => retiring.Count;
    public bool SuppressFullscreen { get; set; } = true;
    public int RevealProtectionSeconds { get => gate.ProtectionSeconds; set => gate.ConfigureProtection(value, clock.ElapsedMilliseconds); }
    public bool Visible => presented;
    public int Count => cards.Count;
    public double LastShowMilliseconds { get; private set; }
    public event Action<string>? Status;

    public ShelfController(Func<bool>? systemMotion = null)
    {
        this.systemMotion = systemMotion ?? (() => SystemParameters.ClientAreaAnimation);
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => Tick();
        InitializeDropTarget();
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.SessionSwitch += SessionChanged;
        SystemParameters.StaticPropertyChanged += SystemMotionChanged;
    }
    internal void RefreshMotion()
    {
        if (disposed || MotionAllowed) return;
        rope.StopMotion(); foreach (var card in cards) card.StopMotion(); CloseRetiring();
        if (localReorder is not null) RenderLocalReorder(localReorder, localReorder.Cursor, localReorder.Ending);
        else if (previewCards.Count > 0) RenderIncomingGap();
    }
    private void SystemMotionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation) && !disposed)
            timer.Dispatcher.BeginInvoke((Action)(() => { if (!disposed) RefreshMotion(); }));
    }
    public void Start(bool samples = true) { sampleMode = samples; Prepare(Native.MonitorAt(Native.Cursor)); if (samples) ResetSamples(); timer.Start(); }
    public void RefreshLanguage() => rope.RefreshLanguage();
    public void SetCardSize(int percent)
    {
        cardScale = percent / 100.0; Prepare(activeMonitor ?? Native.MonitorAt(Native.Cursor));
        if (presented) Present();
    }
    public void SetItems(System.Collections.Generic.IEnumerable<DemoItem> items, bool reveal)
    {
        var desired = items.ToArray();
        if (cards.Select(c => c.Item).SequenceEqual(desired)) { if (reveal && !presented) Toggle(); return; }
        var wasVisible = presented;
        var previous = cards.ToDictionary(c => c.Item.EntryId ?? c.Item.Path);
        foreach (var card in cards)
            if (!desired.Contains(card.Item)) Retire(card, !desired.Any(item => (item.EntryId ?? item.Path) == (card.Item.EntryId ?? card.Item.Path)));
        cards.Clear(); if (reveal) page = 0;
        foreach (var item in desired)
        {
            if (previous.TryGetValue(item.EntryId ?? item.Path, out var existing) && existing.Item == item) cards.Add(existing);
            else AddCard(item);
        }
        if (cards.Count == 0 && wasVisible) gate.Show(clock.ElapsedMilliseconds);
        if (wasVisible) Present();
        else if (reveal) Toggle();
    }
    private void AddCard(DemoItem item)
    {
        var card = new CardWindow(item);
        card.DragRequested = DragCardWithPreview; AttachDropTarget(card);
        card.Status += text => Status?.Invoke(text);
        card.TakeDown += Remove;
        card.RecycleRequested = sampleMode ? null : entry => RecycleRequested?.Invoke(entry);
        card.ExportRequested = (entry, desktop) => ExportRequested?.Invoke(entry, desktop);
        card.WorkspaceRequested = (entry, edit) => WorkspaceRequested?.Invoke(entry, edit);
        card.BatchArchiveRequested = (entry, scope) => BatchArchiveRequested?.Invoke(entry, scope);
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
        Status?.Invoke(T("已生成 3 张原创示例。取下会保留文件；恢复示例会生成新文件。"));
    }
    public void Toggle()
    {
        if (Busy || SessionBlocked) return;
        if (presented) { Hide(); return; }
        Show();
    }
    public void Show()
    {
        if (Busy || SessionBlocked) return;
        if (presented) { gate.Show(clock.ElapsedMilliseconds); return; }
        var monitor = Native.MonitorAt(Native.Cursor);
        if (SuppressFullscreen && Native.IsFullscreen(monitor)) { Status?.Invoke(T("当前显示器为全屏窗口，已暂缓显示。")); return; }
        Prepare(monitor); gate.Show(clock.ElapsedMilliseconds); Present();
    }
    public void Hide() { gate.Hide(); Dismiss(); }
    public void HideFromShortcut() { if (!Busy) Hide(); }
    private void Remove(CardWindow card)
    {
        if (!sampleMode) Archived?.Invoke(card.Item);
        cards.Remove(card); Retire(card, true);
        Status?.Invoke(File.Exists(card.Item.Path) ? T("已从晾衣绳取下；文件仍在本地。") : T("文件已移走，已取下对应卡片。"));
        if (cards.Count == 0 && presented) gate.Show(clock.ElapsedMilliseconds);
        if (presented) Present();
    }
    private void Retire(CardWindow card, bool animate)
    {
        if (retiring.Count >= Math.Max(1, DisplayCapacity)) retiring.First().Close();
        retiring.Add(card); card.Closed += (_, _) => retiring.Remove(card);
        card.Retire(animate && presented && MotionAllowed);
    }
    private void CloseRetiring() { foreach (var card in retiring.ToArray()) card.Close(); retiring.Clear(); }
    private void Prune()
    {
        if (Busy) return;
        foreach (var card in cards.Where(c => !File.Exists(c.Item.Path)).ToArray()) Remove(card);
    }
    private void Prepare(MonitorInfo monitor)
    {
        scale = activeMonitor?.Handle == monitor.Handle && rope.IsVisible
            ? Math.Max(1, Native.GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(rope).EnsureHandle()) / 96.0)
            : Native.MoveAndGetScale(rope, monitor);
        activeMonitor = monitor;
        DisplayCapacity = DesktopGeometry.Cards(monitor.WorkArea, scale, 100, cardScale).Count;
    }
    private void Tick()
    {
        if (SessionBlocked) return;
        if (presented && DesktopLifecycle.IsCurrent(rope) == false) { Hide(); activeMonitor = null; return; }
        var now = clock.ElapsedMilliseconds;
        if (now >= nextPrune) { nextPrune = now + 2000; Prune(); }
        var cursor = Native.Cursor;
        var monitor = Native.MonitorAt(cursor);
        if (localReorder?.PollInput == true) PollLocalReorder();
        TrackDropTarget(now, cursor, monitor, Native.GetAsyncKeyState(1) < 0);
        // The temporary drop target is separate from the normal hover reveal gate.
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
        rope.ShowEmptyHint = cards.Count == 0;
        rope.Present(new PixelRect(monitor.WorkArea.X, monitor.WorkArea.Y, monitor.WorkArea.Width, (int)((cards.Count == 0 ? 100 : 70) * scale)), MotionAllowed);
        if (cards.Count == 0)
        {
            page = 0;
            var width = Math.Min(monitor.WorkArea.Width, (int)(360 * scale));
            shelfArea = new PixelRect(monitor.WorkArea.X + (monitor.WorkArea.Width - width) / 2, monitor.WorkArea.Y, width, (int)(100 * scale));
        }
        else
        {
            var rects = DesktopGeometry.Cards(monitor.WorkArea, scale, cards.Count, cardScale);
            for (var i = 0; i < cards.Count; i++)
            {
                page = Math.Min(page, (cards.Count - 1) / rects.Count);
                var slot = i - page * rects.Count;
                if (slot >= 0 && slot < rects.Count) { if (!cards[i].IsVisible) Native.MoveAndGetScale(cards[i], monitor); cards[i].Present(rects[slot], MotionAllowed); }
                else cards[i].Dismiss();
            }
            shelfArea = new PixelRect(rects[0].X - (int)(16 * scale), monitor.WorkArea.Y,
                rects[^1].Right - rects[0].X + (int)(32 * scale), rects[0].Bottom - monitor.WorkArea.Y + (int)(14 * scale));
        }
        presented = true; LastShowMilliseconds = watch.Elapsed.TotalMilliseconds;
    }
    private void Dismiss() { HideDropTarget(); rope.Dismiss(); foreach (var card in cards) card.Dismiss(); CloseRetiring(); presented = false; }
    internal void ResetEnvironment(bool recover)
    {
        if (disposed) return;
        Hide(); activeMonitor = null;
        if (recover && !SessionBlocked) { Prepare(Native.MonitorAt(Native.Cursor)); EnvironmentRecovered?.Invoke(); }
    }
    internal void HandlePower(PowerModes mode)
    {
        if (mode == PowerModes.Suspend) suspended = true;
        else if (mode == PowerModes.Resume) suspended = false;
        else return;
        ResetEnvironment(mode == PowerModes.Resume);
    }
    internal void HandleSession(SessionSwitchReason reason)
    {
        switch (reason)
        {
            case SessionSwitchReason.SessionLock: case SessionSwitchReason.SessionLogoff: locked = true; break;
            case SessionSwitchReason.SessionUnlock: case SessionSwitchReason.SessionLogon: locked = false; break;
            case SessionSwitchReason.RemoteDisconnect: case SessionSwitchReason.ConsoleDisconnect: disconnected = true; break;
            case SessionSwitchReason.RemoteConnect: case SessionSwitchReason.ConsoleConnect: disconnected = false; break;
            default: return;
        }
        ResetEnvironment(!SessionBlocked);
    }
    private void PowerChanged(object? sender, PowerModeChangedEventArgs e) => timer.Dispatcher.BeginInvoke((Action)(() => { if (!disposed) HandlePower(e.Mode); }));
    private void SessionChanged(object? sender, SessionSwitchEventArgs e) => timer.Dispatcher.BeginInvoke((Action)(() => { if (!disposed) HandleSession(e.Reason); }));
    private void DisplaysChanged(object? sender, EventArgs e) => timer.Dispatcher.BeginInvoke((Action)(() => { if (disposed) return; ResetEnvironment(true); Status?.Invoke(T("显示器布局已变化，已重置晾衣绳位置。")); }));
    public void Dispose()
    {
        disposed = true; timer.Stop(); SystemEvents.DisplaySettingsChanged -= DisplaysChanged; SystemParameters.StaticPropertyChanged -= SystemMotionChanged;
        SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.SessionSwitch -= SessionChanged;
        CloseRetiring();
        CancelLocalReorder(); dropTarget.Close();
        foreach (var card in cards) card.Close(); rope.Close();
    }
}
