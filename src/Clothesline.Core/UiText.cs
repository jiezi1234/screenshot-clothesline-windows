using System.Globalization;
using System.Text.Json;

namespace Clothesline.Core;

/// Chinese source strings are stable keys. Only app-owned text is translated; paths and user content are never looked up.
public static class UiText
{
    public sealed record Locale(string Code, string Name);
    public static IReadOnlyList<Locale> Languages { get; } = new Locale[] {
        new("zh-CN", "简体中文"), new("en", "English") };
    private static readonly Dictionary<string, string> english = Load("en");
    public static string Language { get; set => field = NormalizeLanguage(value); } = "zh-CN";
    public static string NormalizeLanguage(string? language) => language == "en" ? "en" : "zh-CN";
    public static IReadOnlyDictionary<string, string> English => english;
    public static bool IsSupported(string language) => Languages.Any(l => l.Code == language);
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language);
    private static Dictionary<string, string> Load(string language)
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream($"Clothesline.Core.Strings.{language}.json");
        return stream is null ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    public static string T(string source) => Language == "zh-CN" ? source : english.GetValueOrDefault(source, source);
    public static string T(string source, params object[] arguments) => string.Format(Culture, T(source), arguments);
}
