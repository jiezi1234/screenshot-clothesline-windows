using System.Globalization;
using System.Text.Json;

namespace Clothesline.Core;

/// Chinese source strings are stable keys. Only app-owned text is translated; paths and user content are never looked up.
public static class UiText
{
    private static readonly Dictionary<string, string> english = Load();
    public static string Language { get; set; } = "zh-CN";
    public static IReadOnlyDictionary<string, string> English => english;
    private static Dictionary<string, string> Load()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("Clothesline.Core.Strings.en.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    public static string T(string source) => Language == "en" && english.TryGetValue(source, out var translated) ? translated : source;
    public static string T(string source, params object[] arguments) => string.Format(Language == "en" ? CultureInfo.GetCultureInfo("en") : CultureInfo.GetCultureInfo("zh-CN"), T(source), arguments);
}
