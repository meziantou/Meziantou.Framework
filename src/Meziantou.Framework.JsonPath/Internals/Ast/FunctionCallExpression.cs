using System.Text.RegularExpressions;

namespace Meziantou.Framework.Json.Internals;

internal sealed class FunctionCallExpression : LogicalExpression
{
    /// <summary>
    /// Cached <see cref="Regex"/> for a <c>match()</c>/<c>search()</c> call whose pattern is a literal, which is
    /// the overwhelmingly common case. A miss stores <see cref="RegexCacheEntry.Unusable"/> so an invalid pattern
    /// is not re-translated per node either.
    /// </summary>
    /// <remarks>
    /// A parsed <see cref="JsonPath"/> is documented as thread-safe and reusable. Publishing this field races
    /// benignly: <see cref="RegexCacheEntry"/> is immutable, reference assignment is atomic, and the worst case
    /// is that two threads each build an equivalent entry and one wins.
    /// </remarks>
    private RegexCacheEntry? _regexCache;

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

    /// <summary>Gets the cached regex for this call, building it with <paramref name="factory"/> on first use.</summary>
    /// <param name="factory">Builds the entry from the literal pattern.</param>
    /// <returns>The cached entry.</returns>
    public RegexCacheEntry GetOrCreateRegex(Func<string, RegexCacheEntry> factory)
    {
        var cached = _regexCache;
        if (cached is not null)
        {
            return cached;
        }

        var pattern = (string)Arguments[1].Value!;
        cached = factory(pattern);
        _regexCache = cached;
        return cached;
    }

    /// <summary>An immutable compiled-pattern result: either a usable <see cref="Regex"/> or a known failure.</summary>
    public sealed class RegexCacheEntry
    {
        /// <summary>A pattern that cannot be evaluated, which RFC 9535 maps to LogicalFalse.</summary>
        public static readonly RegexCacheEntry Unusable = new(regex: null);

        public RegexCacheEntry(Regex? regex) => Regex = regex;

        public Regex? Regex { get; }
    }
}
