using System.Collections.Concurrent;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.TypedOm;

/// <summary>
/// Converts the values of the properties to <see cref="CssStyleValue"/> objects (https://drafts.css-houdini.org/css-typed-om-1/#reify-stylevalue),
/// and the <see cref="CssStyleValue"/> objects to the values of the properties (https://drafts.css-houdini.org/css-typed-om-1/#create-an-internal-representation).
/// </summary>
/// <remarks>
/// The property-specific rules (https://drafts.css-houdini.org/css-typed-om-1/#reify-stylevalue) are derived from the grammar
/// of the properties: a keyword of the grammar (or a &lt;custom-ident&gt; that is the whole value) is a <see cref="CssKeywordValue"/>,
/// a number, a percentage, a dimension or a math function is a <see cref="CssNumericValue"/>, a &lt;transform-list&gt; is a
/// <see cref="CssTransformValue"/>, and an &lt;image&gt; is a <see cref="CssImageValue"/>. The colors and the other values
/// (e.g. a list of several components) are direct <see cref="CssStyleValue"/> objects, like in the browsers.
/// </remarks>
internal static class CssStyleValueReifier
{
    private static readonly ConcurrentDictionary<string, PropertyGrammarInfo> GrammarInfos = new(StringComparer.Ordinal);

    // ----------------------------------------------------------------------------------------------------------------
    // Parsing (https://drafts.css-houdini.org/css-typed-om-1/#parse-a-cssstylevalue)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#parse-a-cssstylevalue</summary>
    public static List<CssStyleValue> Parse(string property, string cssText, bool parseMultiple)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(cssText);
        var name = NormalizePropertyName(property) ?? throw new TypeErrorException($"'{property}' is not a valid CSS property");
        var values = CssParser.ParseListOfComponentValues(cssText, out var source);
        if (!IsValid(name, values, source))
            throw new TypeErrorException($"'{cssText}' is not a valid value for '{name}'");

