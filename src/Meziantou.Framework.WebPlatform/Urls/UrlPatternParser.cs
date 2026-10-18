using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Urls;

/// <summary>https://urlpattern.spec.whatwg.org/#tokens</summary>
internal enum UrlPatternTokenType
{
    InvalidChar,
    Open,
    Close,
    Regexp,
    Name,
    Char,
    EscapedChar,
    OtherModifier,
    Asterisk,
    End,
}

/// <summary>https://urlpattern.spec.whatwg.org/#token</summary>
internal readonly record struct UrlPatternToken(UrlPatternTokenType Type, int Index, string Value);

/// <summary>https://urlpattern.spec.whatwg.org/#part-type</summary>
internal enum UrlPatternPartType
{
    FixedText,
    Regexp,
    SegmentWildcard,
    FullWildcard,
}

/// <summary>https://urlpattern.spec.whatwg.org/#part-modifier</summary>
internal enum UrlPatternPartModifier
{
    None,
    Optional,
    ZeroOrMore,
    OneOrMore,
}

/// <summary>https://urlpattern.spec.whatwg.org/#part</summary>
internal sealed record UrlPatternPart(UrlPatternPartType Type, string Value, UrlPatternPartModifier Modifier, string Name = "", string Prefix = "", string Suffix = "");

/// <summary>https://urlpattern.spec.whatwg.org/#options</summary>
internal readonly record struct UrlPatternCompileOptions(string DelimiterCodePoint, string PrefixCodePoint, bool IgnoreCase)
{
    /// <summary>https://urlpattern.spec.whatwg.org/#default-options</summary>
    public static UrlPatternCompileOptions Default { get; } = new("", "", IgnoreCase: false);

    /// <summary>https://urlpattern.spec.whatwg.org/#hostname-options</summary>
    public static UrlPatternCompileOptions Hostname { get; } = new(".", "", IgnoreCase: false);

    /// <summary>https://urlpattern.spec.whatwg.org/#pathname-options</summary>
    public static UrlPatternCompileOptions Pathname { get; } = new("/", "/", IgnoreCase: false);
}

/// <summary>A string as a list of code points, with the code point substrings of the URL Pattern algorithms.</summary>
internal readonly struct CodePointString
{
    private readonly int[] _codePoints;

    public CodePointString(string value)
    {
        var codePoints = new List<int>(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            codePoints.Add(rune.Value);
        }

        _codePoints = [.. codePoints];
    }

    public int Length => _codePoints.Length;

    public int this[int index] => _codePoints[index];

    /// <summary>https://infra.spec.whatwg.org/#code-point-substring</summary>
    public string Substring(int start, int length)
    {
        var builder = new StringBuilder(length);
        Span<char> buffer = stackalloc char[2];
        for (var i = start; i < start + length; i++)
        {
            var written = new Rune(_codePoints[i]).EncodeToUtf16(buffer);
            builder.Append(buffer[..written]);
        }

        return builder.ToString();
    }
}

/// <summary>The pattern string algorithms of the URL Pattern Standard (https://urlpattern.spec.whatwg.org/#pattern-strings).</summary>
internal static class UrlPatternParser
{
    /// <summary>https://urlpattern.spec.whatwg.org/#full-wildcard-regexp-value</summary>
    public const string FullWildcardRegexpValue = ".*";

