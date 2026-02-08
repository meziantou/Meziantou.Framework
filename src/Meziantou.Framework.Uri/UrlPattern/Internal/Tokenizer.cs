namespace Meziantou.Framework.UrlPatternInternal;

// https://urlpattern.spec.whatwg.org/#tokenizing

/// <summary>Tokenizes a pattern string into a list of tokens.</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#tokenizing">WHATWG URL Pattern Spec - Tokenizing</see>
/// </remarks>
internal ref struct Tokenizer
{
    private readonly string _input;
    private readonly TokenizePolicy _policy;
    private readonly List<Token> _tokenList;
    private int _index;
    private int _nextIndex;
    private char _codePoint;

    public Tokenizer(string input, TokenizePolicy policy)
    {
        _input = input;
        _policy = policy;
        _tokenList = [];
        _index = 0;
        _nextIndex = 0;
        _codePoint = '\0';
    }

    /// <summary>Tokenizes the input pattern string.</summary>
    /// <returns>A list of tokens.</returns>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#tokenize">WHATWG URL Pattern Spec - Tokenize</see>
    /// </remarks>
    public List<Token> Tokenize()
    {
        // https://urlpattern.spec.whatwg.org/#tokenize
        while (_index < _input.Length)
        {
            SeekAndGetNextCodePoint(_index);

            // If tokenizer's code point is U+002A (*)
            if (_codePoint == '*')
            {
                AddTokenWithDefaultPositionAndLength(TokenType.Asterisk);
                continue;
            }

            // If tokenizer's code point is U+002B (+) or U+003F (?)
            if (_codePoint == '+' || _codePoint == '?')
            {
                AddTokenWithDefaultPositionAndLength(TokenType.OtherModifier);
                continue;
            }

            // If tokenizer's code point is U+005C (\)
            if (_codePoint == '\\')
            {
                if (_index == _input.Length - 1)
                {
                    ProcessTokenizingError(_nextIndex, _index);
                    continue;
                }

                var escapedIndex = _nextIndex;
                GetNextCodePoint();
                AddTokenWithDefaultLength(TokenType.EscapedChar, _nextIndex, escapedIndex);
                continue;
            }

            // If tokenizer's code point is U+007B ({)
            if (_codePoint == '{')
            {
                AddTokenWithDefaultPositionAndLength(TokenType.Open);
                continue;
            }

            // If tokenizer's code point is U+007D (})
            if (_codePoint == '}')
            {
                AddTokenWithDefaultPositionAndLength(TokenType.Close);
                continue;
            }

            // If tokenizer's code point is U+003A (:)
            if (_codePoint == ':')
            {
                var namePosition = _nextIndex;
                var nameStart = namePosition;

                while (namePosition < _input.Length)
                {
                    SeekAndGetNextCodePoint(namePosition);
                    var firstCodePoint = namePosition == nameStart;
                    var validCodePoint = IsValidNameCodePoint(_codePoint, firstCodePoint);
                    if (!validCodePoint)
                        break;

                    namePosition = _nextIndex;
                }

                if (namePosition <= nameStart)
                {
                    ProcessTokenizingError(nameStart, _index);
                    continue;
                }

                AddTokenWithDefaultLength(TokenType.Name, namePosition, nameStart);
                continue;
            }

            // If tokenizer's code point is U+0028 (()
            if (_codePoint == '(')
            {
                var depth = 1;
                var regexpPosition = _nextIndex;
                var regexpStart = regexpPosition;
                var error = false;

                while (regexpPosition < _input.Length)
                {
                    SeekAndGetNextCodePoint(regexpPosition);

                    // If tokenizer's code point is not an ASCII code point
                    if (!IsAscii(_codePoint))
                    {
                        ProcessTokenizingError(regexpStart, _index);
                        error = true;
                        break;
                    }

                    // If regexp position equals regexp start and tokenizer's code point is U+003F (?)
                    if (regexpPosition == regexpStart && _codePoint == '?')
                    {
                        ProcessTokenizingError(regexpStart, _index);
                        error = true;
                        break;
                    }

                    // If tokenizer's code point is U+005C (\)
                    if (_codePoint == '\\')
                    {
                        if (regexpPosition == _input.Length - 1)
                        {
                            ProcessTokenizingError(regexpStart, _index);
                            error = true;
                            break;
                        }

                        GetNextCodePoint();

                        if (!IsAscii(_codePoint))
                        {
                            ProcessTokenizingError(regexpStart, _index);
                            error = true;
                            break;
                        }

                        regexpPosition = _nextIndex;
                        continue;
                    }

                    // If tokenizer's code point is U+0029 ())
                    if (_codePoint == ')')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            regexpPosition = _nextIndex;
                            break;
                        }
                    }
                    // Otherwise if tokenizer's code point is U+0028 (()
                    else if (_codePoint == '(')
                    {
                        depth++;
                        if (regexpPosition == _input.Length - 1)
                        {
                            ProcessTokenizingError(regexpStart, _index);
                            error = true;
                            break;
                        }

                        var temporaryPosition = _nextIndex;
                        GetNextCodePoint();

                        if (_codePoint != '?')
                        {
                            ProcessTokenizingError(regexpStart, _index);
                            error = true;
                            break;
                        }

                        _nextIndex = temporaryPosition;
                    }

                    regexpPosition = _nextIndex;
                }

                if (error)
                    continue;

                if (depth != 0)
                {
                    ProcessTokenizingError(regexpStart, _index);
                    continue;
                }

                var regexpLength = regexpPosition - regexpStart - 1;

                if (regexpLength == 0)
                {
                    ProcessTokenizingError(regexpStart, _index);
                    continue;
                }

                AddToken(TokenType.Regexp, regexpPosition, regexpStart, regexpLength);
                continue;
            }

            // Run add a token with default position and length given tokenizer and "char"
            AddTokenWithDefaultPositionAndLength(TokenType.Char);
        }

        // Run add a token with default length given tokenizer, "end", tokenizer's index, and tokenizer's index
        AddTokenWithDefaultLength(TokenType.End, _index, _index);

        return _tokenList;
    }

    /// <summary>Gets the next code point.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#get-the-next-code-point">WHATWG URL Pattern Spec - Get the next code point</see>
    /// </remarks>
    private void GetNextCodePoint()
    {
        _codePoint = _input[_nextIndex];
        _nextIndex++;
    }

    /// <summary>Seeks to a specific position and gets the next code point.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#seek-and-get-the-next-code-point">WHATWG URL Pattern Spec - Seek and get the next code point</see>
    /// </remarks>
    private void SeekAndGetNextCodePoint(int index)
    {
        _nextIndex = index;
        GetNextCodePoint();
    }

    /// <summary>Adds a token to the token list.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#add-a-token">WHATWG URL Pattern Spec - Add a token</see>
    /// </remarks>
    private void AddToken(TokenType type, int nextPosition, int valuePosition, int valueLength)
    {
        var token = new Token(type, _index, _input.Substring(valuePosition, valueLength));
        _tokenList.Add(token);
        _index = nextPosition;
    }

    /// <summary>Adds a token with default length.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#add-a-token-with-default-length">WHATWG URL Pattern Spec - Add a token with default length</see>
    /// </remarks>
    private void AddTokenWithDefaultLength(TokenType type, int nextPosition, int valuePosition)
    {
        var computedLength = nextPosition - valuePosition;
        AddToken(type, nextPosition, valuePosition, computedLength);
    }

    /// <summary>Adds a token with default position and length.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#add-a-token-with-default-position-and-length">WHATWG URL Pattern Spec - Add a token with default position and length</see>
    /// </remarks>
    private void AddTokenWithDefaultPositionAndLength(TokenType type)
    {
        AddTokenWithDefaultLength(type, _nextIndex, _index);
    }

    /// <summary>Processes a tokenizing error.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#process-a-tokenizing-error">WHATWG URL Pattern Spec - Process a tokenizing error</see>
    /// </remarks>
    private void ProcessTokenizingError(int nextPosition, int valuePosition)
    {
        if (_policy == TokenizePolicy.Strict)
        {
            throw new UrlPatternException($"Invalid pattern: unexpected character at position {valuePosition}");
        }

        AddTokenWithDefaultLength(TokenType.InvalidChar, nextPosition, valuePosition);
    }

    /// <summary>Determines if a code point is valid for a name.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#is-a-valid-name-code-point">WHATWG URL Pattern Spec - Is a valid name code point</see>
    /// </remarks>
    private static bool IsValidNameCodePoint(char codePoint, bool first)
    {
        // If first is true return the result of checking if code point is contained in the IdentifierStart set of code points.
        // Otherwise return the result of checking if code point is contained in the IdentifierPart set of code points.
        // Simplified check using common identifier characters
        if (first)
        {
            return char.IsLetter(codePoint) || codePoint == '_' || codePoint == '$';
        }

        return char.IsLetterOrDigit(codePoint) || codePoint == '_' || codePoint == '$';
    }

    private static bool IsAscii(char codePoint) => codePoint <= 127;
}
