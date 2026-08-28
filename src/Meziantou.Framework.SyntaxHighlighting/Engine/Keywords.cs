namespace Meziantou.Framework.SyntaxHighlighting.Engine;

/// <summary>
/// The keyword groups of a mode, captured in declaration order.
/// </summary>
/// <remarks>
/// When the same word appears in more than one group the last group wins. Grammars rely on
/// this: they list a word in the generic <c>keyword</c> group and then again in a more
/// specific one (<c>type</c>, <c>literal</c>, <c>built_in</c>) to override it. Order is
/// therefore part of the contract, which is why the groups are kept as an ordered list rather
/// than re-enumerated from a dictionary at compile time.
/// </remarks>
internal sealed class Keywords
{
    public IReadOnlyList<KeyValuePair<string, string[]>> Groups { get; }

    private Keywords(IReadOnlyList<KeyValuePair<string, string[]>> groups) => Groups = groups;

    public static Keywords FromWords(IList<string> words) => new([new KeyValuePair<string, string[]>("keyword", [.. words])]);

    public static Keywords FromMap(IReadOnlyDictionary<string, string[]> map) => new([.. map]);

    public static Keywords FromMap(IReadOnlyDictionary<string, string> map) =>
        new([.. map.Select(entry => new KeyValuePair<string, string[]>(entry.Key, entry.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)))]);
}