        var result = Reify(name, values, source, computed: false);
        return parseMultiple ? result : [result[0]];
    }

    /// <summary>
    /// Returns the name of a valid CSS property (https://drafts.css-houdini.org/css-typed-om-1/#valid-css-property): a custom
    /// property name, or the ASCII lowercase name of a supported property; null when the property is not valid.
    /// </summary>
    public static string? NormalizePropertyName(string property)
    {
        if (CssPropertyRegistry.IsCustomProperty(property))
            return property;

        var name = InfraAscii.ToAsciiLowercase(property);
        return CssPropertyRegistry.Get(name) is null ? null : name;
    }

    /// <summary>Returns whether a value is valid for a property (it may contain var() functions).</summary>
    public static bool IsValid(string property, string value)
    {
        var values = CssParser.ParseListOfComponentValues(value, out var source);
        return IsValid(property, values, source);
    }

    private static bool IsValid(string property, List<CssComponentValue> values, string source)
    {
        // An empty value is invalid, even for a custom property
        if (ValueMatcher.RemoveWhitespace(values).Count == 0)
            return false;

        return CssStyleDeclaration.ParseValue(atRule: null, property, values, source.Trim(' ', '\t', '\n')) is not null;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Reification (https://drafts.css-houdini.org/css-typed-om-1/#reify-stylevalue)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Reifies the value of a property (a specified value, or a computed value when <paramref name="computed"/> is true) into the values of its iterations.</summary>
    public static List<CssStyleValue> Reify(string property, string value, bool computed)
    {
        var values = CssParser.ParseListOfComponentValues(value, out var source);
        return Reify(property, values, source, computed);
    }

    /// <summary>
    /// Reifies the specified value of a longhand of a declaration block: the keywords and the numeric values are reified
    /// from the value as specified (<paramref name="typedValue"/>) when its text differs from the serialized value, since the
    /// serialization does not keep the structure of every value (e.g. <c>opacity: 50%</c> is serialized as <c>0.5</c>, and
    /// <c>step-start</c> as <c>steps(1, start)</c>); the other values are reified from the serialized value.
    /// </summary>
    /// <param name="isRepresentation">
    /// Whether <paramref name="typedValue"/> is the internal representation of keywords, numeric values or transform values
    /// set through the Typed OM (see <see cref="CssLonghandValue.TypedValueIsRepresentation"/>): they are reified as they
    /// were set.
    /// </param>
    public static List<CssStyleValue> ReifySpecified(string property, string value, string? typedValue, bool isRepresentation)
    {
        var result = Reify(property, value, computed: false);
        if (typedValue is null)
            return result;

        var values = CssParser.ParseListOfComponentValues(typedValue, out var source);
        var typed = Reify(property, values, source, computed: false, isRepresentation);
        if (typed.Count != result.Count)
            return result;

        var representations = isRepresentation ? ReifyRepresentation(property, values) : null;
        for (var i = 0; i < result.Count; i++)
        {
            if (typed[i] is CssKeywordValue or CssNumericValue || (isRepresentation && typed[i] is CssTransformValue))
            {
                result[i] = typed[i];
            }
            else if (representations is not null && representations.Count == result.Count && representations[i] is { } representation && typed[i] is not CssUnparsedValue)
            {
                result[i] = representation;
            }
        }

        return result;
    }

    /// <summary>
    /// Reifies the internal representation of keywords and numeric values set through the Typed OM, whatever the grammar of
    /// the property (e.g. a keyword of a property whose value is a list, which is otherwise not reified): one value per
    /// iteration, null when the iteration is not a single keyword or numeric value.
    /// </summary>
    private static List<CssStyleValue?> ReifyRepresentation(string property, List<CssComponentValue> values)
    {
        var result = new List<CssStyleValue?>();
        foreach (var iteration in IsListValued(property) ? SplitOnCommas(values) : [values])
        {
            result.Add(ValueMatcher.RemoveWhitespace(iteration) switch
            {
                [CssPreservedToken { Type: CssTokenType.Ident } ident] => new CssKeywordValue(ident.Token.Value),
                [CssPreservedToken { Type: CssTokenType.Number or CssTokenType.Percentage or CssTokenType.Dimension } number] => ReifyNumericValue(number, computed: false, lengthContext: false),
                [CssFunction function] when CssCalc.IsMathFunction(function.Name) => ReifyNumericValue(function, computed: false, lengthContext: false, keepsMathStructure: true),
                _ => null,
            });
        }

        return result;
    }

    /// <summary>
    /// Reifies the computed value of a registered custom property according to the component of its syntax that the value
    /// matches (https://drafts.css-houdini.org/css-properties-values-api/#css-style-value-reification), or an argument of a
    /// worklet function that has a syntax (e.g. the <c>inputArguments</c> of a paint class): a numeric type is a numeric
    /// value, an identifier or a &lt;custom-ident&gt; a keyword, an &lt;image&gt; that is a URL an image value, a
    /// &lt;transform-function&gt; or a &lt;transform-list&gt; a transform value, and the other types (e.g. &lt;color&gt;,
    /// &lt;url&gt;) direct CSSStyleValue objects. A list (<c>+</c> or <c>#</c>) has one value per item. A value of the
    /// universal syntax, or a value that does not match the syntax, is a list of component values.
    /// </summary>
    public static List<CssStyleValue> ReifyWithSyntax(string property, CustomPropertySyntax syntax, string value)
    {
        var values = Trim(CssParser.ParseListOfComponentValues(value, out var source));
        if (syntax.IsUniversal || !syntax.TryMatch(values, out var component) || component is null)
            return [ReifyComponentList(values, source)];

        var significant = ValueMatcher.RemoveWhitespace(values);
        var items = component.Multiplier switch
        {
            '#' => SplitOnCommas(values).ConvertAll(Trim),
            '+' when component.Name != "transform-function" => significant.ConvertAll(item => new List<CssComponentValue> { item }),
            _ => [values],
        };

        var result = new List<CssStyleValue>(items.Count);
        foreach (var item in items)
        {
            var text = GetText(item, source);
            var itemValues = ValueMatcher.RemoveWhitespace(item);
            CssStyleValue? reified = null;
            if (!component.IsDataType || component.Name == "custom-ident")
            {
                reified = itemValues is [CssPreservedToken { Type: CssTokenType.Ident } ident] ? new CssKeywordValue(ident.Token.Value) : null;
            }
            else if (component.Name is "length" or "number" or "percentage" or "length-percentage" or "integer" or "angle" or "time" or "resolution")
            {
                reified = itemValues is [var numeric] ? ReifyNumericValue(numeric, computed: true, lengthContext: component.Name is "length" or "length-percentage") : null;
            }
            else if (component.Name == "image")
            {
                // As in the browsers, an image of a registered custom property is a CSSImageValue when it is a URL
                reified = itemValues is [CssPreservedToken { Type: CssTokenType.Url }] || (itemValues is [CssFunction function] && InfraAscii.ToAsciiLowercase(function.Name) is "url" or "src") ? new CssImageValue(text) : null;
            }
            else if (component.Name is "transform-function" or "transform-list")
            {
                reified = ReifyTransformList(itemValues, computed: true);
            }

            result.Add(reified ?? new CssStyleValue(property, text));
        }

        return result;
    }

    private static List<CssStyleValue> Reify(string property, List<CssComponentValue> values, string source, bool computed, bool isRepresentation = false)
    {
        values = Trim(values);
        var significant = ValueMatcher.RemoveWhitespace(values);

        // https://drafts.css-houdini.org/css-typed-om-1/#reify-a-list-of-component-values: custom properties and values with var()
        if (CssPropertyRegistry.IsCustomProperty(property) || ValueMatcher.ContainsSubstitutionFunction(significant))
            return [ReifyComponentList(values, source)];

        if (significant is [CssPreservedToken { Type: CssTokenType.Ident } keyword] && ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(keyword.Token.Value)))
            return [new CssKeywordValue(InfraAscii.ToAsciiLowercase(keyword.Token.Value))];

        var definition = CssPropertyRegistry.Get(property);
        if (definition is null || definition.IsShorthand)
        {
            // A shorthand is a direct CSSStyleValue, except text-align: it is a longhand for the Typed OM, whose keywords are
            // reified as identifiers, and a shorthand of text-align-all and text-align-last in CSS Text 4
            if (definition?.Name == "text-align" && significant is [CssPreservedToken { Type: CssTokenType.Ident } textAlign])
                return [new CssKeywordValue(InfraAscii.ToAsciiLowercase(textAlign.Token.Value))];

            return [new CssStyleValue(property, GetText(values, source))];
        }

        var info = GetGrammarInfo(definition);
        if (info.IsOpaque)
            return [new CssStyleValue(property, GetText(values, source))];

        // The computed value of a property whose value is a list of components (e.g. border-image-outset) always has all the
        // components (e.g. four lengths), which cannot be reified
        if (computed && info.HasMultipleComponents)
            return [new CssStyleValue(property, GetText(values, source))];

        var result = new List<CssStyleValue>();
        foreach (var iteration in info.IsListValued ? SplitOnCommas(values) : [values])
        {
            result.Add(ReifyIteration(property, info, Trim(iteration), source, computed, isRepresentation));
        }

        return result;
    }

    /// <param name="isRepresentation">Whether the value is the internal representation of values set through the Typed OM: the math functions keep their structure (see <see cref="ReifyNumericValue"/>).</param>
    private static CssStyleValue ReifyIteration(string property, PropertyGrammarInfo info, List<CssComponentValue> values, string source, bool computed, bool isRepresentation)
    {
        var significant = ValueMatcher.RemoveWhitespace(values);

        // A size of background-size or mask-size is serialized with its implicit auto (1px is 1px auto), but it is reified as
        // the single size, as in the browsers
        if (property is "background-size" or "mask-size" && significant is [not CssPreservedToken { Type: CssTokenType.Ident }, CssPreservedToken { Type: CssTokenType.Ident } auto] && InfraAscii.EqualsIgnoreAsciiCase(auto.Token.Value, "auto"))
        {
            values = significant = [significant[0]];
        }

        var text = GetText(values, source);
        if (significant is [var value])
        {
            switch (value)
            {
                // https://drafts.css-houdini.org/css-typed-om-1/#reify-ident
                case CssPreservedToken { Type: CssTokenType.Ident } ident:
                {
                    var name = InfraAscii.ToAsciiLowercase(ident.Token.Value);
                    if (info.Keywords.Contains(name))
                        return new CssKeywordValue(name);

                    // The colors are not reified (https://drafts.css-houdini.org/css-typed-om-1/#reify-a-color-value), like in the
                    // browsers, except the keywords that are not absolute colors (currentcolor and the system colors)
                    if (info.AcceptsColor && IsColorKeyword(name))
                        return new CssStyleValue(property, text);

                    if (info.AcceptsColor && (name == "currentcolor" || Values.CssColor.IsSystemColor(name)))
                        return new CssKeywordValue(name);

                    if (info.AcceptsCustomIdent)
                        return new CssKeywordValue(ident.Token.Value);

                    break;
                }

                case CssPreservedToken { Type: CssTokenType.Number or CssTokenType.Percentage or CssTokenType.Dimension } when !info.IsGridLine:
                    if (ReifyNumericValue(value, computed, lengthContext: !info.AcceptsNumber) is { } numeric)
                        return numeric;

                    break;

                case CssFunction function when CssCalc.IsMathFunction(function.Name) && !info.IsGridLine:
                    if (ReifyNumericValue(value, computed, lengthContext: !info.AcceptsNumber, keepsMathStructure: isRepresentation) is { } math)
                        return math;

                    break;

                case CssPreservedToken { Type: CssTokenType.Url } when info.AcceptsImage:
                    return new CssImageValue(text);

                case CssFunction function when info.AcceptsImage && IsImageFunction(function.Name):
                    return new CssImageValue(text);
            }
        }

        // https://drafts.css-houdini.org/css-typed-om-1/#reify-a-transform-list
        if (info.IsTransformList && significant.Count > 0 && ReifyTransformList(significant, computed, isRepresentation) is { } transform)
            return transform;

        // https://drafts.css-houdini.org/css-typed-om-1/#reify-as-a-cssstylevalue
        return new CssStyleValue(property, text);
    }

    /// <summary>Returns whether an identifier is a named color or transparent (https://drafts.csswg.org/css-color-4/#named-colors).</summary>
    private static bool IsColorKeyword(string name) => Values.CssColor.IsNamedColor(name) || InfraAscii.EqualsIgnoreAsciiCase(name, "transparent");

    private static bool IsImageFunction(string name) => InfraAscii.ToAsciiLowercase(name) is "url" or "src" or "image" or "image-set" or "-webkit-image-set" or "cross-fade" or "-webkit-cross-fade" or "element" or "paint"
        or "linear-gradient" or "radial-gradient" or "conic-gradient" or "repeating-linear-gradient" or "repeating-radial-gradient" or "repeating-conic-gradient"
        or "-webkit-linear-gradient" or "-webkit-radial-gradient" or "-webkit-repeating-linear-gradient" or "-webkit-repeating-radial-gradient" or "-webkit-gradient";

    /// <summary>
    /// https://drafts.css-houdini.org/css-typed-om-1/#reify-a-numeric-value: a number, a percentage, a dimension or a math
    /// function; null when the value is not numeric or cannot be represented (e.g. an unknown unit, a sign() function). A
    /// unitless zero is 0px in the context of a &lt;length&gt;, and a computed value uses the canonical unit (e.g. px).
    /// </summary>
    /// <param name="keepsMathStructure">
    /// Whether a math function is the internal representation of a math value set through the Typed OM, whose structure is kept
    /// (https://drafts.css-houdini.org/css-typed-om-1/#create-an-internal-representation): it is not simplified when its
    /// units are canonical (e.g. calc(2 + 3) is CSSMathSum(2, 3), not CSSMathSum(5)).
    /// </param>
    public static CssNumericValue? ReifyNumericValue(CssComponentValue value, bool computed, bool lengthContext, bool keepsMathStructure = false)
    {
        switch (value)
        {
            case CssPreservedToken { Type: CssTokenType.Number } number:
                return lengthContext && number.Token.NumericValue == 0 ? new CssUnitValue(0, "px") : new CssUnitValue(number.Token.NumericValue, "number");

            case CssPreservedToken { Type: CssTokenType.Percentage } percentage:
                return new CssUnitValue(percentage.Token.NumericValue, "percent");

            case CssPreservedToken { Type: CssTokenType.Dimension } dimension:
            {
                var unit = InfraAscii.ToAsciiLowercase(dimension.Token.Value);
                if (CssUnits.GetBaseType(unit) is null)
                    return null;

                var number = dimension.Token.NumericValue;
                if (computed)
                {
                    (number, unit) = CssUnits.ToCanonical(number, unit);
                }

                return new CssUnitValue(number, unit);
            }

            case CssFunction function:
            {
                // A computed value is simplified: a single value is not a sum (e.g. calc(10em) computes to 160px)
                var math = ReifyMathExpression(function, keepsMathStructure && !computed);
                if (computed && math is CssMathSum { Values: [CssUnitValue single] })
                {
                    var (number, unit) = CssUnits.ToCanonical(single.Value, single.Unit);
                    return new CssUnitValue(number, unit);
                }

                return math;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// https://drafts.css-houdini.org/css-typed-om-1/#reify-a-math-expression: the math function is simplified as a specified
    /// value (https://drafts.csswg.org/css-values-4/#calc-simplification), then converted to a tree of math values.
    /// </summary>
    private static CssNumericValue? ReifyMathExpression(CssFunction function, bool keepsMathStructure)
    {
        // The structure of a math value set through the Typed OM is kept when its units are canonical (or relative); otherwise
        // the units are converted, which simplifies the value like a specified value (e.g. calc(0s + 0ms) is CSSMathSum(0s))
        var simplifiedFunction = function;
        if (!keepsMathStructure || ContainsNonCanonicalUnit(function.Value))
        {
            if (CssCalc.SerializeSpecifiedValue(function) is not { } simplified)
                return null;

            if (ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(simplified)) is not [CssFunction parsed])
                return null;

            simplifiedFunction = parsed;
        }

        try
        {
            return ConvertMathFunction(simplifiedFunction, topLevel: true);
        }
        catch (TypeErrorException)
        {
            return null;
        }
        catch (DomException)
        {
            return null;
        }
    }

    /// <summary>Returns whether a dimension of the values is not expressed in the canonical unit of its type (e.g. ms, in; the relative units are canonical).</summary>
    private static bool ContainsNonCanonicalUnit(List<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Dimension } dimension:
                {
                    var unit = InfraAscii.ToAsciiLowercase(dimension.Token.Value);
                    if (CssUnits.GetBaseType(unit) is not null && CssUnits.ToCanonical(1, unit).Unit != unit)
                        return true;

                    break;
                }

                case CssFunction function when ContainsNonCanonicalUnit(function.Value):
                    return true;

                case CssSimpleBlock block when ContainsNonCanonicalUnit(block.Value):
                    return true;
            }
        }

        return false;
    }

    private static CssNumericValue? ConvertMathFunction(CssFunction function, bool topLevel)
    {
        switch (CssCalc.GetKind(function.Name))
        {
            case CssCalcFunctionKind.Calc:
            {
                // A single value without operation is a sum with a single argument
                var result = ConvertSum(ValueMatcher.RemoveWhitespace(function.Value), out var hasOperation);
                return result is not null && topLevel && !hasOperation ? new CssMathSum([result]) : result;
            }

            case CssCalcFunctionKind.Min or CssCalcFunctionKind.Max:
            {
                var arguments = new List<CssNumericValue>();
                foreach (var argument in SplitOnCommas(function.Value))
                {
                    if (ConvertSum(ValueMatcher.RemoveWhitespace(argument), out _) is not { } converted)
                        return null;

                    arguments.Add(converted);
                }

                return CssCalc.GetKind(function.Name) == CssCalcFunctionKind.Min ? new CssMathMin(arguments) : new CssMathMax(arguments);
            }

            case CssCalcFunctionKind.Clamp:
            {
                var arguments = SplitOnCommas(function.Value);
                if (arguments.Count != 3)
                    return null;

                var lower = ConvertSum(ValueMatcher.RemoveWhitespace(arguments[0]), out _);
                var value = ConvertSum(ValueMatcher.RemoveWhitespace(arguments[1]), out _);
                var upper = ConvertSum(ValueMatcher.RemoveWhitespace(arguments[2]), out _);
                return lower is null || value is null || upper is null ? null : new CssMathClamp(lower, value, upper);
            }

            default:
                // The other math functions (e.g. round(), sign()) have no math value
                return null;
        }
    }

    /// <summary>Converts a sum: the subtractions are additions of a negated value.</summary>
    private static CssNumericValue? ConvertSum(List<CssComponentValue> values, out bool hasOperation)
    {
        var terms = new List<CssNumericValue>();
        var start = 0;
        var negate = false;
        hasOperation = false;
        for (var i = 0; i <= values.Count; i++)
        {
            if (i < values.Count && !(values[i] is CssPreservedToken { Type: CssTokenType.Delim } delim && (delim.Token.IsDelim('+') || delim.Token.IsDelim('-'))))
                continue;

            if (ConvertProduct(values[start..i], out var productHasOperation) is not { } term)
                return null;

            hasOperation |= productHasOperation;
            terms.Add(negate ? new CssMathNegate(term) : term);
            if (i < values.Count)
            {
                negate = ((CssPreservedToken)values[i]).Token.IsDelim('-');
                hasOperation = true;
                start = i + 1;
            }
        }

        return terms.Count == 1 ? terms[0] : new CssMathSum(terms);
    }

    /// <summary>Converts a product: the divisions are multiplications by an inverted value.</summary>
    private static CssNumericValue? ConvertProduct(List<CssComponentValue> values, out bool hasOperation)
    {
        hasOperation = false;
        if (values.Count == 0 || values.Count % 2 == 0)
            return null;

        var factors = new List<CssNumericValue>();
        for (var i = 0; i < values.Count; i += 2)
        {
            var invert = false;
            if (i > 0)
            {
                if (values[i - 1] is not CssPreservedToken { Type: CssTokenType.Delim } delim || !(delim.Token.IsDelim('*') || delim.Token.IsDelim('/')))
                    return null;

                invert = delim.Token.IsDelim('/');
                hasOperation = true;
            }

            if (ConvertCalcValue(values[i]) is not { } factor)
                return null;

            factors.Add(invert ? new CssMathInvert(factor) : factor);
        }

        return factors.Count == 1 ? factors[0] : new CssMathProduct(factors);
    }

    private static CssNumericValue? ConvertCalcValue(CssComponentValue value) => value switch
    {
        CssPreservedToken { Type: CssTokenType.Number or CssTokenType.Percentage or CssTokenType.Dimension } => ReifyNumericValue(value, computed: false, lengthContext: false),
        CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } block => ConvertSum(ValueMatcher.RemoveWhitespace(block.Value), out _),
        CssFunction function => ConvertMathFunction(function, topLevel: false),
        _ => null,
    };

    /// <summary>
    /// https://drafts.css-houdini.org/css-typed-om-1/#reify-a-list-of-component-values: the var() functions are
    /// <see cref="CssVariableReferenceValue"/> objects, the other component values are strings.
    /// </summary>
    private static CssUnparsedValue ReifyComponentList(List<CssComponentValue> values, string source)
    {
        var segments = new List<CssUnparsedSegment>();
        var text = new StringBuilder();
        AppendComponentList(values, source, segments, text);
        if (text.Length > 0)
        {
            segments.Add(text.ToString());
        }

        return new CssUnparsedValue(segments);
    }

    private static void AppendComponentList(List<CssComponentValue> values, string source, List<CssUnparsedSegment> segments, StringBuilder text)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssFunction function when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "var"):
                {
                    if (text.Length > 0)
                    {
                        segments.Add(text.ToString());
                        text.Clear();
                    }

                    segments.Add(ReifyVariableReference(function, source));
                    break;
                }

                case CssFunction function when ValueMatcher.ContainsSubstitutionFunction(function.Value):
                    text.Append(CssSerializer.SerializeIdentifier(function.Name)).Append('(');
                    AppendComponentList(function.Value, source, segments, text);
                    text.Append(')');
                    break;

                case CssSimpleBlock block when ValueMatcher.ContainsSubstitutionFunction(block.Value):
                {
                    var (open, close) = block.AssociatedToken switch
                    {
                        CssTokenType.LeftSquareBracket => ('[', ']'),
                        CssTokenType.LeftCurlyBracket => ('{', '}'),
                        _ => ('(', ')'),
                    };

                    text.Append(open);
                    AppendComponentList(block.Value, source, segments, text);
                    text.Append(close);
                    break;
                }

                default:
                    text.Append(GetText(value, source));
                    break;
            }
        }
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#reify-a-var-reference</summary>
    private static CssVariableReferenceValue ReifyVariableReference(CssFunction function, string source)
    {
        var arguments = function.Value;
        var comma = arguments.FindIndex(v => v is CssPreservedToken { Type: CssTokenType.Comma });
        var name = ValueMatcher.RemoveWhitespace(comma < 0 ? arguments : arguments[..comma]) is [CssPreservedToken { Type: CssTokenType.Ident } ident] ? ident.Token.Value : GetText(Trim(arguments), source);
        var fallback = comma < 0 ? null : ReifyComponentList(arguments[(comma + 1)..], source);
        return new CssVariableReferenceValue(name, fallback);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Transforms (https://drafts.css-houdini.org/css-typed-om-1/#reify-a-transform-list)
    // ----------------------------------------------------------------------------------------------------------------

    /// <param name="isRepresentation">Whether the list is the internal representation of a transform value set through the Typed OM (see <see cref="ReifyTransformFunction"/>).</param>
    private static CssTransformValue? ReifyTransformList(List<CssComponentValue> values, bool computed, bool isRepresentation = false)
    {
        var components = new List<CssTransformComponent>(values.Count);
        foreach (var value in values)
        {
            if (value is not CssFunction function || ReifyTransformFunction(function, computed, isRepresentation) is not { } component)
                return null;

            components.Add(component);
        }

        return new CssTransformValue(components);
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#reify-a-transform-function: returns null when a value cannot be represented.</summary>
    /// <param name="isRepresentation">
    /// Whether the function is the internal representation of a component set through the Typed OM: the calc() added by the
    /// serialization of the length of a CSSPerspective that cannot be compared to zero (a relative length,
    /// https://drafts.css-houdini.org/css-typed-om-1/#serialize-a-cssperspective) is not part of the value.
    /// </param>
    private static CssTransformComponent? ReifyTransformFunction(CssFunction function, bool computed, bool isRepresentation)
    {
        var arguments = new List<CssComponentValue>();
        foreach (var argument in SplitOnCommas(function.Value))
        {
            if (ValueMatcher.RemoveWhitespace(argument) is not [var value])
                return null;

            arguments.Add(value);
        }

        CssNumericValue? Length(int index) => index < arguments.Count ? ReifyNumericValue(arguments[index], computed, lengthContext: true) : new CssUnitValue(0, "px");
        CssNumericValue? Number(int index, double defaultValue)
        {
            if (index >= arguments.Count)
                return new CssUnitValue(defaultValue, "number");

            // A percentage is a number (scale(50%) is scale(0.5))
            var result = ReifyNumericValue(arguments[index], computed, lengthContext: false);
            return result is CssUnitValue { Unit: "percent" } percent ? new CssUnitValue(percent.Value / 100, "number") : result;
        }

        CssNumericValue? Angle(int index)
        {
            if (index >= arguments.Count)
                return new CssUnitValue(0, "deg");

            // A unitless zero is an angle in the transform functions
            return arguments[index] is CssPreservedToken { Type: CssTokenType.Number, Token.NumericValue: 0 } ? new CssUnitValue(0, "deg") : ReifyNumericValue(arguments[index], computed, lengthContext: false);
        }

        try
        {
            var name = InfraAscii.ToAsciiLowercase(function.Name);
            return (name, arguments.Count) switch
            {
                ("matrix", 6) or ("matrix3d", 16) => ReifyMatrix(arguments, computed),
                ("translate", 1 or 2) when Length(0) is { } x && Length(1) is { } y => new CssTranslate(x, y),
                ("translatex", 1) when Length(0) is { } x => new CssTranslate(x, new CssUnitValue(0, "px")),
                ("translatey", 1) when Length(0) is { } y => new CssTranslate(new CssUnitValue(0, "px"), y),
                ("translatez", 1) when Length(0) is { } z => new CssTranslate(new CssUnitValue(0, "px"), new CssUnitValue(0, "px"), z),
                ("translate3d", 3) when Length(0) is { } x && Length(1) is { } y && Length(2) is { } z => new CssTranslate(x, y, z),
                ("scale", 1 or 2) when Number(0, 1) is { } x && (arguments.Count == 1 ? x : Number(1, 1)) is { } y => new CssScale(x, y),
                ("scalex", 1) when Number(0, 1) is { } x => new CssScale(x, 1),
                ("scaley", 1) when Number(0, 1) is { } y => new CssScale(1, y),
                ("scalez", 1) when Number(0, 1) is { } z => new CssScale(1, 1, z),
                ("scale3d", 3) when Number(0, 1) is { } x && Number(1, 1) is { } y && Number(2, 1) is { } z => new CssScale(x, y, z),
                ("rotate", 1) when Angle(0) is { } angle => new CssRotate(angle),
                ("rotatex", 1) when Angle(0) is { } angle => new CssRotate(1, 0, 0, angle),
                ("rotatey", 1) when Angle(0) is { } angle => new CssRotate(0, 1, 0, angle),
                ("rotatez", 1) when Angle(0) is { } angle => new CssRotate(0, 0, 1, angle),
                ("rotate3d", 4) when Number(0, 0) is { } x && Number(1, 0) is { } y && Number(2, 0) is { } z && Angle(3) is { } angle => new CssRotate(x, y, z, angle),
                ("skew", 1 or 2) when Angle(0) is { } ax && Angle(1) is { } ay => new CssSkew(ax, ay),
                ("skewx", 1) when Angle(0) is { } ax => new CssSkewX(ax),
                ("skewy", 1) when Angle(0) is { } ay => new CssSkewY(ay),
                ("perspective", 1) when arguments[0] is CssPreservedToken { Type: CssTokenType.Ident } ident => new CssPerspective(new CssKeywordValue(InfraAscii.ToAsciiLowercase(ident.Token.Value))),
                ("perspective", 1) when Length(0) is { } length => new CssPerspective(isRepresentation && length is CssMathSum { Values: [CssUnitValue single] } && arguments[0] is CssFunction { Value: var calc } && ValueMatcher.RemoveWhitespace(calc) is [CssPreservedToken { Type: CssTokenType.Dimension }] ? single : length),
                _ => null,
            };
        }
        catch (TypeErrorException)
        {
            return null;
        }
    }

    private static CssMatrixComponent? ReifyMatrix(List<CssComponentValue> arguments, bool computed)
    {
        var elements = new double[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            if (ReifyNumericValue(arguments[i], computed, lengthContext: false) is not CssUnitValue { Unit: "number" } number)
                return null;

            elements[i] = number.Value;
        }

        // matrix() is a 2D matrix, matrix3d() a 3D matrix
        return new CssMatrixComponent(new DomMatrix(elements));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Internal representations (https://drafts.css-houdini.org/css-typed-om-1/#create-an-internal-representation)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the text of the value of a property represented by a <see cref="CssStyleValue"/> or a string
    /// (https://drafts.css-houdini.org/css-typed-om-1/#create-an-internal-representation), or throws a TypeError when the
    /// value does not match the grammar of the property. A numeric value outside of the range of the property is wrapped in
    /// calc() (e.g. calc(-1px) for width).
    /// </summary>
    public static string CreateInternalRepresentation(string property, CssStyleValueOrString value)
    {
        // https://drafts.css-houdini.org/css-typed-om-1/#cssstylevalue-match-a-grammar: only a CSSUnparsedValue (which matches
        // any grammar) and the value reified from the property match the grammar of a custom property
        // (https://drafts.css-houdini.org/css-properties-values-api/#css-style-value-reification: a registered custom
        // property is set to a string or to a CSSUnparsedValue)
        if (CssPropertyRegistry.IsCustomProperty(property) && value.Value is CssStyleValue and not CssUnparsedValue and not { AssociatedProperty: not null })
            throw new TypeErrorException($"'{value.Value}' is not a valid value for the custom property '{property}'");

        string text;
        switch (value.Value)
        {
            case string cssText:
                text = cssText;
                break;

            case CssNumericValue when IsGridLine(property):
                // The integer of a <grid-line> is not a numeric value of the Typed OM (see PropertyGrammarInfo.IsGridLine)
                throw new TypeErrorException($"'{value.Value}' is not a valid value for '{property}'");

            case CssUnitValue unit:
            {
                // The type of the value must match the grammar: calc() checks it (e.g. a unitless zero is not a length)
                var serialized = unit.ToString();
                var calc = "calc(" + serialized + ")";
                if (!IsValid(property, calc))
                    throw new TypeErrorException($"'{serialized}' is not a valid value for '{property}'");

                return IsValid(property, serialized) ? serialized : calc;
            }

            case CssKeywordValue keyword:
                text = ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(keyword.Value)) ? keyword.Value : keyword.ToString();
                break;

            case CssStyleValue styleValue:
                text = styleValue.ToString();
                break;

            default:
                throw new TypeErrorException("The value is not a CSSStyleValue or a string");
        }

        if (!IsValid(property, text))
            throw new TypeErrorException($"'{text}' is not a valid value for '{property}'");

        return text;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns whether a property is a list-valued property (https://drafts.css-houdini.org/css-typed-om-1/#list-valued-property).</summary>
    public static bool IsListValued(string property) => !CssPropertyRegistry.IsCustomProperty(property) && CssPropertyRegistry.Get(property) is { IsShorthand: false } definition && GetGrammarInfo(definition).IsListValued;

    private static bool IsGridLine(string property) => !CssPropertyRegistry.IsCustomProperty(property) && CssPropertyRegistry.Get(property) is { IsShorthand: false } definition && GetGrammarInfo(definition).IsGridLine;

    private static PropertyGrammarInfo GetGrammarInfo(CssPropertyDefinition definition) => GrammarInfos.GetOrAdd(definition.Name, static (_, definition) => PropertyGrammarInfo.Create(definition), definition);

    private static List<CssComponentValue> Trim(List<CssComponentValue> values)
    {
        var start = 0;
        var end = values.Count;
        while (start < end && values[start] is CssPreservedToken { Type: CssTokenType.Whitespace })
        {
            start++;
        }

        while (end > start && values[end - 1] is CssPreservedToken { Type: CssTokenType.Whitespace })
        {
            end--;
        }

        return values[start..end];
    }

    private static List<List<CssComponentValue>> SplitOnCommas(List<CssComponentValue> values)
    {
        var result = new List<List<CssComponentValue>>();
        var current = new List<CssComponentValue>();
        foreach (var value in values)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Comma })
            {
                result.Add(current);
                current = [];
            }
            else
            {
                current.Add(value);
            }
        }

        result.Add(current);
        return result;
    }

    private static string GetText(CssComponentValue value, string source)
    {
        var start = Math.Clamp(value.Start, 0, source.Length);
        var end = Math.Clamp(value.End, start, source.Length);
        return source[start..end];
    }

    private static string GetText(List<CssComponentValue> values, string source)
    {
        if (values.Count == 0)
            return "";

        var start = Math.Clamp(values[0].Start, 0, source.Length);
        var end = Math.Clamp(values[^1].End, start, source.Length);
        return source[start..end];
    }

    /// <summary>What the grammar of a longhand accepts, which determines how its values are reified.</summary>
    private sealed class PropertyGrammarInfo
    {
        private const int MaxDepth = 32;

        /// <summary>
        /// The properties that are always reified as direct CSSStyleValue objects (https://drafts.css-houdini.org/css-typed-om-1/#reify-stylevalue),
        /// except for the CSS-wide keywords and the var() references.
        /// </summary>
        /// <remarks>
        /// The values of the rule properties of CSS Gaps are lists of values and of repeat() functions
        /// (https://drafts.csswg.org/css-gaps-1/#lists-repeat), which have no representation, even when the list has one value.
        /// </remarks>
        private static readonly HashSet<string> OpaqueProperties = new(StringComparer.Ordinal)
        {
            "font-family", "column-rule-style", "row-rule-style", "column-rule-width", "row-rule-width",
        };

        /// <summary>
        /// The properties whose grammar is a bounded repetition, but whose computed value has the components of the specified
        /// value (https://drafts.csswg.org/css-overflow-4/#text-overflow): a single component is reified.
        /// </summary>
        private static readonly HashSet<string> ComputedAsSpecifiedProperties = new(StringComparer.Ordinal) { "text-overflow" };

        /// <summary>Whether the property is a list-valued property: the iterations are separated by commas (e.g. background-image).</summary>
        public bool IsListValued { get; private init; }

        /// <summary>The keywords of the grammar (ASCII lowercase).</summary>
        public HashSet<string> Keywords { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Whether the computed value is a list of a fixed number of components separated by spaces (e.g. the four lengths
        /// of [ &lt;length&gt; | &lt;number&gt; ]{1,4}), which cannot be reified.
        /// </summary>
        public bool HasMultipleComponents { get; private init; }

        /// <summary>Whether a &lt;custom-ident&gt; can be the whole value of an iteration (e.g. animation-name, but not counter-reset).</summary>
        public bool AcceptsCustomIdent { get; private set; }

        /// <summary>Whether a &lt;number&gt; is accepted (otherwise a unitless zero is a length).</summary>
        public bool AcceptsNumber { get; private set; }

        public bool AcceptsImage { get; private set; }

        public bool AcceptsColor { get; private set; }

        public bool IsTransformList { get; private init; }

        /// <summary>
        /// Whether the value is a &lt;grid-line&gt; (grid-row-start, grid-column-end...): its integer is a line number that can
        /// be combined with a name and with span, not a numeric value. As in the browsers, it is not reified as a numeric
        /// value (the specification has no rule for these properties), and a numeric value cannot be set.
        /// </summary>
        public bool IsGridLine { get; private init; }

        public bool IsOpaque { get; private init; }

        public static PropertyGrammarInfo Create(CssPropertyDefinition definition)
        {
            var grammar = CssPropertyRegistry.GetGrammar(definition);
            var info = new PropertyGrammarInfo
            {
                // A list-valued property is a list at the top level (e.g. not will-change: auto | <animateable-feature>#)
                IsListValued = grammar is { CommaSeparated: true, Max: > 1 },
                // The computed value of a corner radius is always a pair of radii (https://drafts.csswg.org/css-backgrounds-3/#border-radius)
                HasMultipleComponents = (grammar is not null && IsRepeated(grammar, 0) && !ComputedAsSpecifiedProperties.Contains(definition.Name)) || definition.Grammar == "<border-radius>",
                IsTransformList = definition.Name == "transform",
                IsGridLine = definition.Grammar == "<grid-line>",
                IsOpaque = OpaqueProperties.Contains(definition.Name),
            };

            if (grammar is not null)
            {
                info.Visit(grammar, new HashSet<string>(StringComparer.Ordinal), isTopLevel: true, inList: false, 0);
                info.AcceptsCustomIdent = AcceptsCustomIdentAlone(grammar, isTopLevel: true, 0);
            }

            return info;
        }

        /// <summary>Whether the grammar is a bounded repetition (e.g. {1,4}, possibly in a type or with an optional keyword).</summary>
        private static bool IsRepeated(GrammarNode grammar, int depth)
        {
            if (depth > 4)
                return false;

            if (grammar.Max > 1 && grammar.Max != int.MaxValue && !grammar.CommaSeparated)
                return true;

            if (grammar.Max != 1)
                return false;

            return grammar switch
            {
                GroupNode { Children: [var child] } => IsRepeated(child, depth + 1),
                GroupNode { Combinator: GrammarCombinator.OneOf } group => group.Children.TrueForAll(c => IsRepeated(c, depth + 1)),
                GroupNode { Combinator: GrammarCombinator.AllInAnyOrder or GrammarCombinator.AnyInAnyOrder } group => group.Children.Exists(c => c.Max > 1 && c.Max != int.MaxValue && !c.CommaSeparated),
                TypeNode type => GetTypeGrammar(type) is { } typeGrammar && IsRepeated(typeGrammar, depth + 1),
                _ => false,
            };
        }

        private static bool IsNestedList(GrammarNode node, bool isTopLevel) => !isTopLevel && node.CommaSeparated && node.Max > 1;

        private static GrammarNode? GetTypeGrammar(TypeNode type)
        {
            if (type.IsProperty)
                return CssPropertyRegistry.Get(type.Name) is { } property ? CssPropertyRegistry.GetGrammar(property) : null;

            return CssPropertyRegistry.GetTypeGrammar(type.Name.EndsWith("()", StringComparison.Ordinal) ? type.Name : "<" + type.Name + ">");
        }

        /// <summary>
        /// Collects the keywords and the types of the grammar (the arguments of the functions are ignored). The keywords of a
        /// list that is not the whole value (e.g. will-change: auto | &lt;animateable-feature&gt;#) are not collected: such a list
        /// is not reified, like in the browsers.
        /// </summary>
        private void Visit(GrammarNode node, HashSet<string> visitedTypes, bool isTopLevel, bool inList, int depth)
        {
            if (depth > MaxDepth)
                return;

            inList |= IsNestedList(node, isTopLevel);
            switch (node)
            {
                case KeywordNode keyword:
                    if (!inList)
                    {
                        Keywords.Add(InfraAscii.ToAsciiLowercase(keyword.Keyword));
                    }

                    break;

                case GroupNode group:
                    foreach (var child in group.Children)
                    {
                        Visit(child, visitedTypes, isTopLevel: false, inList, depth + 1);
                    }

                    break;

                case TypeNode type:
                    switch (type.Name)
                    {
                        case "number" or "integer":
                            AcceptsNumber = true;
                            return;
                        case "image" or "url":
                            AcceptsImage = true;
                            return;
                        case "color":
                            AcceptsColor = true;
                            return;
                    }

                    if (visitedTypes.Add(type + (inList ? " list" : "")) && GetTypeGrammar(type) is { } typeGrammar)
                    {
                        Visit(typeGrammar, visitedTypes, isTopLevel: false, inList, depth + 1);
                    }

                    break;
            }
        }

        /// <summary>Returns whether a single &lt;custom-ident&gt; can be a whole value of the grammar (it is not a component of a sequence or of a list).</summary>
        private static bool AcceptsCustomIdentAlone(GrammarNode node, bool isTopLevel, int depth)
        {
            if (depth > MaxDepth || node.Min > 1 || IsNestedList(node, isTopLevel))
                return false;

            switch (node)
            {
                case GroupNode { Combinator: GrammarCombinator.OneOf or GrammarCombinator.AnyInAnyOrder } group:
                    return group.Children.Exists(child => AcceptsCustomIdentAlone(child, isTopLevel: false, depth + 1));

                case GroupNode group:
                    return group.Children.Count == 1 && AcceptsCustomIdentAlone(group.Children[0], isTopLevel: false, depth + 1);

                case TypeNode { Name: "custom-ident" or "ident" }:
                    return true;

                case TypeNode { Name: "dashed-ident" or "string" or "number" or "integer" or "length" or "percentage" or "angle" or "time" or "frequency" or "resolution" or "flex" or "dimension" or "color" or "image" or "url" }:
                    return false;

                case TypeNode type:
                    return GetTypeGrammar(type) is { } grammar && AcceptsCustomIdentAlone(grammar, isTopLevel: false, depth + 1);

                default:
                    return false;
            }
        }
    }
}
