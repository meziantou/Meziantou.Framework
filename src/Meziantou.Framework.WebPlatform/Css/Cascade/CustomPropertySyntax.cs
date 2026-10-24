using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>A component of a syntax definition: a data type name (e.g. "length") or an identifier, with its multiplier.</summary>
/// <param name="Multiplier">'+' (space-separated list), '#' (comma-separated list), or '\0' for a single value.</param>
internal sealed record CustomPropertySyntaxComponent(string Name, bool IsDataType, char Multiplier)
{
    /// <summary>The grammar of the values of a data type component (null for an identifier).</summary>
    public GrammarNode? Grammar { get; } = IsDataType ? ValueGrammarParser.Parse(GetGrammar(Name) + (Multiplier == '\0' ? "" : Multiplier.ToString())) : null;

    // "The allowed range of <resolution> values always excludes negative values" (https://drafts.csswg.org/css-values-4/#resolution-value)
    private static string GetGrammar(string name) => name == "resolution" ? "<resolution [0,∞]>" : "<" + name + ">";
}

/// <summary>
/// The syntax of the registered custom properties (https://drafts.css-houdini.org/css-properties-values-api/#syntax-strings)
/// and the computation of their values (https://drafts.css-houdini.org/css-properties-values-api/#calculation-of-computed-values).
/// </summary>
internal sealed class CustomPropertySyntax
{
    private static readonly ConcurrentDictionary<string, CustomPropertySyntax?> Definitions = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, GrammarNode?> Grammars = new(StringComparer.Ordinal);

    /// <summary>https://drafts.css-houdini.org/css-properties-values-api/#supported-names</summary>
    private static readonly FrozenSet<string> SupportedNames = FrozenSet.Create(StringComparer.Ordinal,
        "length", "number", "percentage", "length-percentage", "string", "color", "image", "url", "integer", "angle", "time", "resolution", "transform-function", "custom-ident", "transform-list");

    /// <summary>The font-relative length units, whose values are not computationally independent.</summary>
    private static readonly FrozenSet<string> FontRelativeUnits = FrozenSet.Create(StringComparer.Ordinal,
        "em", "rem", "ex", "rex", "cap", "rcap", "ch", "rch", "ic", "ric", "lh", "rlh");

    private CustomPropertySyntax(List<CustomPropertySyntaxComponent> components)
    {
        Components = components;
    }

    /// <summary>https://drafts.css-houdini.org/css-properties-values-api/#universal-syntax-definition</summary>
    public static CustomPropertySyntax Universal { get; } = new([]);

    public bool IsUniversal => Components.Count == 0;

    /// <summary>The components of the syntax, in order (empty for the universal syntax definition).</summary>
    public IReadOnlyList<CustomPropertySyntaxComponent> Components { get; }

    /// <summary>Consumes a syntax definition (https://drafts.css-houdini.org/css-properties-values-api/#consume-a-syntax-definition); returns null on failure.</summary>
    public static CustomPropertySyntax? Parse(string syntax) => Definitions.GetOrAdd(syntax, static syntax => ParseCore(syntax));

    private static CustomPropertySyntax? ParseCore(string syntax)
    {
        var codePoints = new List<int>();
        foreach (var rune in CssTokenizer.Preprocess(syntax).Trim(['\n', '\t', ' ']).EnumerateRunes())
        {
            codePoints.Add(rune.Value);
        }

        if (codePoints.Count == 0)
            return null;

        if (codePoints is ['*'])
            return Universal;

        var components = new List<CustomPropertySyntaxComponent>();
        var position = 0;
        int Peek(int offset = 0) => position + offset < codePoints.Count ? codePoints[position + offset] : -1;
        void SkipWhitespace()
        {
            while (Peek() is '\n' or '\t' or ' ')
            {
                position++;
            }
        }

        while (true)
        {
            SkipWhitespace();
            if (Peek() == -1)
                return components.Count == 0 ? null : new CustomPropertySyntax(components);

            if (ConsumeComponent(codePoints, ref position) is not { } component)
                return null;

            components.Add(component);
            SkipWhitespace();
            var next = Peek();
            if (next == -1)
                return new CustomPropertySyntax(components);

            if (next != '|')
                return null;

            position++;
        }
    }

