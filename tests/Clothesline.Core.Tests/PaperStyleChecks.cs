using Clothesline.Core;

internal static class PaperStyleChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var (id, expected) in new[]
        {
            ("00000000000000000000000000000000", TextPaperStyle.Sticky),
            ("00000000000000000000000000000002", TextPaperStyle.Notebook),
            ("00000000000000000000000000000003", TextPaperStyle.Journal),
            ("0000000000000000000000000000000f", TextPaperStyle.Index)
        }) check(TextPaperStyles.ForEntry(id) == expected, "paper style keeps its cross-process identity mapping: " + expected);
        var counts = Enumerable.Range(0, 1000).Select(i => TextPaperStyles.ForEntry(i.ToString("x32"))).GroupBy(style => style).ToArray();
        check(counts.Length == 4 && counts.All(group => group.Count() is > 180 and < 320), "all four paper styles are distributed across persisted capture IDs");
        var rejected = false; try { TextPaperStyles.ForEntry(""); } catch (ArgumentException) { rejected = true; }
        check(rejected, "missing identity cannot assign an unstable paper style");
    }
}
