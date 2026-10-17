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

    private readonly Dictionary<(string? AtRule, string Name, string Text), List<CssLonghandValue>?> _values = [];

    /// <summary>The values of the declarations of the style sheets and of the style attributes, by their text without the leading and trailing whitespace.</summary>
    public static ParsedValueCache Declarations => s_declarations ??= new ParsedValueCache();

    /// <summary>The values of the properties after the substitution of their arbitrary substitution functions (e.g. var()), by the substituted text.</summary>
    public static ParsedValueCache SubstitutedValues => s_substitutedValues ??= new ParsedValueCache();

    /// <summary>The values of the shorthands serialized from their longhands, parsed back to check that they round-trip, by text.</summary>
    public static ParsedValueCache SerializedShorthands => s_serializedShorthands ??= new ParsedValueCache();

    public bool TryGetValue(string? atRule, string name, string text, out List<CssLonghandValue>? longhands) => _values.TryGetValue((atRule, name, text), out longhands);

    public void Add(string? atRule, string name, string text, List<CssLonghandValue>? longhands)
    {
        if (_values.Count >= MaxCount)
        {
            _values.Clear();
        }

        _values[(atRule, name, text)] = longhands;
    }
}