    /// <summary>https://drafts.css-houdini.org/css-properties-values-api/#consume-a-syntax-component</summary>
    private static CustomPropertySyntaxComponent? ConsumeComponent(List<int> codePoints, ref int position)
    {
        string name;
        bool isDataType;
        if (codePoints[position] == '<')
        {
            // Consume a data type name: the name must be exactly a supported name (no whitespace, no escapes)
            var end = codePoints.IndexOf('>', position);
            if (end < 0)
                return null;

            var builder = new StringBuilder();
            for (var i = position + 1; i < end; i++)
            {
                builder.Append(char.ConvertFromUtf32(codePoints[i]));
            }

            name = builder.ToString();
            if (!SupportedNames.Contains(name))
                return null;

            position = end + 1;
            isDataType = true;
        }
        else
        {
            if (!WouldStartIdentSequence(codePoints, position))
                return null;

            name = ConsumeIdentSequence(codePoints, ref position);
            if (ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(name)) || InfraAscii.EqualsIgnoreAsciiCase(name, "default"))
                return null;

            isDataType = false;
        }

        // The pre-multiplied data type names cannot have a multiplier
        var multiplier = '\0';
        if (name != "transform-list" && position < codePoints.Count && codePoints[position] is '+' or '#')
        {
            multiplier = (char)codePoints[position];
            position++;
        }