    /// <summary>https://urlpattern.spec.whatwg.org/#tokenize</summary>
    public static List<UrlPatternToken> Tokenize(string inputString, bool lenient)
    {
        var input = new CodePointString(inputString);
        var tokens = new List<UrlPatternToken>();
        var index = 0;
        int nextIndex;
        int codePoint;

        while (index < input.Length)
        {
            SeekAndGetNextCodePoint(index);
            if (codePoint == '*')
            {
                AddTokenWithDefaultPositionAndLength(UrlPatternTokenType.Asterisk);
                continue;
            }

            if (codePoint is '+' or '?')
            {
                AddTokenWithDefaultPositionAndLength(UrlPatternTokenType.OtherModifier);
                continue;
            }

            if (codePoint == '\\')
            {
                if (index == input.Length - 1)
                {
                    ProcessTokenizingError(nextIndex, index);
                    continue;
                }

                var escapedIndex = nextIndex;
                GetNextCodePoint();
                AddTokenWithDefaultLength(UrlPatternTokenType.EscapedChar, nextIndex, escapedIndex);
                continue;
            }

            if (codePoint == '{')
            {
                AddTokenWithDefaultPositionAndLength(UrlPatternTokenType.Open);
                continue;
            }

            if (codePoint == '}')
            {
                AddTokenWithDefaultPositionAndLength(UrlPatternTokenType.Close);
                continue;
            }

            if (codePoint == ':')
            {
                var namePosition = nextIndex;
                var nameStart = namePosition;
                while (namePosition < input.Length)
                {
                    SeekAndGetNextCodePoint(namePosition);
                    if (!IsValidNameCodePoint(codePoint, first: namePosition == nameStart))
                        break;

                    namePosition = nextIndex;
                }

                if (namePosition <= nameStart)
                {
                    ProcessTokenizingError(nameStart, index);
                    continue;
                }

                AddTokenWithDefaultLength(UrlPatternTokenType.Name, namePosition, nameStart);
                continue;
            }

            if (codePoint == '(')
            {
                var depth = 1;
                var regexpPosition = nextIndex;
                var regexpStart = regexpPosition;
                var error = false;
                while (regexpPosition < input.Length)
                {
                    SeekAndGetNextCodePoint(regexpPosition);
                    if (codePoint > 0x7F)
                    {
                        ProcessTokenizingError(regexpStart, index);
                        error = true;
                        break;
                    }

                    if (regexpPosition == regexpStart && codePoint == '?')
                    {
                        ProcessTokenizingError(regexpStart, index);
                        error = true;
                        break;
                    }

                    if (codePoint == '\\')
                    {
                        if (regexpPosition == input.Length - 1)
                        {
                            ProcessTokenizingError(regexpStart, index);
                            error = true;
                            break;
                        }

                        GetNextCodePoint();
                        if (codePoint > 0x7F)
                        {
                            ProcessTokenizingError(regexpStart, index);
                            error = true;
                            break;
                        }

                        regexpPosition = nextIndex;
                        continue;
                    }

                    if (codePoint == ')')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            regexpPosition = nextIndex;
                            break;
                        }
                    }
                    else if (codePoint == '(')
                    {
                        depth++;
                        if (regexpPosition == input.Length - 1)
                        {
                            ProcessTokenizingError(regexpStart, index);
                            error = true;
                            break;
                        }

                        var temporaryPosition = nextIndex;
                        GetNextCodePoint();
                        if (codePoint != '?')
                        {
                            ProcessTokenizingError(regexpStart, index);
                            error = true;
                            break;
                        }

                        nextIndex = temporaryPosition;
                    }

                    regexpPosition = nextIndex;
                }

                if (error)
                    continue;

                if (depth != 0)
                {
                    ProcessTokenizingError(regexpStart, index);
                    continue;
                }

                var regexpLength = regexpPosition - regexpStart - 1;
                if (regexpLength == 0)
                {
                    ProcessTokenizingError(regexpStart, index);
                    continue;
                }

                AddToken(UrlPatternTokenType.Regexp, regexpPosition, regexpStart, regexpLength);
                continue;
            }

