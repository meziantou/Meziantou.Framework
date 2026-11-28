using System.Collections.Concurrent;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>The serialization of CSS values (https://drafts.csswg.org/cssom/#common-serializing-idioms).</summary>
internal static partial class CssSerializer
{
    private static readonly ConcurrentDictionary<string, HashSet<string>> Keywords = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, bool> ZeroIsLength = new(StringComparer.Ordinal);

    /// <summary>Returns whether an identifier is serialized as is: it has no character to escape (the common case).</summary>
    private static bool IsSerializedAsIs(string value, bool keepsNonAsciiCharacters)
    {
        if (value.Length == 0 || char.IsAsciiDigit(value[0]) || (value[0] == '-' && (value.Length == 1 || char.IsAsciiDigit(value[1]))))
            return false;

        foreach (var c in value)
        {
            if (c < '\u0080' ? !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_') : !keepsNonAsciiCharacters && !IsNonAsciiIdentifierCharacter(c))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns whether a character of at least U+0080 is read back as a part of an identifier: the non-ASCII ident code
    /// points of CSS Syntax exclude some characters, such as U+00BA, which an escape gives (fuzz regression: the name of
    /// <c>@container sid\ba r</c> was written unescaped, and read back as three tokens). The surrogates of a pair are a code
    /// point of at least U+10000, which is one.
    /// </summary>
    private static bool IsNonAsciiIdentifierCharacter(char c) => char.IsSurrogate(c) || CssTokenizer.IsIdentCodePoint(c);

    /// <summary>
    /// https://drafts.csswg.org/cssom/#serialize-an-identifier, which keeps the characters of at least U+0080. Except for
    /// <c>CSS.escape()</c> (<see cref="EscapeIdentifier"/>), the ones that are not ident code points of CSS
    /// Syntax are escaped, so that the text is read back as the same identifier.
    /// </summary>
    public static string SerializeIdentifier(string value) => SerializeIdentifier(value, keepsNonAsciiCharacters: false);

    /// <summary>The identifier as <c>CSS.escape()</c> writes it: https://drafts.csswg.org/cssom/#serialize-an-identifier, which keeps the characters of at least U+0080.</summary>
    public static string EscapeIdentifier(string value) => SerializeIdentifier(value, keepsNonAsciiCharacters: true);

    private static string SerializeIdentifier(string value, bool keepsNonAsciiCharacters)
    {
        if (IsSerializedAsIs(value, keepsNonAsciiCharacters))
            return value;

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\0')
            {
                builder.Append('�');
            }
            else if (c is (>= '\u0001' and <= '\u001F') or '\u007F' || (i == 0 && char.IsAsciiDigit(c)) || (i == 1 && char.IsAsciiDigit(c) && value[0] == '-') || (c >= '\u0080' && !keepsNonAsciiCharacters && !IsNonAsciiIdentifierCharacter(c)))
            {
                builder.Append('\\').Append(((int)c).ToString("x", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
            }
            else if (i == 0 && c == '-' && value.Length == 1)
            {
                builder.Append("\\-");
            }
            else if (c >= '\u0080' || c is '-' or '_' || char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('\\').Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>https://drafts.csswg.org/cssom/#serialize-a-string</summary>
    public static string SerializeString(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            if (c == '\0')
            {
                builder.Append('�');
            }
            else if (c is (>= '\u0001' and <= '\u001F') or '\u007F')
            {
                builder.Append('\\').Append(((int)c).ToString("x", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
            }
            else if (c is '"' or '\\')
            {
                builder.Append('\\').Append(c);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.Append('"').ToString();
    }

    /// <summary>https://drafts.csswg.org/cssom/#serialize-a-url</summary>
    public static string SerializeUrl(string value) => "url(" + SerializeString(value) + ")";

    /// <summary>The keywords of the grammar of a property (they are serialized in lowercase).</summary>
    private static HashSet<string> GetKeywords(CssPropertyDefinition property)
    {
        return Keywords.GetOrAdd(property.Name, static (_, property) =>
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            if (CssPropertyRegistry.GetGrammar(property) is { } grammar)
            {
                CollectKeywords(grammar, result, visited);
            }

            foreach (var longhand in CssPropertyRegistry.GetLonghandsRecursive(property))
            {
                if (CssPropertyRegistry.Get(longhand) is { } longhandProperty && CssPropertyRegistry.GetGrammar(longhandProperty) is { } longhandGrammar)
                {
                    CollectKeywords(longhandGrammar, result, visited);
                }
            }

            return result;
        }, property);
    }

    private static void CollectKeywords(GrammarNode node, HashSet<string> keywords, HashSet<string> visited)
    {
        switch (node)
        {
            case KeywordNode keyword:
                keywords.Add(InfraAscii.ToAsciiLowercase(keyword.Keyword));
                break;
            case GroupNode group:
                foreach (var child in group.Children)
                {
                    CollectKeywords(child, keywords, visited);
                }

                break;
            case FunctionNode function when function.Arguments is not null:
                CollectKeywords(function.Arguments, keywords, visited);
                break;
            case TypeNode type:
                if (!visited.Add((type.IsProperty ? "'" : "") + type.Name))
                    return;

                GrammarNode? grammar = null;
                if (type.IsProperty)
                {
                    if (CssPropertyRegistry.Get(type.Name) is { } property)
                    {
                        grammar = CssPropertyRegistry.GetGrammar(property);
                    }
                }
                else
                {
                    grammar = CssPropertyRegistry.GetTypeGrammar(type.Name.EndsWith("()", StringComparison.Ordinal) ? type.Name : "<" + type.Name + ">");
                }

                if (grammar is not null)
                {
                    CollectKeywords(grammar, keywords, visited);
                }

                break;
        }
    }

    /// <summary>
    /// Returns whether a unitless zero is a length in the value of the property (it is then serialized as "0px"): the
    /// property accepts lengths but no numbers.
    /// </summary>
    private static bool IsZeroLength(CssPropertyDefinition property)
    {
        return ZeroIsLength.GetOrAdd(property.Name, static (_, property) =>
        {
            var types = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            if (CssPropertyRegistry.GetGrammar(property) is { } grammar)
            {
                CollectTypes(grammar, types, visited);
            }

            return (types.Contains("length") || types.Contains("length-percentage")) && !types.Contains("number") && !types.Contains("integer") && !types.Contains("zero");
        }, property);
    }

    private static void CollectTypes(GrammarNode node, HashSet<string> types, HashSet<string> visited)
    {
        switch (node)
        {
            case GroupNode group:
                foreach (var child in group.Children)
                {
                    CollectTypes(child, types, visited);
                }

                break;
            case FunctionNode:
                // The arguments of functions are not the value of the property (e.g. the numbers of the math functions)
                break;
            case TypeNode type:
            {
                var name = type.IsProperty ? "'" + type.Name : type.Name;
                if (!visited.Add(name))
                    return;

                types.Add(type.Name);
                var grammar = type.IsProperty
                    ? CssPropertyRegistry.Get(type.Name) is { } property ? CssPropertyRegistry.GetGrammar(property) : null
                    : CssPropertyRegistry.GetTypeGrammar(type.Name.EndsWith("()", StringComparison.Ordinal) ? type.Name : "<" + type.Name + ">");
                if (grammar is not null)
                {
                    CollectTypes(grammar, types, visited);
                }

                break;
            }
        }
    }

    // Whether the colors are serialized as specified, with the full precision of the numbers (see SerializeValueForComputation)
    [ThreadStatic]
    private static bool s_preserveColors;

    /// <summary>
    /// Serializes a specified value that contains colors whose computed value depends on the specified components (color-mix()
    /// and the relative colors): the color functions are kept as specified, with the full precision of their numbers, since the
    /// canonical serialization of the specified colors rounds the channels of the legacy colors and converts hsl() and hwb() to rgb().
    /// Returns null when the value does not contain such colors.
    /// </summary>
    public static string? SerializeValueForComputation(IReadOnlyList<CssComponentValue> values, CssPropertyDefinition? property)
    {
        if (!ContainsComputedColor(values))
            return null;

        return WithPreservedNumbers(() => SerializeValue(values, property));
    }

    /// <summary>
    /// Serializes a specified value with the full precision of its numbers (e.g. the easing functions of the animations, whose
    /// progress must not be rounded); returns null when no number would be rounded by the canonical serialization.
    /// </summary>
    public static string? SerializeValuePreservingNumbers(IReadOnlyList<CssComponentValue> values, CssPropertyDefinition? property)
    {
        if (!ContainsRoundedNumber(values))
            return null;

        return WithPreservedNumbers(() => SerializeValue(values, property));
    }

    /// <summary>Runs <paramref name="action"/> with the numbers serialized with their full precision.</summary>
    public static T WithPreservedNumbers<T>(Func<T> action)
    {
        var previous = s_preserveColors;
        s_preserveColors = true;
        try
        {
            return action();
        }
        finally
        {
            s_preserveColors = previous;
        }
    }

    /// <summary>
    /// Serializes a number of a math function: with its full precision when the value is serialized for the computation
    /// (<see cref="WithPreservedNumbers"/>), otherwise like <see cref="SerializeNumber"/>.
    /// </summary>
    internal static string SerializeNumberForComputation(double value)
        => s_preserveColors && double.IsFinite(value) ? value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : SerializeNumber(value);

    /// <summary>
    /// Returns whether a math function of the values has a dimension that is converted to the canonical unit of its type by the
    /// serialization (e.g. calc(100dpi + 20dpi * sign(1em - 2px)) is calc(1.041667dppx + ...)): the converted values are rounded.
    /// </summary>
    public static bool ContainsConvertedDimension(IReadOnlyList<CssComponentValue> values, bool inMathFunction = false)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Token: { Type: CssTokenType.Dimension } token } when inMathFunction:
                {
                    var unit = InfraAscii.ToAsciiLowercase(token.Value);
                    if (TypedOm.CssUnits.GetBaseType(unit) is not null && TypedOm.CssUnits.ToCanonical(1, unit).Unit != unit)
                        return true;

                    break;
                }

                case CssFunction function when ContainsConvertedDimension(function.Value, inMathFunction || CssCalc.IsMathFunction(function.Name)):
                    return true;
                case CssSimpleBlock block when ContainsConvertedDimension(block.Value, inMathFunction):
                    return true;
            }
        }

        return false;
    }

    /// <summary>Returns whether a number of the values is rounded by the canonical serialization (more than 6 decimals).</summary>
    public static bool ContainsRoundedNumber(IReadOnlyList<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Token.Type: CssTokenType.Number or CssTokenType.Percentage or CssTokenType.Dimension } token
                    when SerializeNumber(token.NumericValue) != token.NumericValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture):
                    return true;
                case CssFunction function when ContainsRoundedNumber(function.Value):
                    return true;
                case CssSimpleBlock block when ContainsRoundedNumber(block.Value):
                    return true;
            }
        }

        return false;
    }

    private static bool ContainsComputedColor(IReadOnlyList<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            if (value is CssFunction function && ((InfraAscii.ToAsciiLowercase(function.Name) is "color-mix" or "color-hdr" || CssRelativeColor.IsRelativeColorSyntax(function)) || ContainsComputedColor(function.Value)))
                return true;

            if (value is CssSimpleBlock block && ContainsComputedColor(block.Value))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the canonical name of a function: the translation and rotation functions are camel-cased (e.g. translateX), the
    /// other functions are lowercase, like in the browsers (e.g. scalex, see css/css-transforms/parsing/transform-valid.html).
    /// </summary>
    private static string GetCanonicalFunctionName(string lowercaseName) => lowercaseName switch
    {
        "translatex" => "translateX",
        "translatey" => "translateY",
        "translatez" => "translateZ",
        "rotatex" => "rotateX",
        "rotatey" => "rotateY",
        "rotatez" => "rotateZ",
        _ => lowercaseName,
    };

    /// <summary>
    /// The arguments of a transform function in their canonical form (https://drafts.csswg.org/css-transforms-2/#transform-functions):
    /// the percentages of the scale functions are numbers (<c>scale(250%)</c> is <c>scale(2.5)</c>), and a unitless zero angle
    /// or length has its unit (<c>rotate(0)</c> is <c>rotate(0deg)</c>, <c>translate(0)</c> is <c>translate(0px)</c>). The math
    /// functions are kept.
    /// </summary>
    private static List<CssComponentValue> CanonicalizeTransformFunctionArguments(string name, List<CssComponentValue> arguments)
    {
        var scale = name is "scale" or "scalex" or "scaley" or "scalez" or "scale3d";
        var angle = name is "rotate" or "rotatex" or "rotatey" or "rotatez" or "rotate3d" or "skew" or "skewx" or "skewy";
        var length = name is "translate" or "translatex" or "translatey" or "translatez" or "translate3d" or "perspective";
        if (!scale && !angle && !length)
            return arguments;

        // The angle of rotate3d() is its last argument: its other arguments are numbers
        var firstAngle = name == "rotate3d" ? arguments.FindLastIndex(argument => argument is CssPreservedToken { Type: CssTokenType.Comma }) : -1;
        List<CssComponentValue>? result = null;
        for (var i = 0; i < arguments.Count; i++)
        {
            CssToken? replacement = arguments[i] switch
            {
                CssPreservedToken { Token: { Type: CssTokenType.Percentage } percentage } when scale => new CssToken { Type = CssTokenType.Number, Value = "", NumericValue = percentage.NumericValue / 100 },
                CssPreservedToken { Token: { Type: CssTokenType.Number, NumericValue: 0 } } when angle && i > firstAngle => new CssToken { Type = CssTokenType.Dimension, Value = "deg", NumericValue = 0 },
                CssPreservedToken { Token: { Type: CssTokenType.Number, NumericValue: 0 } } when length => new CssToken { Type = CssTokenType.Dimension, Value = "px", NumericValue = 0 },
                _ => null,
            };

            if (replacement is { } token)
            {
                result ??= [.. arguments];
                result[i] = new CssPreservedToken(token);
            }
        }

        return result ?? arguments;
    }

    private static bool IsColorFunctionName(string name) => name is "rgb" or "rgba" or "hsl" or "hsla" or "hwb" or "lab" or "lch" or "oklab" or "oklch" or "ictcp" or "jzazbz" or "jzczhz" or "color" or "device-cmyk" or "color-mix" or "color-hdr" or "alpha" or "light-dark";

    /// <summary>Whether the value being serialized is a value of a property whose percentages stand for lengths (they are not resolved in the math functions).</summary>
    [ThreadStatic]
    private static bool s_percentagesAreLengths;

    /// <summary>Serializes a specified value (https://drafts.csswg.org/cssom/#serialize-a-css-value).</summary>
    public static string SerializeValue(IReadOnlyList<CssComponentValue> values, CssPropertyDefinition? property)
    {
        // An <opacity-value> percentage is serialized as a number (https://drafts.csswg.org/css-color-4/#typedef-opacity-opacity-value)
        if (property?.Grammar is "<opacity-value>" or "<'opacity'>" && ValueMatcher.RemoveWhitespace(values) is [CssPreservedToken { Type: CssTokenType.Percentage } percentage])
            return SerializeNumber(percentage.NumericValue / 100);

        var keywords = property is null ? null : GetKeywords(property);
        if (property?.Name == "font-family")
            return SerializeFontFamily(values, keywords);

        var builder = new StringBuilder();
        var percentagesAreLengths = s_percentagesAreLengths;
        s_percentagesAreLengths = property is not null && Cascade.CssValueComputer.GetPercentageUnit(property) == "px";
        try
        {
            SerializeList(builder, values, keywords, zeroIsLength: property is not null && IsZeroLength(property));
        }
        finally
        {
            s_percentagesAreLengths = percentagesAreLengths;
        }

        var result = builder.ToString().Trim();
        return property is null ? result : SerializeFontValue(property.Name, result) ?? result;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-fonts-4/#font-family-prop: a quoted family name is serialized as a sequence of identifiers
    /// when they round-trip, like the browsers (https://github.com/w3c/csswg-drafts/issues/5846), e.g. <c>'Lucida Grande'</c>
    /// is <c>Lucida Grande</c>, but <c>"serif"</c> and <c>"34J"</c> stay quoted.
    /// </summary>
    private static string SerializeFontFamily(IReadOnlyList<CssComponentValue> values, HashSet<string>? keywords)
    {
        var families = new List<string>();
        foreach (var entry in Cascade.CssValueComputer.SplitArguments([.. values]))
        {
            var significant = ValueMatcher.RemoveWhitespace(entry);
            if (significant is [CssPreservedToken { Type: CssTokenType.String } family])
            {
                families.Add(Om.FontFace.SerializeFamilyName(family.Value));
                continue;
            }

            // A family name of several identifiers
            if (significant.Count > 1 && significant.TrueForAll(component => component is CssPreservedToken { Type: CssTokenType.Ident }))
            {
                families.Add(string.Join(' ', significant.Select(component => SerializeIdentifier(((CssPreservedToken)component).Value))));
                continue;
            }

            var builder = new StringBuilder();
            SerializeList(builder, entry, keywords, zeroIsLength: false);
            families.Add(builder.ToString().Trim());
        }

        return string.Join(", ", families);
    }

    /// <summary>
    /// The shortest serialization of the values of some font properties, like the browsers
    /// (https://drafts.csswg.org/css-fonts/): oblique 0deg is normal, the value 1 (on) of a feature is omitted and off is 0,
    /// the default ex-height metric of font-size-adjust is omitted, and the trailing spaces of a language tag are removed.
    /// Returns null when the value does not change.
    /// </summary>
    internal static string? SerializeFontValue(string property, string value)
    {
        switch (property)
        {
            case "font-style" when value == "oblique 0deg":
                return "normal";

            case "font-size-adjust" when value.StartsWith("ex-height ", StringComparison.Ordinal):
                return value["ex-height ".Length..];

            case "font-feature-settings" when value != "normal":
            {
                var features = value.Split(", ");
                for (var i = 0; i < features.Length; i++)
                {
                    var feature = features[i];
                    if (feature.EndsWith(" 1", StringComparison.Ordinal) || feature.EndsWith(" on", StringComparison.Ordinal))
                    {
                        features[i] = feature[..feature.LastIndexOf(' ', StringComparison.Ordinal)];
                    }
                    else if (feature.EndsWith(" off", StringComparison.Ordinal))
                    {
                        features[i] = feature[..^"off".Length] + "0";
                    }
                }

                return string.Join(", ", features);
            }

            case "font-language-override" when value.Length > 2 && value[0] == '"' && value[^1] == '"' && value[^2] == ' ':
                return "\"" + value[1..^1].TrimEnd(' ') + "\"";

            default:
                return null;
        }
    }

    private static void SerializeList(StringBuilder builder, IReadOnlyList<CssComponentValue> values, HashSet<string>? keywords, bool zeroIsLength)
    {
        var pendingSpace = false;

        // The end of the serialization of the last component value: a space there ends an escape (\30 ), it is not a separator
        var componentEnd = -1;
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (value is CssPreservedToken { Type: CssTokenType.Whitespace })
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (value is CssPreservedToken { Type: CssTokenType.Comma })
            {
                builder.Append(", ");
                pendingSpace = false;
                continue;
            }

            if (value is CssPreservedToken slash && slash.IsDelim('/'))
            {
                TrimEnd(builder);
                builder.Append(builder.Length > 0 ? " / " : "/ ");
                pendingSpace = false;
                continue;
            }

            var adjacent = builder.Length > 0 && builder.Length == componentEnd;
            if (pendingSpace && builder.Length > 0 && (adjacent || (builder[^1] != ' ' && builder[^1] != '(')))
            {
                builder.Append(' ');
            }
            else if (!pendingSpace && adjacent && value is CssPreservedToken { Type: CssTokenType.Hash } && CssColor.Parse(value) is not null && WouldMergeWithFollowingName(builder[^1]))
            {
                // A hex color is serialized as rgb(): it is separated from the previous token it would merge with (a#fff, 1#fff)
                builder.Append(' ');
            }

            var start = builder.Length;
            SerializeComponentValue(builder, value, keywords, zeroIsLength);
            if (!pendingSpace && adjacent && i > 0 && values[i - 1] is CssPreservedToken { Type: CssTokenType.Delim } previous && StartsWithDigits(builder, start, previous.Token.Value))
            {
                // A number serialized without its sign is parsed with the delimiter before it ("+" followed by "+1" written "+1")
                builder.Insert(start, "/**/");
            }
            else if (!pendingSpace && adjacent && value is CssPreservedToken { Type: CssTokenType.Number or CssTokenType.Percentage or CssTokenType.Dimension }
                && start > 0 && start < builder.Length && char.IsAsciiDigit(builder[start]) && WouldMergeWithFollowingName(builder[start - 1]))
            {
                // A number serialized with a leading digit (".5" written "0.5", "+5" written "5") is parsed with the name or the
                // number before it ("a" followed by ".5" written "a0.5", "1px" followed by "+5" written "1px5")
                builder.Insert(start, "/**/");
            }

            pendingSpace = false;
            componentEnd = builder.Length;
        }

        // Whether the text appended at start would be parsed with the delimiter before it as a number
        static bool StartsWithDigits(StringBuilder builder, int start, string delimiter)
        {
            if (delimiter is not ("+" or "-" or "."))
                return false;

            if (start < builder.Length && char.IsAsciiDigit(builder[start]))
                return true;

            return delimiter is not "." && start + 1 < builder.Length && builder[start] == '.' && char.IsAsciiDigit(builder[start + 1]);
        }

        // The last character of a serialization followed by a name: an ident code point, the space ending an escape, @ (an
        // at-keyword), # (a hash), or \ (an escape)
        static bool WouldMergeWithFollowingName(char c) => c >= '\u0080' || char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ' ' or '@' or '#' or '\\';
    }

    /// <summary>
    /// The arguments of cross-fade() in the canonical order of &lt;cf-image&gt; (https://drafts.csswg.org/css-images-4/#cross-fade-function):
    /// the percentage follows the image, e.g. cross-fade(1% red, green) is cross-fade(red 1%, green).
    /// </summary>
    private static List<CssComponentValue> CanonicalizeCrossFadeArguments(List<CssComponentValue> arguments)
    {
        var result = new List<CssComponentValue>(arguments.Count);
        foreach (var argument in Cascade.CssValueComputer.SplitArguments(arguments))
        {
            if (result.Count > 0)
            {
                result.Add(new CssPreservedToken(new CssToken { Type = CssTokenType.Comma, Value = "," }));
            }

            var significant = ValueMatcher.RemoveWhitespace(argument);
            if (significant is [CssPreservedToken { Type: CssTokenType.Percentage } percentage, var image])
            {
                significant = [image, percentage];
            }

            result.AddRange(CssShorthands.InsertSpaces(significant));
        }

        return result;
    }

    private static void TrimEnd(StringBuilder builder)
    {
        while (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Length--;
        }
    }

    private static void SerializeComponentValue(StringBuilder builder, CssComponentValue value, HashSet<string>? keywords, bool zeroIsLength)
    {
        switch (value)
        {
            case CssFunction function when s_preserveColors && IsColorFunctionName(InfraAscii.ToAsciiLowercase(function.Name)):
            {
                builder.Append(InfraAscii.ToAsciiLowercase(function.Name)).Append('(');
                var inner = new StringBuilder();
                SerializeList(inner, function.Value, keywords, zeroIsLength: false);
                builder.Append(inner.ToString().Trim()).Append(')');
                return;
            }

            case CssFunction function:
            {
                // Colors are serialized in their canonical form
                if (CssColor.Parse(function) is { } color)
                {
                    builder.Append(color.Serialize());
                    return;
                }

                if (CssColor.SerializeWithMathFunctions(function) is { } colorWithMathFunctions)
                {
                    builder.Append(colorWithMathFunctions);
                    return;
                }

                // Math functions are simplified
                if (CssCalc.IsMathFunction(function.Name) && CssCalc.SerializeSpecifiedValue(function, percentagesAreLengths: s_percentagesAreLengths) is { } calc)
                {
                    builder.Append(calc);
                    return;
                }

                var name = InfraAscii.ToAsciiLowercase(function.Name);

                // The request URL modifiers are serialized in their canonical order
                if (name is "url" or "src" && ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken { Type: CssTokenType.String } url, _, ..] urlArguments && CssUrlModifiers.Canonicalize(urlArguments[1..]) is { } modifiers)
                {
                    builder.Append(CssUrlModifiers.Serialize(name, url.Value, modifiers));
                    return;
                }

                if (name == "path" && CssPath.Serialize(function, absolute: false) is { } path)
                {
                    builder.Append(path);
                    return;
                }

                if (TrySerializeGradient(builder, function, name, keywords) || (name == "color-mix" && TrySerializeColorMix(builder, function, keywords)) || (name == "color-hdr" && TrySerializeHdrColor(builder, function, keywords)) || (name == "color-layers" && TrySerializeColorLayers(builder, function, keywords)) || (name == "palette-mix" && TrySerializePaletteMix(builder, function, keywords)) || TrySerializeRelativeColor(builder, function, name, keywords))
                    return;

                // The default counter style of counter() and counters() is omitted (https://drafts.csswg.org/css-lists-3/#counter-functions)
                var arguments = function.Value;
                if (name is "counter" or "counters" && ValueMatcher.RemoveWhitespace(arguments) is [.., CssPreservedToken { Type: CssTokenType.Comma }, CssPreservedToken { Type: CssTokenType.Ident } style] && InfraAscii.EqualsIgnoreAsciiCase(style.Value, "decimal"))
                {
                    arguments = arguments[..arguments.FindLastIndex(argument => argument is CssPreservedToken { Type: CssTokenType.Comma })];
                }

                if (name == "cross-fade")
                {
                    arguments = CanonicalizeCrossFadeArguments(arguments);
                }

                // https://drafts.csswg.org/css-images-4/#deprecated: -webkit-image-set() is a parse-time alias of image-set()
                if (name is "image-set" or "-webkit-image-set")
                {
                    name = "image-set";
                    arguments = CssImageSet.Canonicalize(arguments);
                }

                // symbolic is the default type of symbols(), which is omitted (https://drafts.csswg.org/css-counter-styles-3/#symbols-function)
                if (name == "symbols" && arguments.FindIndex(argument => argument is not CssPreservedToken { Type: CssTokenType.Whitespace }) is var typeIndex && typeIndex >= 0 && arguments[typeIndex] is CssPreservedToken { Type: CssTokenType.Ident } type && InfraAscii.EqualsIgnoreAsciiCase(type.Value, "symbolic"))
                {
                    arguments = arguments[(typeIndex + 1)..];
                }

                arguments = CanonicalizeTransformFunctionArguments(name, arguments);
                builder.Append(SerializeIdentifier(GetCanonicalFunctionName(name))).Append('(');
                var inner = new StringBuilder();
                SerializeList(inner, arguments, keywords, zeroIsLength: false);
                builder.Append(inner.ToString().Trim()).Append(')');
                return;
            }

            case CssSimpleBlock block:
            {
                var (open, close) = block.AssociatedToken switch
                {
                    CssTokenType.LeftSquareBracket => ('[', ']'),
                    CssTokenType.LeftParenthesis => ('(', ')'),
                    _ => ('{', '}'),
                };

                var inner = new StringBuilder();
                SerializeList(inner, block.Value, keywords, zeroIsLength);
                builder.Append(open).Append(inner.ToString().Trim()).Append(close);
                return;
            }

            case CssPreservedToken { Token: var token }:
                switch (token.Type)
                {
                    case CssTokenType.Ident:
                    {
                        var lower = InfraAscii.ToAsciiLowercase(token.Value);
                        var keyword = ValueMatcher.CssWideKeywords.Contains(lower) || (keywords?.Contains(lower) ?? false) || CssColor.IsNamedColor(lower) || lower is "transparent" or "currentcolor";
                        builder.Append(SerializeIdentifier(keyword ? lower : token.Value));
                        return;
                    }

                    case CssTokenType.Hash:
                        if (CssColor.Parse(value) is { } hashColor)
                        {
                            builder.Append(hashColor.Serialize());
                        }
                        else
                        {
                            builder.Append('#').Append(SerializeIdentifier(token.Value));
                        }

                        return;

                    case CssTokenType.String:
                        builder.Append(SerializeString(token.Value));
                        return;
                    case CssTokenType.Url:
                        builder.Append(SerializeUrl(token.Value));
                        return;
                    case CssTokenType.Number when s_preserveColors:
                        builder.Append(token.NumericValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                        return;
                    case CssTokenType.Percentage when s_preserveColors:
                        builder.Append(token.NumericValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('%');
                        return;
                    case CssTokenType.Dimension when s_preserveColors:
                        builder.Append(token.NumericValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(SerializeIdentifier(InfraAscii.ToAsciiLowercase(token.Value)));
                        return;
                    case CssTokenType.Number when token.IsInteger && double.IsFinite(token.NumericValue) && Math.Abs(token.NumericValue) >= 1e21:
                        // The exponent notation of a large number is not an <integer>: the serialization would not be parsed as the same value
                        builder.Append(token.NumericValue.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
                        return;
                    case CssTokenType.Number:
                        builder.Append(SerializeNumber(token.NumericValue));
                        if (zeroIsLength && token.NumericValue == 0)
                        {
                            builder.Append("px");
                        }

                        return;
                    case CssTokenType.Percentage:
                        builder.Append(SerializeNumber(token.NumericValue)).Append('%');
                        return;
                    case CssTokenType.Dimension:
                        builder.Append(SerializeNumber(token.NumericValue)).Append(SerializeIdentifier(InfraAscii.ToAsciiLowercase(token.Value)));
                        return;
                    case CssTokenType.AtKeyword:
                        builder.Append('@').Append(SerializeIdentifier(token.Value));
                        return;
                    case CssTokenType.Delim:
                        builder.Append(token.Value);
                        return;
                    case CssTokenType.Colon:
                        builder.Append(':');
                        return;
                    case CssTokenType.Semicolon:
                        builder.Append(';');
                        return;
                    case CssTokenType.UnicodeRange:
                        builder.Append("U+").Append(token.RangeStart.ToString("X", System.Globalization.CultureInfo.InvariantCulture));
                        if (token.RangeEnd != token.RangeStart)
                        {
                            builder.Append('-').Append(token.RangeEnd.ToString("X", System.Globalization.CultureInfo.InvariantCulture));
                        }

                        return;
                    default:
                        builder.Append(token.ToString());
                        return;
                }
        }
    }

    /// <summary>https://drafts.csswg.org/cssom/#serialize-a-css-component-value: numbers are serialized like JavaScript numbers, with at most 6 decimals.</summary>
    public static string SerializeNumber(double value)
    {
        // A number too large for a double (1e400) is infinite: it is clamped to the largest finite value
        // (https://drafts.csswg.org/css-values-4/#numeric-ranges), as "Infinity" is an identifier, read back as another value
        if (double.IsInfinity(value))
        {
            value = value > 0 ? double.MaxValue : double.MinValue;
        }

        var rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
        if (rounded == 0)
            return "0";

        if (Math.Abs(rounded) >= 1e21 || Math.Abs(rounded) < 1e-6)
            return Html.Microsyntaxes.ToBestFloatingPointRepresentation(value);

        // A custom format has 15 significant digits, which rounds a large number to another one: 999999999999999918052 was
        // written as 1000000000000000000000, which is then written 1e+21 (fuzz regression). The fixed-point format writes
        // the exact digits of the number, which are read back as the same number
        if (Math.Abs(rounded) >= 1e15)
            return rounded.ToString("F6", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');

        return rounded.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }
}
