namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// The parsed values of the declarations, by at-rule, property name and text (null when the value is invalid): the parsed
/// value only depends on its text, and the same values are repeated in the style sheets, the style attributes and the
/// substituted values of the elements, and the serialized shorthands (e.g. display: none, margin: 0). The lists are shared:
/// they are never modified.
/// </summary>
internal sealed class ParsedValueCache
{
    /// <summary>The longer values are rarely repeated (e.g. the data URLs).</summary>
    public const int MaxTextLength = 256;

    private const int MaxCount = 4096;

    [ThreadStatic]
    private static ParsedValueCache? s_declarations;

    [ThreadStatic]
    private static ParsedValueCache? s_substitutedValues;

    [ThreadStatic]
    private static ParsedValueCache? s_serializedShorthands;

    private readonly Dictionary<Key, List<CssLonghandValue>?> _values;

    /// <summary>Looks up the values by a span of text, so that a hit does not allocate the text.</summary>
    private readonly Dictionary<Key, List<CssLonghandValue>?>.AlternateLookup<SpanKey> _lookup;

    private ParsedValueCache()
    {
        _values = new(KeyComparer.Instance);
        _lookup = _values.GetAlternateLookup<SpanKey>();
    }

    /// <summary>The values of the declarations of the style sheets and of the style attributes, by their text without the leading and trailing whitespace.</summary>
    public static ParsedValueCache Declarations => s_declarations ??= new ParsedValueCache();

    /// <summary>The values of the properties after the substitution of their arbitrary substitution functions (e.g. var()), by the substituted text.</summary>
    public static ParsedValueCache SubstitutedValues => s_substitutedValues ??= new ParsedValueCache();

    /// <summary>The values of the shorthands serialized from their longhands, parsed back to check that they round-trip, by text.</summary>
    public static ParsedValueCache SerializedShorthands => s_serializedShorthands ??= new ParsedValueCache();

    public bool TryGetValue(string? atRule, string name, ReadOnlySpan<char> text, out List<CssLonghandValue>? longhands) => _lookup.TryGetValue(new SpanKey(atRule, name, text), out longhands);

    public void Add(string? atRule, string name, string text, List<CssLonghandValue>? longhands)
    {
        if (_values.Count >= MaxCount)
        {
            _values.Clear();
        }

        _values[new Key(atRule, name, text)] = longhands;
    }

    private readonly record struct Key(string? AtRule, string Name, string Text);

    private readonly ref struct SpanKey(string? atRule, string name, ReadOnlySpan<char> text)
    {
        public string? AtRule { get; } = atRule;

        public string Name { get; } = name;

        public ReadOnlySpan<char> Text { get; } = text;
    }

    private sealed class KeyComparer : IEqualityComparer<Key>, IAlternateEqualityComparer<SpanKey, Key>
    {
        public static KeyComparer Instance { get; } = new();

        public bool Equals(Key x, Key y) => x.AtRule == y.AtRule && x.Name == y.Name && x.Text == y.Text;

        public int GetHashCode(Key obj) => GetHashCode(obj.AtRule, obj.Name, obj.Text);

        public bool Equals(SpanKey alternate, Key other) => alternate.AtRule == other.AtRule && alternate.Name == other.Name && alternate.Text.SequenceEqual(other.Text);

        public int GetHashCode(SpanKey alternate) => GetHashCode(alternate.AtRule, alternate.Name, alternate.Text);

        public Key Create(SpanKey alternate) => new(alternate.AtRule, alternate.Name, alternate.Text.ToString());

        private static int GetHashCode(string? atRule, string name, ReadOnlySpan<char> text) => HashCode.Combine(atRule is null ? 0 : string.GetHashCode(atRule, StringComparison.Ordinal), string.GetHashCode(name, StringComparison.Ordinal), string.GetHashCode(text, StringComparison.Ordinal));
    }
}
