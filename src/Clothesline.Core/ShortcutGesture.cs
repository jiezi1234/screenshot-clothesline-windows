namespace Clothesline.Core;

public sealed record ShortcutGesture(uint Modifiers, uint Key, string Display)
{
    public static (ShortcutGesture Show, ShortcutGesture Hide) ParsePair(string show, string hide)
    {
        var first = Parse(show); var second = Parse(hide);
        if (first.Key != 0 && first.Display == second.Display) throw new ArgumentException(T("显示和收回不能使用相同的快捷键。"));
        return (first, second);
    }
    public static ShortcutGesture Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(0, 0, "");
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2) throw new ArgumentException(T("快捷键需要 Ctrl 或 Alt，并搭配字母、数字、功能键或上下方向键。"));
        uint modifiers = 0;
        foreach (var part in parts.Take(parts.Length - 1))
        {
            var bit = part.ToUpperInvariant() switch { "CTRL" => 2u, "ALT" => 1u, "SHIFT" => 4u, _ => 0u };
            if (bit == 0 || (modifiers & bit) != 0) throw new ArgumentException(T("修饰键仅支持 Ctrl、Alt、Shift，且不能重复。"));
            modifiers |= bit;
        }
        if ((modifiers & 3) == 0) throw new ArgumentException(T("请至少包含 Ctrl 或 Alt，避免拦截普通输入。"));
        var key = parts[^1].ToUpperInvariant(); uint code;
        if (key.Length == 1 && (key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')) code = key[0];
        else if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var f) && f is >= 1 and <= 24 && f != 12) { code = (uint)(111 + f); key = "F" + f; }
        else if (key is "UP" or "↑") { code = 0x26; key = "Up"; }
        else if (key is "DOWN" or "↓") { code = 0x28; key = "Down"; }
        else throw new ArgumentException(T("主键支持 A–Z、0–9、Up/Down（↑/↓）、F1–F24（不含 F12）。"));
        var prefix = ((modifiers & 2) != 0 ? "Ctrl+" : "") + ((modifiers & 1) != 0 ? "Alt+" : "") + ((modifiers & 4) != 0 ? "Shift+" : "");
        return new(modifiers, code, prefix + key);
    }
}
