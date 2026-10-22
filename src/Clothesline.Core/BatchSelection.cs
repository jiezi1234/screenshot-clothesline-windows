namespace Clothesline.Core;

public enum ArchiveScope { Left, Right, All }
public static class BatchSelection
{
    public static string[] Select(IReadOnlyList<string> ordered, string anchor, ArchiveScope scope)
    {
        var index = ordered.ToList().IndexOf(anchor);
        if (index < 0) return [];
        return scope switch
        {
            ArchiveScope.Left => ordered.Take(index).ToArray(),
            ArchiveScope.Right => ordered.Skip(index + 1).ToArray(),
            ArchiveScope.All => ordered.ToArray(),
            _ => []
        };
    }
}
