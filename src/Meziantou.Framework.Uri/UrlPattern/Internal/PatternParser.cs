namespace Meziantou.Framework.UrlPatternInternal;

// https://urlpattern.spec.whatwg.org/#parsing

/// <summary>Parses a pattern string into a list of parts.</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#parsing">WHATWG URL Pattern Spec - Parsing</see>
/// </remarks>
internal sealed class PatternParser
{
    private readonly List<Token> _tokenList;
    private readonly Func<string, string> _encodingCallback;
    private readonly string _segmentWildcardRegexp;
    private readonly PatternOptions _options;
    private readonly List<Part> _partList;
    private readonly StringBuilder _pendingFixedValue;
    private int _index;
    private int _nextNumericName;

    /// <summary>
    /// The full wildcard regexp value is the string ".*".
    /// </summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#full-wildcard-regexp-value">WHATWG URL Pattern Spec - Full wildcard regexp value</see>
    /// </remarks>
    private const string FullWildcardRegexpValue = ".*";

    public PatternParser(List<Token> tokenList, Func<string, string> encodingCallback, PatternOptions options)
    {
        _tokenList = tokenList;
        _encodingCallback = encodingCallback;
        _options = options;
        _segmentWildcardRegexp = GenerateSegmentWildcardRegexp(options);
        _partList = [];
        _pendingFixedValue = new StringBuilder();
        _index = 0;
        _nextNumericName = 0;
    }

    /// <summary>Parses the token list into a part list.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#parse-a-pattern-string">WHATWG URL Pattern Spec - Parse a pattern string</see>
    /// </remarks>
    public List<Part> Parse()
    {
        while (_index < _tokenList.Count)
        {
            // Look for sequence: <prefix char><name><regexp><modifier>
            var charToken = TryConsumeToken(TokenType.Char);
            var nameToken = TryConsumeToken(TokenType.Name);
            var regexpOrWildcardToken = TryConsumeRegexpOrWildcardToken(nameToken);

            if (nameToken is not null || regexpOrWildcardToken is not null)
            {
                // If there is a matching group, we need to add the part immediately
                var prefix = "";
                if (charToken is not null)
                {
                    prefix = charToken.Value.Value;
                }

                if (!string.IsNullOrEmpty(prefix) && prefix != _options.PrefixCodePoint)
                {
                    _pendingFixedValue.Append(prefix);
                    prefix = "";
                }

                MaybeAddPartFromPendingFixedValue();
                var modifierToken = TryConsumeModifierToken();
                AddPart(prefix, nameToken, regexpOrWildcardToken, "", modifierToken);
                continue;
            }

            // If there was no matching group, buffer any fixed text
            var fixedToken = charToken;
            if (fixedToken is null)
            {
                fixedToken = TryConsumeToken(TokenType.EscapedChar);
            }

            if (fixedToken is not null)
            {
                _pendingFixedValue.Append(fixedToken.Value.Value);
                continue;
            }

            // Look for sequence: <open><char prefix><name><regexp><char suffix><close><modifier>
            var openToken = TryConsumeToken(TokenType.Open);
            if (openToken is not null)
            {
                var prefix2 = ConsumeText();
                nameToken = TryConsumeToken(TokenType.Name);
                regexpOrWildcardToken = TryConsumeRegexpOrWildcardToken(nameToken);
                var suffix = ConsumeText();
                ConsumeRequiredToken(TokenType.Close);
                var modifierToken = TryConsumeModifierToken();
                AddPart(prefix2, nameToken, regexpOrWildcardToken, suffix, modifierToken);
                continue;
            }

            MaybeAddPartFromPendingFixedValue();
            ConsumeRequiredToken(TokenType.End);
        }

        return _partList;
    }

    /// <summary>Generates the segment wildcard regexp for the given options.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#generate-a-segment-wildcard-regexp">WHATWG URL Pattern Spec - Generate a segment wildcard regexp</see>
    /// </remarks>
    private static string GenerateSegmentWildcardRegexp(PatternOptions options)
    {
        // Note: In JavaScript regex, [^] matches any character including newline.
        // In .NET regex, [^] is invalid. We use [\s\S] instead which has the same effect.
        if (string.IsNullOrEmpty(options.DelimiterCodePoint))
        {
            return "[\\s\\S]+?";
        }

        var result = "[^";
        result += EscapeRegexpString(options.DelimiterCodePoint);
        result += "]+?";
        return result;
    }

    /// <summary>Tries to consume a token of the specified type.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#try-to-consume-a-token">WHATWG URL Pattern Spec - Try to consume a token</see>
    /// </remarks>
    private Token? TryConsumeToken(TokenType type)
    {
        if (_index >= _tokenList.Count)
            return null;

        var nextToken = _tokenList[_index];
        if (nextToken.Type != type)
            return null;

        _index++;
        return nextToken;
    }

    /// <summary>Tries to consume a modifier token.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#try-to-consume-a-modifier-token">WHATWG URL Pattern Spec - Try to consume a modifier token</see>
    /// </remarks>
    private Token? TryConsumeModifierToken()
    {
        var token = TryConsumeToken(TokenType.OtherModifier);
        if (token is not null)
            return token;

        return TryConsumeToken(TokenType.Asterisk);
    }

    /// <summary>Tries to consume a regexp or wildcard token.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#try-to-consume-a-regexp-or-wildcard-token">WHATWG URL Pattern Spec - Try to consume a regexp or wildcard token</see>
    /// </remarks>
    private Token? TryConsumeRegexpOrWildcardToken(Token? nameToken)
    {
        var token = TryConsumeToken(TokenType.Regexp);
        if (nameToken is null && token is null)
        {
            token = TryConsumeToken(TokenType.Asterisk);
        }

        return token;
    }

