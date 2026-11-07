using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// Resolves the counter style names of a document to counter styles (https://drafts.csswg.org/css-counter-styles-3/#counter-styles)
/// and generates the representations of the counter values. A name is a tree-scoped reference: the @counter-style rules of the
/// tree of the style sheet that uses the name, then the ones of the trees of the shadow hosts, then the predefined counter
/// styles; an unknown name is the decimal style.
/// </summary>
/// <remarks>
/// The counter styles are created from the rules when they are first used, and kept for the life of the resolver, which is
/// created for each box tree: the rules may change between two layouts.
/// </remarks>
internal sealed class CounterStyleResolver
{
    private readonly StyleEngine _engine;
    private readonly Dictionary<(Node Scope, string Name), CounterStyle> _styles = [];

    /// <summary>The rules whose counter style is being created: a rule that extends one of them is in a cycle.</summary>
    private readonly List<(Node Scope, string Name)> _resolving = [];
    private HashSet<(Node Scope, string Name)>? _cycles;

    public CounterStyleResolver(StyleEngine engine)
    {
        _engine = engine;
    }

    /// <summary>Returns the counter style with the name, which is looked up from a tree (the document when null); the decimal style when there is none.</summary>
    public CounterStyle Resolve(string name, Node? treeScope) => Resolve(new CounterStyleReference(CounterStyle.NormalizeName(name), treeScope, Predefined: false));

    public CounterStyle Resolve(CounterStyleReference reference)
    {
        if (!reference.Predefined && _engine.FindCounterStyle(reference.Name, reference.Scope) is var (rule, scope))
        {
            var key = (scope, reference.Name);
            if (_styles.TryGetValue(key, out var style))
                return style;

            // "If one or more @counter-style rules form a cycle with their extends values, all of the counter styles
            // participating in the cycle must be treated as if they were extending the decimal counter style instead"
            var index = _resolving.IndexOf(key);
            if (index >= 0)
            {
                _cycles ??= [];
                for (var i = index; i < _resolving.Count; i++)
                {
                    _cycles.Add(_resolving[i]);
                }

                return CounterStyle.Decimal;
            }

            _resolving.Add(key);
            style = CounterStyle.Create(rule, scope, predefined: false, Resolve);
            _resolving.RemoveAt(_resolving.Count - 1);
            if (_cycles is not null && _cycles.Contains(key))
            {
                style = CounterStyle.Create(rule, scope, predefined: false, static _ => CounterStyle.Decimal);
            }

            // The rules that do not define a counter style are not collected
            style ??= CounterStyle.Decimal;
            _styles[key] = style;
            return style;
        }

        return CounterStyle.Predefined.GetValueOrDefault(reference.Name) ?? CounterStyle.Decimal;
    }

    /// <summary>
    /// Returns the counter style of a &lt;counter-style&gt; value (https://drafts.csswg.org/css-counter-styles-3/#typedef-counter-style):
    /// a name, or a symbols() function. Returns null when the value is not a counter style (none, a string).
    /// </summary>
    public CounterStyle? Resolve(List<CssComponentValue> value, Node? treeScope)
    {
        return ValueMatcher.TrimWhitespace(value) switch
        {
            [CssPreservedToken { Type: CssTokenType.Ident } name] => Resolve(name.Token.Value, treeScope),
            [CssFunction function] => CounterStyle.Create(function),
            _ => null,
        };
    }

    /// <summary>
    /// Generates the representation of a counter value in a counter style (https://drafts.csswg.org/css-counter-styles-3/#generate-a-counter),
    /// without the prefix and the suffix: when the style cannot represent the value (out of its range, too long...), its
    /// fallback style is used, and the decimal style when the fallback styles loop.
    /// </summary>
    public string Generate(CounterStyle style, long value, WritingMode writingMode, Direction direction)
    {
        List<CounterStyle>? visited = null;
        while (true)
        {
            if (style.Generate(value, writingMode, direction) is { } representation)
                return representation;

            visited ??= [];
            visited.Add(style);
            style = Resolve(style.Fallback);
            if (visited.Contains(style))
                return CounterStyle.Decimal.Generate(value, writingMode, direction)!;
        }
    }

    /// <summary>The marker string of a counter value (https://drafts.csswg.org/css-lists-3/#marker-string): the prefix of the style, the representation of the value, and the suffix of the style.</summary>
    public string GenerateMarker(CounterStyle style, long value, WritingMode writingMode, Direction direction) => style.Prefix + Generate(style, value, writingMode, direction) + style.Suffix;
}
