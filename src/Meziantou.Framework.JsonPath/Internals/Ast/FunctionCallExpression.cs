using System.Text.RegularExpressions;

namespace Meziantou.Framework.Json.Internals;

internal sealed class FunctionCallExpression : LogicalExpression
{
    /// <summary>
    /// The last pattern a <c>match()</c>/<c>search()</c> call compiled, with its result. A literal pattern never
    /// changes, and a pattern read from the document is usually one value shared by every node the filter
    /// visits (<c>$.pattern</c>), so a single entry avoids recompiling per node in both cases. An invalid pattern
    /// is cached as <see cref="RegexCacheEntry.Unusable"/> so it is not re-translated per node either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pattern read from the document may be attacker-controlled, which is why this is one entry rather than
    /// a growing map. Nodes whose patterns alternate replace the entry each time, which costs what an uncached
    /// compilation costs.
    /// </para>
    /// <para>
    /// A parsed <see cref="JsonPath"/> is documented as thread-safe and reusable. Publishing this field races
    /// benignly: <see cref="RegexCacheSlot"/> is immutable, so a reader never pairs one pattern with another's
    /// regex, reference assignment is atomic, and the worst case is that concurrent evaluations using different
    /// patterns keep replacing each other's entry.
    /// </para>
    /// </remarks>
    private RegexCacheSlot? _regexCache;

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
        var cached = _regexCache;
        if (cached is not null && string.Equals(cached.Pattern, pattern, StringComparison.Ordinal))
        {
            return cached.Entry;
        }

        var entry = factory(pattern, anchored);
        _regexCache = new RegexCacheSlot(pattern, entry);
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
