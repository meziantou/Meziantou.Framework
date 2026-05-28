namespace Meziantou.Framework.SyntaxHighlighting.Engine;

internal sealed class Keywords
{
    public IReadOnlyDictionary<string, string[]> Groups { get; }

    private Keywords(Dictionary<string, string[]> groups) => Groups = groups;

    public static Keywords FromWords(IList<string> words) => new Keywords(new(StringComparer.Ordinal)
    {
        ["keyword"] = [.. words],
    });

    public static Keywords FromMap(IReadOnlyDictionary<string, string[]> map)
    {
        var groups = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (scope, list) in map)
            groups[scope] = list;
        return new Keywords(groups);
    }

    public static Keywords FromMap(IReadOnlyDictionary<string, string> map)
    {
        var groups = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (scope, list) in map)
            groups[scope] = list.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new Keywords(groups);
    }
}
