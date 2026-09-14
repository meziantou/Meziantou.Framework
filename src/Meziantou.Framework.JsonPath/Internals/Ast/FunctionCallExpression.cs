using System.Text.RegularExpressions;

namespace Meziantou.Framework.Json.Internals;

internal sealed class FunctionCallExpression : LogicalExpression
{
    /// <summary>
    /// The number of compiled patterns a single <c>match()</c>/<c>search()</c> call keeps. A pattern read from the
    /// document may be attacker-controlled, which is why the cache is bounded rather than a growing map.
    /// </summary>
    internal const int RegexCacheCapacity = 8;

    /// <summary>
    /// The patterns this <c>match()</c>/<c>search()</c> call compiled most recently, newest first, with their results.
    /// A literal pattern never changes, and a pattern read from the document is usually one of a few values shared by
    /// the nodes the filter visits (<c>$.pattern</c>, or <c>@.pattern</c> alternating between a handful of values),
    /// so a few entries avoid recompiling per node. Compiling costs far more than matching, often tens of
    /// milliseconds. An invalid pattern is cached as <see cref="RegexCacheEntry.Unusable"/> so it is not
    /// re-translated per node either.
    /// </summary>
    /// <remarks>
    /// A parsed <see cref="JsonPath"/> is documented as thread-safe and reusable. Publishing this field races
    /// benignly: the array is never mutated once published and <see cref="RegexCacheSlot"/> is immutable, so a
    /// reader never pairs one pattern with another's regex, reference assignment is atomic, and the worst case is
    /// that concurrent evaluations drop an entry another one just added.
    /// </remarks>
    private RegexCacheSlot[] _regexCache = [];

    public FunctionCallExpression(string name, FunctionArgument[] arguments, FunctionExpressionType resultType)
    {
        Name = name;
        Arguments = arguments;
        ResultType = resultType;
    }

    public override LogicalExpressionKind Kind => LogicalExpressionKind.FunctionCall;

    public string Name { get; }

    public FunctionArgument[] Arguments { get; }

    public FunctionExpressionType ResultType { get; }

    /// <summary>Gets the compiled regex for <paramref name="pattern"/>, building it with <paramref name="factory"/> unless it is the cached one.</summary>
    /// <param name="pattern">The I-Regexp pattern, which is the cache key.</param>
    /// <param name="anchored">
    /// Passed to <paramref name="factory"/>. It is not part of the key: it depends only on whether this call is
    /// <c>match()</c> or <c>search()</c>, so it never varies for a given call.
    /// </param>
    /// <param name="factory">Builds the entry from the pattern.</param>
    /// <returns>The entry for <paramref name="pattern"/>.</returns>
    public RegexCacheEntry GetOrCreateRegex(string pattern, bool anchored, Func<string, bool, RegexCacheEntry> factory)
    {
        var slots = _regexCache;
        foreach (var slot in slots)
        {
            if (string.Equals(slot.Pattern, pattern, StringComparison.Ordinal))
            {
                return slot.Entry;
            }
        }

        // Evict the oldest entry once full. A hit does not move its entry forward, so reads never allocate.
        var entry = factory(pattern, anchored);
        var updated = new RegexCacheSlot[Math.Min(slots.Length + 1, RegexCacheCapacity)];
        updated[0] = new RegexCacheSlot(pattern, entry);
        Array.Copy(slots, 0, updated, 1, updated.Length - 1);
        _regexCache = updated;
        return entry;
    }

    /// <summary>An immutable compiled-pattern result: either a usable <see cref="Regex"/> or a known failure.</summary>
    public sealed class RegexCacheEntry
    {
        /// <summary>A pattern that cannot be evaluated, which RFC 9535 maps to LogicalFalse.</summary>
        public static readonly RegexCacheEntry Unusable = new(regex: null);

        public RegexCacheEntry(Regex? regex) => Regex = regex;

        public Regex? Regex { get; }
    }

    /// <summary>A pattern paired with its entry, published as one reference so the two can never be torn apart.</summary>
    private sealed class RegexCacheSlot(string pattern, RegexCacheEntry entry)
    {
        public string Pattern { get; } = pattern;

        public RegexCacheEntry Entry { get; } = entry;
    }
}