            AddTokenWithDefaultPositionAndLength(UrlPatternTokenType.Char);
        }

        AddTokenWithDefaultLength(UrlPatternTokenType.End, index, index);
        return tokens;

        void GetNextCodePoint()
        {
            codePoint = input[nextIndex];
            nextIndex++;
        }

        void SeekAndGetNextCodePoint(int position)
        {
            nextIndex = position;
            GetNextCodePoint();
        }

        void AddToken(UrlPatternTokenType type, int nextPosition, int valuePosition, int valueLength)
        {
            tokens.Add(new UrlPatternToken(type, index, input.Substring(valuePosition, valueLength)));
            index = nextPosition;
        }

        void AddTokenWithDefaultLength(UrlPatternTokenType type, int nextPosition, int valuePosition) => AddToken(type, nextPosition, valuePosition, nextPosition - valuePosition);

        void AddTokenWithDefaultPositionAndLength(UrlPatternTokenType type) => AddTokenWithDefaultLength(type, nextIndex, index);

        void ProcessTokenizingError(int nextPosition, int valuePosition)
        {
            if (!lenient)
                throw new TypeErrorException("Invalid URL pattern: unexpected character at index " + valuePosition.ToString(CultureInfo.InvariantCulture));

            AddTokenWithDefaultLength(UrlPatternTokenType.InvalidChar, nextPosition, valuePosition);
        }
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#is-a-valid-name-code-point (IdentifierStart and IdentifierPart of ECMAScript)</summary>
    public static bool IsValidNameCodePoint(int codePoint, bool first)
    {
        if (first)
            return codePoint is '$' or '_' || UnicodeProperties.IsIdStart(codePoint);

        return codePoint is '$' or 0x200C or 0x200D || UnicodeProperties.IsIdContinue(codePoint);
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#parse-a-pattern-string</summary>
    public static List<UrlPatternPart> ParsePatternString(string input, UrlPatternCompileOptions options, Func<string, string> encodingCallback)
    {
        var tokens = Tokenize(input, lenient: false);
        var segmentWildcardRegexp = GenerateSegmentWildcardRegexp(options);
        var parts = new List<UrlPatternPart>();
        var pendingFixedValue = new StringBuilder();
        var index = 0;
        var nextNumericName = 0;

        while (index < tokens.Count)
        {
            var charToken = TryConsumeToken(UrlPatternTokenType.Char);
            var nameToken = TryConsumeToken(UrlPatternTokenType.Name);
            var regexpOrWildcardToken = TryConsumeRegexpOrWildcardToken(nameToken);
            if (nameToken is not null || regexpOrWildcardToken is not null)
            {
                var prefix = charToken?.Value ?? "";
                if (prefix.Length > 0 && prefix != options.PrefixCodePoint)
                {
                    pendingFixedValue.Append(prefix);
                    prefix = "";
                }

                MaybeAddPartFromPendingFixedValue();
                var modifierToken = TryConsumeModifierToken();
                AddPart(prefix, nameToken, regexpOrWildcardToken, "", modifierToken);
                continue;
            }

            var fixedToken = charToken ?? TryConsumeToken(UrlPatternTokenType.EscapedChar);
            if (fixedToken is not null)
            {
                pendingFixedValue.Append(fixedToken.Value.Value);
                continue;
            }

            var openToken = TryConsumeToken(UrlPatternTokenType.Open);
            if (openToken is not null)
            {
                var prefix = ConsumeText();
                nameToken = TryConsumeToken(UrlPatternTokenType.Name);
                regexpOrWildcardToken = TryConsumeRegexpOrWildcardToken(nameToken);
                var suffix = ConsumeText();
                ConsumeRequiredToken(UrlPatternTokenType.Close);
                var modifierToken = TryConsumeModifierToken();
                AddPart(prefix, nameToken, regexpOrWildcardToken, suffix, modifierToken);
                continue;
            }

            MaybeAddPartFromPendingFixedValue();
            ConsumeRequiredToken(UrlPatternTokenType.End);
        }

        return parts;

        UrlPatternToken? TryConsumeToken(UrlPatternTokenType type)
        {
            var nextToken = tokens[index];
            if (nextToken.Type != type)
                return null;

            index++;
            return nextToken;
        }

        UrlPatternToken? TryConsumeModifierToken() => TryConsumeToken(UrlPatternTokenType.OtherModifier) ?? TryConsumeToken(UrlPatternTokenType.Asterisk);

        UrlPatternToken? TryConsumeRegexpOrWildcardToken(UrlPatternToken? nameToken)
        {
            var token = TryConsumeToken(UrlPatternTokenType.Regexp);
            if (nameToken is null && token is null)
            {
                token = TryConsumeToken(UrlPatternTokenType.Asterisk);
            }

            return token;
        }

        void ConsumeRequiredToken(UrlPatternTokenType type)
        {
            if (TryConsumeToken(type) is null)
                throw new TypeErrorException("Invalid URL pattern: unexpected token at index " + tokens[index].Index.ToString(CultureInfo.InvariantCulture));
        }

        string ConsumeText()
        {
            var result = new StringBuilder();
            while (true)
            {
                var token = TryConsumeToken(UrlPatternTokenType.Char) ?? TryConsumeToken(UrlPatternTokenType.EscapedChar);
                if (token is null)
                    break;

                result.Append(token.Value.Value);
            }

            return result.ToString();
        }

        void MaybeAddPartFromPendingFixedValue()
        {
            if (pendingFixedValue.Length == 0)
                return;

            var encodedValue = encodingCallback(pendingFixedValue.ToString());
            pendingFixedValue.Clear();
            parts.Add(new UrlPatternPart(UrlPatternPartType.FixedText, encodedValue, UrlPatternPartModifier.None));
        }

        void AddPart(string prefix, UrlPatternToken? nameToken, UrlPatternToken? regexpOrWildcardToken, string suffix, UrlPatternToken? modifierToken)
        {
            var modifier = modifierToken?.Value switch
            {
                "?" => UrlPatternPartModifier.Optional,
                "*" => UrlPatternPartModifier.ZeroOrMore,
                "+" => UrlPatternPartModifier.OneOrMore,
                _ => UrlPatternPartModifier.None,
            };

            if (nameToken is null && regexpOrWildcardToken is null && modifier == UrlPatternPartModifier.None)
            {
                // A "{foo}" grouping
                pendingFixedValue.Append(prefix);
                return;
            }

            MaybeAddPartFromPendingFixedValue();
            if (nameToken is null && regexpOrWildcardToken is null)
            {
                // A "{foo}?" grouping
                if (prefix.Length == 0)
                    return;

                parts.Add(new UrlPatternPart(UrlPatternPartType.FixedText, encodingCallback(prefix), modifier));
                return;
            }

            string regexpValue;
            if (regexpOrWildcardToken is null)
            {
                regexpValue = segmentWildcardRegexp;
            }
            else if (regexpOrWildcardToken.Value.Type == UrlPatternTokenType.Asterisk)
            {
                regexpValue = FullWildcardRegexpValue;
            }
            else
            {
                regexpValue = regexpOrWildcardToken.Value.Value;
            }

            var type = UrlPatternPartType.Regexp;
            if (regexpValue == segmentWildcardRegexp)
            {
                type = UrlPatternPartType.SegmentWildcard;
                regexpValue = "";
            }
            else if (regexpValue == FullWildcardRegexpValue)
            {
                type = UrlPatternPartType.FullWildcard;
                regexpValue = "";
            }

            var name = "";
            if (nameToken is not null)
            {
                name = nameToken.Value.Value;
            }
            else if (regexpOrWildcardToken is not null)
            {
                name = nextNumericName.ToString(CultureInfo.InvariantCulture);
                nextNumericName++;
            }

            if (parts.Exists(part => part.Name == name))
                throw new TypeErrorException($"Invalid URL pattern: duplicate group name '{name}'");

            var encodedPrefix = encodingCallback(prefix);
            var encodedSuffix = encodingCallback(suffix);
            parts.Add(new UrlPatternPart(type, regexpValue, modifier, name, encodedPrefix, encodedSuffix));
        }
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#generate-a-segment-wildcard-regexp</summary>
    public static string GenerateSegmentWildcardRegexp(UrlPatternCompileOptions options) => "[^" + EscapeRegexpString(options.DelimiterCodePoint) + "]+?";

    /// <summary>https://urlpattern.spec.whatwg.org/#generate-a-regular-expression-and-name-list</summary>
    public static (string RegularExpression, List<string> NameList) GenerateRegularExpressionAndNameList(List<UrlPatternPart> partList, UrlPatternCompileOptions options)
    {
        var result = new StringBuilder("^");
        var nameList = new List<string>();
        foreach (var part in partList)
        {
            if (part.Type == UrlPatternPartType.FixedText)
            {
                if (part.Modifier == UrlPatternPartModifier.None)
                {
                    result.Append(EscapeRegexpString(part.Value));
                }
                else
                {
                    result.Append("(?:").Append(EscapeRegexpString(part.Value)).Append(')').Append(ModifierToString(part.Modifier));
                }

                continue;
            }

            nameList.Add(part.Name);
            var regexpValue = part.Type switch
            {
                UrlPatternPartType.SegmentWildcard => GenerateSegmentWildcardRegexp(options),
                UrlPatternPartType.FullWildcard => FullWildcardRegexpValue,
                _ => part.Value,
            };

            if (part.Prefix.Length == 0 && part.Suffix.Length == 0)
            {
                if (part.Modifier is UrlPatternPartModifier.None or UrlPatternPartModifier.Optional)
                {
                    result.Append('(').Append(regexpValue).Append(')').Append(ModifierToString(part.Modifier));
                }
                else
                {
                    result.Append("((?:").Append(regexpValue).Append(')').Append(ModifierToString(part.Modifier)).Append(')');
                }

                continue;
            }

            if (part.Modifier is UrlPatternPartModifier.None or UrlPatternPartModifier.Optional)
            {
                result.Append("(?:").Append(EscapeRegexpString(part.Prefix)).Append('(').Append(regexpValue).Append(')').Append(EscapeRegexpString(part.Suffix)).Append(')').Append(ModifierToString(part.Modifier));
                continue;
            }

            result.Append("(?:").Append(EscapeRegexpString(part.Prefix))
                .Append("((?:").Append(regexpValue)
                .Append(")(?:").Append(EscapeRegexpString(part.Suffix)).Append(EscapeRegexpString(part.Prefix))
                .Append("(?:").Append(regexpValue)
                .Append("))*)").Append(EscapeRegexpString(part.Suffix))
                .Append(')');
            if (part.Modifier == UrlPatternPartModifier.ZeroOrMore)
            {
                result.Append('?');
            }
        }

        result.Append('$');
        return (result.ToString(), nameList);
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#escape-a-regexp-string</summary>
    public static string EscapeRegexpString(string input)
    {
        var result = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (c is '.' or '+' or '*' or '?' or '^' or '$' or '{' or '}' or '(' or ')' or '[' or ']' or '|' or '/' or '\\')
            {
                result.Append('\\');
            }

            result.Append(c);
        }

        return result.ToString();
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#generate-a-pattern-string</summary>
    public static string GeneratePatternString(List<UrlPatternPart> partList, UrlPatternCompileOptions options)
    {
        var result = new StringBuilder();
        for (var index = 0; index < partList.Count; index++)
        {
            var part = partList[index];
            var previousPart = index > 0 ? partList[index - 1] : null;
            var nextPart = index < partList.Count - 1 ? partList[index + 1] : null;
            if (part.Type == UrlPatternPartType.FixedText)
            {
                if (part.Modifier == UrlPatternPartModifier.None)
                {
                    result.Append(EscapePatternString(part.Value));
                    continue;
                }

                result.Append('{').Append(EscapePatternString(part.Value)).Append('}').Append(ModifierToString(part.Modifier));
                continue;
            }

            var customName = !char.IsAsciiDigit(part.Name[0]);
            var needsGrouping = part.Suffix.Length > 0 || (part.Prefix.Length > 0 && part.Prefix != options.PrefixCodePoint);
            if (!needsGrouping && customName && part.Type == UrlPatternPartType.SegmentWildcard && part.Modifier == UrlPatternPartModifier.None
                && nextPart is not null && nextPart.Prefix.Length == 0 && nextPart.Suffix.Length == 0)
            {
                if (nextPart.Type == UrlPatternPartType.FixedText)
                {
                    needsGrouping = IsValidNameCodePoint(FirstCodePoint(nextPart.Value), first: false);
                }
                else
                {
                    needsGrouping = char.IsAsciiDigit(nextPart.Name[0]);
                }
            }

            if (!needsGrouping && part.Prefix.Length == 0 && previousPart is not null && previousPart.Type == UrlPatternPartType.FixedText
                && previousPart.Value.Length > 0 && options.PrefixCodePoint.Length > 0 && LastCodePoint(previousPart.Value) == options.PrefixCodePoint[0])
            {
                needsGrouping = true;
            }

            if (needsGrouping)
            {
                result.Append('{');
            }

            result.Append(EscapePatternString(part.Prefix));
            if (customName)
            {
                result.Append(':').Append(part.Name);
            }

            if (part.Type == UrlPatternPartType.Regexp)
            {
                result.Append('(').Append(part.Value).Append(')');
            }
            else if (part.Type == UrlPatternPartType.SegmentWildcard && !customName)
            {
                result.Append('(').Append(GenerateSegmentWildcardRegexp(options)).Append(')');
            }
            else if (part.Type == UrlPatternPartType.FullWildcard)
            {
                if (!customName && (previousPart is null || previousPart.Type == UrlPatternPartType.FixedText || previousPart.Modifier != UrlPatternPartModifier.None || needsGrouping || part.Prefix.Length > 0))
                {
                    result.Append('*');
                }
                else
                {
                    result.Append('(').Append(FullWildcardRegexpValue).Append(')');
                }
            }

            if (part.Type == UrlPatternPartType.SegmentWildcard && customName && part.Suffix.Length > 0 && IsValidNameCodePoint(FirstCodePoint(part.Suffix), first: false))
            {
                result.Append('\\');
            }

            result.Append(EscapePatternString(part.Suffix));
            if (needsGrouping)
            {
                result.Append('}');
            }

            result.Append(ModifierToString(part.Modifier));
        }

        return result.ToString();

        static int FirstCodePoint(string value) => value.Length == 0 ? -1 : char.ConvertToUtf32(value, 0);

        static int LastCodePoint(string value) => value.Length >= 2 && char.IsLowSurrogate(value[^1]) && char.IsHighSurrogate(value[^2]) ? char.ConvertToUtf32(value[^2], value[^1]) : value[^1];
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#escape-a-pattern-string</summary>
    public static string EscapePatternString(string input)
    {
        var result = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (c is '+' or '*' or '?' or ':' or '{' or '}' or '(' or ')' or '\\')
            {
                result.Append('\\');
            }

            result.Append(c);
        }

        return result.ToString();
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#convert-a-modifier-to-a-string</summary>
    private static string ModifierToString(UrlPatternPartModifier modifier) => modifier switch
    {
        UrlPatternPartModifier.ZeroOrMore => "*",
        UrlPatternPartModifier.Optional => "?",
        UrlPatternPartModifier.OneOrMore => "+",
        _ => "",
    };
}