        return new CustomPropertySyntaxComponent(name, isDataType, multiplier);
    }

    private static int At(List<int> codePoints, int index) => index < codePoints.Count ? codePoints[index] : -1;

    private static bool IsValidEscape(int first, int second) => first == '\\' && second is not ('\n' or -1);

    /// <summary>https://drafts.csswg.org/css-syntax-3/#would-start-an-identifier</summary>
    private static bool WouldStartIdentSequence(List<int> codePoints, int position)
    {
        var first = At(codePoints, position);
        var second = At(codePoints, position + 1);
        if (first == '-')
            return second == '-' || (second >= 0 && CssTokenizer.IsIdentStart(second)) || IsValidEscape(second, At(codePoints, position + 2));

        return (first >= 0 && CssTokenizer.IsIdentStart(first)) || IsValidEscape(first, second);
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-name</summary>
    private static string ConsumeIdentSequence(List<int> codePoints, ref int position)
    {
        var builder = new StringBuilder();
        while (position < codePoints.Count)
        {
            var c = codePoints[position];
            if (CssTokenizer.IsIdentCodePoint(c))
            {
                builder.Append(char.ConvertFromUtf32(c));
                position++;
            }
            else if (IsValidEscape(c, At(codePoints, position + 1)))
            {
                position++;
                builder.Append(char.ConvertFromUtf32(ConsumeEscapedCodePoint(codePoints, ref position)));
            }
            else
            {
                break;
            }
        }

        return builder.ToString();
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-escaped-code-point</summary>
    private static int ConsumeEscapedCodePoint(List<int> codePoints, ref int position)
    {
        var c = codePoints[position++];
        if (!IsHexDigit(c))
            return c;

        var value = HexValue(c);
        for (var i = 1; i < 6 && IsHexDigit(At(codePoints, position)); i++)
        {
            value = (value * 16) + HexValue(codePoints[position]);
            position++;
        }

        if (position < codePoints.Count && codePoints[position] is '\n' or '\t' or ' ')
        {
            position++;
        }

        return value == 0 || value is >= 0xD800 and <= 0xDFFF || value > 0x10FFFF ? 0xFFFD : value;
    }

    private static bool IsHexDigit(int c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    private static int HexValue(int c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    // ----------------------------------------------------------------------------------------------------------------
    // Values
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the component that a value matches (the first one, https://drafts.css-houdini.org/css-properties-values-api/#parse-a-value-according-to-a-syntax),
    /// or null when the value does not match the syntax. A value matches the universal syntax definition when it is a
    /// valid &lt;declaration-value&gt;?.
    /// </summary>
    public bool TryMatch(List<CssComponentValue> values, out CustomPropertySyntaxComponent? component)
    {
        component = null;
        if (!CssShorthands.IsValidDeclarationValue(values, topLevel: true))
            return false;

        if (IsUniversal)
            return true;

        var significant = ValueMatcher.RemoveWhitespace(values);
        if (significant.Count == 0)
            return false;

        foreach (var candidate in Components)
        {
            if (candidate.Grammar is { } grammar ? ValueMatcher.Matches(grammar, significant) : MatchesIdent(candidate, significant))
            {
                component = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>An identifier component matches the identifiers equal to its name (case-sensitively), with its multiplier.</summary>
    private static bool MatchesIdent(CustomPropertySyntaxComponent component, List<CssComponentValue> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            var expectsComma = component.Multiplier == '#' && i % 2 == 1;
            if (expectsComma ? values[i] is not CssPreservedToken { Type: CssTokenType.Comma } : values[i] is not CssPreservedToken { Type: CssTokenType.Ident } ident || ident.Token.Value != component.Name)
                return false;
        }

        return component.Multiplier switch
        {
            '+' => true,
            '#' => values.Count % 2 == 1,
            _ => values.Count == 1,
        };
    }

    /// <summary>
    /// Returns whether a value is a valid initial value for the syntax: it matches the syntax, it is not a CSS-wide keyword,
    /// and it is computationally independent (https://drafts.css-houdini.org/css-properties-values-api/#computationally-independent).
    /// </summary>
    public bool IsValidInitialValue(string value)
    {
        var values = CssParser.ParseListOfComponentValues(value);
        if (!TryMatch(values, out _))
            return false;

        if (ValueMatcher.RemoveWhitespace(values) is [CssPreservedToken { Type: CssTokenType.Ident } ident] && ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(ident.Token.Value)))
            return false;

        return IsComputationallyIndependent(values, checkUnits: !IsUniversal);
    }

    private static bool IsComputationallyIndependent(List<CssComponentValue> values, bool checkUnits)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Dimension } dimension when checkUnits && FontRelativeUnits.Contains(InfraAscii.ToAsciiLowercase(dimension.Token.Value)):
                    return false;
                case CssFunction function:
                    if (InfraAscii.ToAsciiLowercase(function.Name) is "var" or "attr" || !IsComputationallyIndependent(function.Value, checkUnits))
                        return false;

                    // The random base value of a random function depends on the element and the property, unless it is fixed
                    // (https://drafts.csswg.org/css-values-5/#random-caching)
                    if (InfraAscii.ToAsciiLowercase(function.Name) is "random" or "random-item" && !IsFixedRandomFunction(function))
                        return false;

                    break;
                case CssSimpleBlock block when !IsComputationallyIndependent(block.Value, checkUnits):
                    return false;
            }
        }

        return true;

        static bool IsFixedRandomFunction(CssFunction function) => ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken first, ..] && first.Token.IsIdent("fixed");
    }

    /// <summary>Returns the computed value of a value of a registered custom property, or null when the value does not match the syntax.</summary>
    public string? Compute(ComputedStyle style, string value, string? baseUrl)
    {
        if (IsUniversal)
            return value;

        var values = CssParser.ParseListOfComponentValues(value);
        if (!TryMatch(values, out var component))
            return null;

        var significant = ValueMatcher.RemoveWhitespace(values);
        // The identifiers and the strings compute to themselves
        if (!component!.IsDataType || component.Name is "custom-ident" or "string")
            return JoinItems(significant, component, item => CssSerializer.SerializeValue([item], property: null));

        if (component.Name == "transform-list")
            return RestoreTransformFunctionNames(CssValueComputer.ComputeGeneric(style, property: null, value, baseUrl, hasColors: false));

        return JoinItems(significant, component, item => ComputeItem(style, component.Name, item, baseUrl));
    }

    /// <summary>Computes the items of a list (or the single value) and joins them with the separator of the multiplier.</summary>
    private static string JoinItems(List<CssComponentValue> values, CustomPropertySyntaxComponent component, Func<CssComponentValue, string> compute)
    {
        var items = values.Where(v => v is not CssPreservedToken { Type: CssTokenType.Comma }).Select(compute);
        return string.Join(component.Multiplier == '#' ? ", " : " ", items);
    }

    private static string ComputeItem(ComputedStyle style, string type, CssComponentValue value, string? baseUrl)
    {
        var text = CssSerializer.SerializeValue([value], property: null);

        // Like the browsers, currentcolor is resolved in the computed value of a <color> (e.g. color-mix(in srgb, currentcolor, red) is a color)
        if (type == "color" && text.Contains("currentcolor", StringComparison.Ordinal))
        {
            text = CssValueComputer.ReplaceCurrentColor(text, style.CurrentColor);
        }

        var computed = CssValueComputer.ComputeGeneric(style, property: null, text, baseUrl, hasColors: type == "color");
        switch (type)
        {
            // The math functions of an <integer> are rounded to the nearest integer (https://drafts.csswg.org/css-values-4/#calc-type-checking)
            case "integer" when CssValueComputer.TryParseNumber(computed, out var number):
                return CssValueComputer.SerializeNumber(Math.Floor(number + 0.5));
            case "length" or "length-percentage" when computed == "0":
                return "0px";
            case "transform-function":
                return RestoreTransformFunctionNames(computed);
            default:
                return computed;
        }
    }

    /// <summary>The names of the transform functions in camel case (e.g. translateX), by lowercase name.</summary>
    private static readonly Lazy<FrozenDictionary<string, string>> TransformFunctionNames = new(() =>
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (CssPropertyRegistry.GetTypeGrammar("<transform-function>") is GroupNode group)
        {
            foreach (var child in group.Children)
            {
                if (child is TypeNode { Name: var name } && name.EndsWith("()", StringComparison.Ordinal) && InfraAscii.ToAsciiLowercase(name) is var lower && lower != name)
                {
                    names[lower[..^1]] = name[..^1];
                }
            }
        }

        return names.ToFrozenDictionary(StringComparer.Ordinal);
    });

    /// <summary>The serialization lowercases the names of the functions: the transform functions are serialized in camel case, like the browsers.</summary>
    private static string RestoreTransformFunctionNames(string value)
    {
        foreach (var (lower, name) in TransformFunctionNames.Value)
        {
            value = value.Replace(lower, name, StringComparison.Ordinal);
        }

        return value;
    }

    /// <summary>
    /// Parses a syntax definition written as component values (https://drafts.css-houdini.org/css-properties-values-api/#parsing-syntax):
    /// the universal syntax (*) or syntax components separated by |, each one a data type name (&lt;length&gt;) or an
    /// identifier, followed by an optional multiplier (+ or #). Returns null when the syntax is invalid.
    /// </summary>
    public static CssSyntaxDefinition? ParseSyntax(List<CssComponentValue> values)
    {
        var significant = ValueMatcher.RemoveWhitespace(values);
        if (significant is [CssPreservedToken universal] && universal.Token.IsDelim('*'))
            return CssSyntaxDefinition.Universal;

        var components = new List<string>();
        var index = SkipWhitespace(values, 0);
        while (true)
        {
            if (index >= values.Count || values[index] is not CssPreservedToken { Token: var token })
                return null;

            string component;
            var isTransformList = false;
            if (token.IsDelim('<'))
            {
                // <data-type-name>, without whitespace
                if (index + 2 >= values.Count || values[index + 1] is not CssPreservedToken { Type: CssTokenType.Ident } name || values[index + 2] is not CssPreservedToken close || !close.Token.IsDelim('>'))
                    return null;

                if (!SupportedNames.Contains(name.Token.Value))
                    return null;

                component = "<" + name.Token.Value + ">";
                isTransformList = name.Token.Value == "transform-list";
                index += 3;
            }
            else if (token.Type == CssTokenType.Ident)
            {
                // A <custom-ident>
                var lower = InfraAscii.ToAsciiLowercase(token.Value);
                if (ValueMatcher.CssWideKeywords.Contains(lower) || lower == "default")
                    return null;

                component = CssSerializer.SerializeIdentifier(token.Value);
                index++;
            }
            else
            {
                return null;
            }

            // The multiplier follows the component without whitespace; <transform-list> is already a list
            if (index < values.Count && values[index] is CssPreservedToken multiplier && (multiplier.Token.IsDelim('+') || multiplier.Token.IsDelim('#')))
            {
                if (isTransformList)
                    return null;

                component += multiplier.Token.Value;
                index++;
            }

            components.Add(component);
            index = SkipWhitespace(values, index);
            if (index >= values.Count)
                break;

            if (values[index] is not CssPreservedToken bar || !bar.Token.IsDelim('|'))
                return null;

            index = SkipWhitespace(values, index + 1);
        }

        return new CssSyntaxDefinition(string.Join(" | ", components), components.Count);
    }

    private static int SkipWhitespace(List<CssComponentValue> values, int index)
    {
        while (index < values.Count && values[index] is CssPreservedToken { Type: CssTokenType.Whitespace })
        {
            index++;
        }

        return index;
    }

    /// <summary>Returns whether a value (without arbitrary substitution functions) matches a syntax definition.</summary>
    public static bool Matches(CssSyntaxDefinition syntax, List<CssComponentValue> values)
    {
        if (syntax.IsUniversal)
            return true;

        return ValueMatcher.RemoveWhitespace(values).Count > 0 && (Parse(syntax.Text) is not { } parsed || parsed.TryMatch(values, out _));
    }

    /// <summary>
    /// Returns the computed value of a value matching a grammar (e.g. "&lt;length&gt;" for the size features of the container
    /// queries), or null when the value does not match it.
    /// </summary>
    public static string? ComputeGrammar(ComputedStyle style, string grammarText, string value, string? baseUrl, bool fontSizeContext = false)
    {
        var grammar = Grammars.GetOrAdd(grammarText, static syntax =>
        {
            try
            {
                return ValueGrammarParser.Parse(syntax);
            }
            catch (FormatException)
            {
                return null;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        });

        if (grammar is null)
            return value;

        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        if (values.Count == 0 || !ValueMatcher.Matches(grammar, values))
            return null;

        var hasColors = CssValueComputer.ContainsType(grammar, "color", new HashSet<string>(StringComparer.Ordinal));
        return CssValueComputer.ComputeGeneric(style, property: null, value, baseUrl, hasColors, fontSizeContext);
    }
}

/// <summary>
/// A syntax definition (https://drafts.css-houdini.org/css-properties-values-api/#syntax-definition): <see cref="Text"/> is
/// its syntax string, with the components separated by " | " ("*" for the universal syntax definition).
/// </summary>
internal sealed class CssSyntaxDefinition(string text, int componentCount)
{
    public static CssSyntaxDefinition Universal { get; } = new("*", 0);

    public string Text { get; } = text;

    /// <summary>The number of syntax components (0 for the universal syntax definition).</summary>
    public int ComponentCount { get; } = componentCount;

    public bool IsUniversal => ComponentCount == 0;
}
