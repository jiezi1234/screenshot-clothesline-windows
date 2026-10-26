using System;
using System.Collections.Generic;
using System.Linq;
using Clothesline.Core;

namespace Clothesline.App;

/// Keeps both registrations until every replacement and settings write has succeeded.
internal sealed class ShelfHotKeys : IDisposable
{
    private Dictionary<string, HotKey> keys = new(StringComparer.Ordinal);
    private readonly Action show, hide;
    public string ShowGesture { get; private set; }
    public string HideGesture { get; private set; }
    public bool ShowRegistered => IsRegistered(ShowGesture);
    public bool HideRegistered => IsRegistered(HideGesture);
    public bool Registered => keys.Count > 0 && keys.Values.All(key => key.Registered);
    private bool IsRegistered(string gesture) => keys.TryGetValue(gesture, out var key) && key.Registered;
    public ShelfHotKeys(Action show, Action hide, string showGesture, string hideGesture)
    {
        var pair = ShortcutGesture.ParsePair(showGesture, hideGesture);
        this.show = show; this.hide = hide; ShowGesture = pair.Show.Display; HideGesture = pair.Hide.Display;
        try
        {
            foreach (var gesture in new[] { ShowGesture, HideGesture }.Where(g => g.Length > 0))
                keys.Add(gesture, new HotKey(() => Dispatch(gesture), gesture));
        }
        catch { Dispose(); throw; }
    }
    internal void Dispatch(string gesture)
    {
        if (!IsRegistered(gesture)) return;
        if (gesture == ShowGesture) show();
        else if (gesture == HideGesture) hide();
    }
    public PreparedShortcut Prepare(string showGesture, string hideGesture)
    {
        var pair = ShortcutGesture.ParsePair(showGesture, hideGesture);
        var next = new Dictionary<string, HotKey>(StringComparer.Ordinal); var created = new List<HotKey>();
        try
        {
            foreach (var gesture in new[] { pair.Show.Display, pair.Hide.Display }.Where(g => g.Length > 0))
            {
                if (keys.TryGetValue(gesture, out var existing) && existing.Registered) { next.Add(gesture, existing); continue; }
                var key = new HotKey(() => Dispatch(gesture), gesture); created.Add(key);
                if (!key.Registered) throw new InvalidOperationException(T("快捷键被占用或被系统保留，原设置已保留。请选择其他组合。"));
                next.Add(gesture, key);
            }
            return new(() =>
            {
                var previous = keys; keys = next; ShowGesture = pair.Show.Display; HideGesture = pair.Hide.Display;
                foreach (var key in previous.Values.Where(key => !next.Values.Contains(key))) key.Dispose();
            }, () => { foreach (var key in created) key.Dispose(); });
        }
        catch { foreach (var key in created) key.Dispose(); throw; }
    }
    public bool Retry()
    {
        var success = true;
        foreach (var key in keys.Values) if (!key.Retry()) success = false;
        return success;
    }
    public void Dispose() { foreach (var key in keys.Values) key.Dispose(); keys.Clear(); }
}