    /// <summary>Consumes a required token.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#consume-a-required-token">WHATWG URL Pattern Spec - Consume a required token</see>
    /// </remarks>
    private Token ConsumeRequiredToken(TokenType type)
    {
        var result = TryConsumeToken(type);
        if (result is null)
        {
            throw new UrlPatternException($"Expected token of type {type}");
        }

        return result.Value;
    }

    /// <summary>Consumes text.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#consume-text">WHATWG URL Pattern Spec - Consume text</see>
    /// </remarks>
    private string ConsumeText()
    {
        var result = new StringBuilder();
        while (true)
        {
            var token = TryConsumeToken(TokenType.Char);
            if (token is null)
            {
                token = TryConsumeToken(TokenType.EscapedChar);
            }

            if (token is null)
                break;

            result.Append(token.Value.Value);
        }

        return result.ToString();
    }

    /// <summary>Maybe adds a part from the pending fixed value.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#maybe-add-a-part-from-the-pending-fixed-value">WHATWG URL Pattern Spec - Maybe add a part from the pending fixed value</see>
    /// </remarks>
    private void MaybeAddPartFromPendingFixedValue()
    {
        if (_pendingFixedValue.Length == 0)
            return;

        var encodedValue = _encodingCallback(_pendingFixedValue.ToString());
        _pendingFixedValue.Clear();
        var part = new Part(PartType.FixedText, encodedValue, PartModifier.None);
        _partList.Add(part);
    }

    /// <summary>Adds a part.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#add-a-part">WHATWG URL Pattern Spec - Add a part</see>
    /// </remarks>
    private void AddPart(string prefix, Token? nameToken, Token? regexpOrWildcardToken, string suffix, Token? modifierToken)
    {
        var modifier = GetModifier(modifierToken);
        MaybeAddPartFromPendingFixedValue();

        if (nameToken is null && regexpOrWildcardToken is null)
        {
            // This was a "{foo}?" grouping
            if (string.IsNullOrEmpty(suffix) && string.IsNullOrEmpty(prefix))
                return;

            if (!string.IsNullOrEmpty(prefix))
            {
                var encodedValue = _encodingCallback(prefix);
                var part = new Part(PartType.FixedText, encodedValue, modifier);
                _partList.Add(part);
            }

            return;
        }

        var regexpValue = "";
        if (regexpOrWildcardToken is null)
        {
            regexpValue = _segmentWildcardRegexp;
        }
        else if (regexpOrWildcardToken.Value.Type == TokenType.Asterisk)
        {
            regexpValue = FullWildcardRegexpValue;
        }
        else
        {
            regexpValue = regexpOrWildcardToken.Value.Value;
        }

        var type = PartType.Regexp;

        if (regexpValue == _segmentWildcardRegexp)
        {
            type = PartType.SegmentWildcard;
            regexpValue = "";
        }
        else if (regexpValue == FullWildcardRegexpValue)
        {
            type = PartType.FullWildcard;
            regexpValue = "";
        }

        var name = "";
        if (nameToken is not null)
        {
            name = nameToken.Value.Value;
        }
        else if (regexpOrWildcardToken is not null)
        {
            name = _nextNumericName.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _nextNumericName++;
        }

        if (IsDuplicateName(name))
        {
            throw new UrlPatternException($"Duplicate name '{name}'");
        }

        var encodedPrefix = _encodingCallback(prefix);
        var encodedSuffix = _encodingCallback(suffix);
        var newPart = new Part(type, regexpValue, modifier, name, encodedPrefix, encodedSuffix);
        _partList.Add(newPart);
    }

    /// <summary>Determines if a name is a duplicate.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#is-a-duplicate-name">WHATWG URL Pattern Spec - Is a duplicate name</see>
    /// </remarks>
    private bool IsDuplicateName(string name)
    {
        foreach (var part in _partList)
        {
            if (part.Name == name)
                return true;
        }

        return false;
    }

    /// <summary>Gets the modifier from a modifier token.</summary>
    private static PartModifier GetModifier(Token? modifierToken)
    {
        if (modifierToken is null)
            return PartModifier.None;

        return modifierToken.Value.Value switch
        {
            "?" => PartModifier.Optional,
            "*" => PartModifier.ZeroOrMore,
            "+" => PartModifier.OneOrMore,
            _ => PartModifier.None,
        };
    }

    /// <summary>Escapes a string for use in a regular expression.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#escape-a-regexp-string">WHATWG URL Pattern Spec - Escape a regexp string</see>
    /// </remarks>
    internal static string EscapeRegexpString(string input)
    {
        var result = new StringBuilder();
        foreach (var c in input)
        {
            if (IsRegexpSpecialChar(c))
            {
                result.Append('\\');
            }

            result.Append(c);
        }

        return result.ToString();
    }

    private static bool IsRegexpSpecialChar(char c)
    {
        return c is '.' or '+' or '*' or '?' or '^' or '$' or '{' or '}' or '(' or ')' or '[' or ']' or '|' or '/' or '\\';
    }

    /// <summary>Escapes a string for use in a pattern string.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#escape-a-pattern-string">WHATWG URL Pattern Spec - Escape a pattern string</see>
    /// </remarks>
    internal static string EscapePatternString(string input)
    {
        var result = new StringBuilder();
        foreach (var c in input)
        {
            if (IsPatternSpecialChar(c))
            {
                result.Append('\\');
            }

            result.Append(c);
        }

        return result.ToString();
    }

    private static bool IsPatternSpecialChar(char c)
    {
        return c is '+' or '*' or '?' or ':' or '{' or '}' or '(' or ')' or '\\';
    }
}
