using System.Text.RegularExpressions;

namespace Meziantou.Framework.UrlPatternInternal;

// https://urlpattern.spec.whatwg.org/#url-pattern-struct

/// <summary>A component represents one part of a URL pattern (protocol, hostname, etc.).</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#component">WHATWG URL Pattern Spec - Component</see>
/// </remarks>
internal sealed class UrlPatternComponent
{
    public UrlPatternComponent(string patternString, Regex regularExpression, List<string> groupNameList, bool hasRegexpGroups)
    {
        PatternString = patternString;
        RegularExpression = regularExpression;
        GroupNameList = groupNameList;
        HasRegexpGroups = hasRegexpGroups;
    }

    /// <summary>Gets the pattern string.</summary>
    public string PatternString { get; }

    /// <summary>Gets the regular expression for matching.</summary>
    public Regex RegularExpression { get; }

    /// <summary>Gets the list of group names.</summary>
    public List<string> GroupNameList { get; }

    /// <summary>Gets whether this component has regexp groups.</summary>
    public bool HasRegexpGroups { get; }

    /// <summary>Compiles a component from an input pattern string.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#compile-a-component">WHATWG URL Pattern Spec - Compile a component</see>
    /// </remarks>
    public static UrlPatternComponent Compile(string input, Func<string, string> encodingCallback, PatternOptions options)
    {
        var tokenizer = new Tokenizer(input, TokenizePolicy.Strict);
        var tokenList = tokenizer.Tokenize();

        var parser = new PatternParser(tokenList, encodingCallback, options);
        var partList = parser.Parse();

        var (regexpString, nameList) = GenerateRegularExpressionAndNameList(partList, options);

        var regexOptions = RegexOptions.CultureInvariant;
        if (options.IgnoreCase)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        Regex regularExpression;
        try
        {
            regularExpression = new Regex(regexpString, regexOptions, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            throw new UrlPatternException($"Invalid regular expression: {regexpString}", ex);
        }

        var patternString = GeneratePatternString(partList, options);

        var hasRegexpGroups = false;
        foreach (var part in partList)
        {
            if (part.Type == PartType.Regexp)
            {
                hasRegexpGroups = true;
                break;
            }
        }

        return new UrlPatternComponent(patternString, regularExpression, nameList, hasRegexpGroups);
    }

    /// <summary>Generates a regular expression and name list from a part list.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#generate-a-regular-expression-and-name-list">WHATWG URL Pattern Spec - Generate a regular expression and name list</see>
    /// </remarks>
    private static (string RegexpString, List<string> NameList) GenerateRegularExpressionAndNameList(List<Part> partList, PatternOptions options)
    {
        var result = new StringBuilder("^");
        var nameList = new List<string>();

        foreach (var part in partList)
        {
            if (part.Type == PartType.FixedText)
            {
                if (part.Modifier == PartModifier.None)
                {
                    result.Append(PatternParser.EscapeRegexpString(part.Value));
                }
                else
                {
                    result.Append("(?:");
                    result.Append(PatternParser.EscapeRegexpString(part.Value));
                    result.Append(')');
                    result.Append(ConvertModifierToString(part.Modifier));
                }

                continue;
            }

            nameList.Add(part.Name);

            var regexpValue = part.Value;
            if (part.Type == PartType.SegmentWildcard)
            {
                regexpValue = GenerateSegmentWildcardRegexp(options);
            }
            else if (part.Type == PartType.FullWildcard)
            {
                regexpValue = ".*";
            }

            if (string.IsNullOrEmpty(part.Prefix) && string.IsNullOrEmpty(part.Suffix))
            {
                // No prefix or suffix
                if (part.Modifier is PartModifier.None or PartModifier.Optional)
                {
                    result.Append('(');
                    result.Append(regexpValue);
                    result.Append(')');
                    result.Append(ConvertModifierToString(part.Modifier));
                }
                else
                {
                    result.Append("((?:");
                    result.Append(regexpValue);
                    result.Append(')');
                    result.Append(ConvertModifierToString(part.Modifier));
                    result.Append(')');
                }

                continue;
            }

            if (part.Modifier is PartModifier.None or PartModifier.Optional)
            {
                // Non-repeating parts with prefix or suffix
                result.Append("(?:");
                result.Append(PatternParser.EscapeRegexpString(part.Prefix));
                result.Append('(');
                result.Append(regexpValue);
                result.Append(')');
                result.Append(PatternParser.EscapeRegexpString(part.Suffix));
                result.Append(')');
                result.Append(ConvertModifierToString(part.Modifier));
                continue;
            }

            // Repeating parts with prefix or suffix
            result.Append("(?:");
            result.Append(PatternParser.EscapeRegexpString(part.Prefix));
            result.Append("((?:");
            result.Append(regexpValue);
            result.Append(")(?:");
            result.Append(PatternParser.EscapeRegexpString(part.Suffix));
            result.Append(PatternParser.EscapeRegexpString(part.Prefix));
            result.Append("(?:");
            result.Append(regexpValue);
            result.Append("))*)");
            result.Append(PatternParser.EscapeRegexpString(part.Suffix));
            result.Append(')');

            if (part.Modifier == PartModifier.ZeroOrMore)
            {
                result.Append('?');
            }
        }

        result.Append('$');
        return (result.ToString(), nameList);
    }

    /// <summary>Generates a pattern string from a part list.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#generate-a-pattern-string">WHATWG URL Pattern Spec - Generate a pattern string</see>
    /// </remarks>
    private static string GeneratePatternString(List<Part> partList, PatternOptions options)
    {
        var result = new StringBuilder();

        for (var index = 0; index < partList.Count; index++)
        {
            var part = partList[index];
            var previousPart = index > 0 ? partList[index - 1] : null;
            var nextPart = index < partList.Count - 1 ? partList[index + 1] : null;

            if (part.Type == PartType.FixedText)
            {
                if (part.Modifier == PartModifier.None)
                {
                    result.Append(PatternParser.EscapePatternString(part.Value));
                    continue;
                }

                result.Append('{');
                result.Append(PatternParser.EscapePatternString(part.Value));
                result.Append('}');
                result.Append(ConvertModifierToString(part.Modifier));
                continue;
            }

            var customName = !string.IsNullOrEmpty(part.Name) && !char.IsDigit(part.Name[0]);

            var needsGrouping = false;
            if (!string.IsNullOrEmpty(part.Suffix) ||
                (!string.IsNullOrEmpty(part.Prefix) && part.Prefix != options.PrefixCodePoint))
            {
                needsGrouping = true;
            }

            if (!needsGrouping && customName && part.Type == PartType.SegmentWildcard && part.Modifier == PartModifier.None && nextPart is not null && string.IsNullOrEmpty(nextPart.Prefix) && string.IsNullOrEmpty(nextPart.Suffix))
            {
                if (nextPart.Type == PartType.FixedText)
                {
                    needsGrouping = !string.IsNullOrEmpty(nextPart.Value) && IsValidNameCodePoint(nextPart.Value[0], firstCodePoint: false);
                }
                else
                {
                    needsGrouping = !string.IsNullOrEmpty(nextPart.Name) && char.IsDigit(nextPart.Name[0]);
                }
            }

            if (!needsGrouping && string.IsNullOrEmpty(part.Prefix) && previousPart is not null && previousPart.Type == PartType.FixedText && !string.IsNullOrEmpty(previousPart.Value) && previousPart.Value[^1].ToString() == options.PrefixCodePoint)
            {
                needsGrouping = true;
            }

            if (needsGrouping)
            {
                result.Append('{');
            }

            result.Append(PatternParser.EscapePatternString(part.Prefix));

            if (customName)
            {
                result.Append(':');
                result.Append(part.Name);
            }

            if (part.Type == PartType.Regexp)
            {
                result.Append('(');
                result.Append(part.Value);
                result.Append(')');
            }
            else if (part.Type == PartType.SegmentWildcard && !customName)
            {
                result.Append('(');
                result.Append(GenerateSegmentWildcardRegexp(options));
                result.Append(')');
            }
            else if (part.Type == PartType.FullWildcard)
            {
                if (!customName && (previousPart is null || previousPart.Type == PartType.FixedText || previousPart.Modifier != PartModifier.None || needsGrouping || !string.IsNullOrEmpty(part.Prefix)))
                {
                    result.Append('*');
                }
                else
                {
                    result.Append("(.*)");
                }
            }

            if (part.Type == PartType.SegmentWildcard && customName && !string.IsNullOrEmpty(part.Suffix) && IsValidNameCodePoint(part.Suffix[0], firstCodePoint: false))
            {
                result.Append('\\');
            }

            result.Append(PatternParser.EscapePatternString(part.Suffix));

            if (needsGrouping)
            {
                result.Append('}');
            }

            result.Append(ConvertModifierToString(part.Modifier));
        }

        return result.ToString();
    }

    /// <summary>Generates the segment wildcard regexp.</summary>
    private static string GenerateSegmentWildcardRegexp(PatternOptions options)
    {
        // Note: In JavaScript regex, [^] matches any character including newline.
        // In .NET regex, [^] is invalid. We use [\s\S] instead which has the same effect.
        if (string.IsNullOrEmpty(options.DelimiterCodePoint))
        {
            return "[\\s\\S]+?";
        }

        return "[^" + PatternParser.EscapeRegexpString(options.DelimiterCodePoint) + "]+?";
    }

    /// <summary>Converts a modifier to its string representation.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#convert-a-modifier-to-a-string">WHATWG URL Pattern Spec - Convert a modifier to a string</see>
    /// </remarks>
    private static string ConvertModifierToString(PartModifier modifier)
    {
        return modifier switch
        {
            PartModifier.ZeroOrMore => "*",
            PartModifier.Optional => "?",
            PartModifier.OneOrMore => "+",
            _ => "",
        };
    }

    /// <summary>Determines if a code point is valid for a name.</summary>
    private static bool IsValidNameCodePoint(char codePoint, bool firstCodePoint)
    {
        if (firstCodePoint)
        {
            return char.IsLetter(codePoint) || codePoint == '_' || codePoint == '$';
        }

        return char.IsLetterOrDigit(codePoint) || codePoint == '_' || codePoint == '$';
    }
}
