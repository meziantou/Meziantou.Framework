namespace Meziantou.Framework.UrlPatternInternal;

// https://urlpattern.spec.whatwg.org/#constructor-string-parsing

/// <summary>Parses a constructor string into a URLPatternInit dictionary.</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#constructor-string-parsing">WHATWG URL Pattern Spec - Constructor string parsing</see>
/// </remarks>
internal sealed class ConstructorStringParser
{
    private readonly string _input;
    private readonly List<Token> _tokenList;
    private readonly Dictionary<string, string> _result;
    private int _componentStart;
    private int _tokenIndex;
    private int _tokenIncrement;
    private int _groupDepth;
    private int _hostnameIPv6BracketDepth;
    private bool _protocolMatchesSpecialScheme;
    private ConstructorStringState _state;

    /// <summary>The constructor string parser states.</summary>
    private enum ConstructorStringState
    {
        Init,
        Protocol,
        Authority,
        Username,
        Password,
        Hostname,
        Port,
        Pathname,
        Search,
        Hash,
        Done,
    }

    /// <summary>
    /// Special schemes per URL Standard.
    /// </summary>
    private static readonly HashSet<string> SpecialSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ftp",
        "file",
        "http",
        "https",
        "ws",
        "wss",
    };

    public ConstructorStringParser(string input)
    {
        _input = input;
        var tokenizer = new Tokenizer(input, TokenizePolicy.Lenient);
        _tokenList = tokenizer.Tokenize();
        _result = [];
        _componentStart = 0;
        _tokenIndex = 0;
        _tokenIncrement = 1;
        _groupDepth = 0;
        _hostnameIPv6BracketDepth = 0;
        _protocolMatchesSpecialScheme = false;
        _state = ConstructorStringState.Init;
    }

    /// <summary>Parses the constructor string.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#parse-a-constructor-string">WHATWG URL Pattern Spec - Parse a constructor string</see>
    /// </remarks>
    public Dictionary<string, string> Parse()
    {
        while (_tokenIndex < _tokenList.Count)
        {
            _tokenIncrement = 1;

            if (_tokenList[_tokenIndex].Type == TokenType.End)
            {
                if (_state == ConstructorStringState.Init)
                {
                    Rewind();
                    if (IsHashPrefix())
                    {
                        ChangeState(ConstructorStringState.Hash, skip: 1);
                    }
                    else if (IsSearchPrefix())
                    {
                        ChangeState(ConstructorStringState.Search, skip: 1);
                    }
                    else
                    {
                        ChangeState(ConstructorStringState.Pathname, skip: 0);
                    }

                    _tokenIndex += _tokenIncrement;
                    continue;
                }

                if (_state == ConstructorStringState.Authority)
                {
                    Rewind();
                    ChangeState(ConstructorStringState.Hostname, skip: 0);
                    _tokenIndex += _tokenIncrement;
                    continue;
                }

                ChangeState(ConstructorStringState.Done, skip: 0);
                break;
            }

            if (IsGroupOpen())
            {
                _groupDepth++;
                _tokenIndex += _tokenIncrement;
                continue;
            }

            if (_groupDepth > 0)
            {
                if (IsGroupClose())
                {
                    _groupDepth--;
                }
                else
                {
                    _tokenIndex += _tokenIncrement;
                    continue;
                }
            }

            switch (_state)
            {
                case ConstructorStringState.Init:
                    if (IsProtocolSuffix())
                    {
                        RewindAndSetState(ConstructorStringState.Protocol);
                    }

                    break;

                case ConstructorStringState.Protocol:
                    if (IsProtocolSuffix())
                    {
                        ComputeProtocolMatchesSpecialScheme();
                        var nextState = ConstructorStringState.Pathname;
                        var skip = 1;

                        if (_protocolMatchesSpecialScheme)
                        {
                            _result["pathname"] = "/";
                        }

                        if (NextIsAuthoritySlashes())
                        {
                            nextState = ConstructorStringState.Authority;
                            skip = 3;
                        }
                        else if (_protocolMatchesSpecialScheme)
                        {
                            nextState = ConstructorStringState.Authority;
                        }

                        ChangeState(nextState, skip);
                    }

                    break;

                case ConstructorStringState.Authority:
                    if (IsIdentityTerminator())
                    {
                        RewindAndSetState(ConstructorStringState.Username);
                    }
                    else if (IsPathnameStart() || IsSearchPrefix() || IsHashPrefix())
                    {
                        RewindAndSetState(ConstructorStringState.Hostname);
                    }

                    break;

                case ConstructorStringState.Username:
                    if (IsPasswordPrefix())
                    {
                        ChangeState(ConstructorStringState.Password, skip: 1);
                    }
                    else if (IsIdentityTerminator())
                    {
                        ChangeState(ConstructorStringState.Hostname, skip: 1);
                    }

                    break;

                case ConstructorStringState.Password:
                    if (IsIdentityTerminator())
                    {
                        ChangeState(ConstructorStringState.Hostname, skip: 1);
                    }

                    break;

                case ConstructorStringState.Hostname:
                    if (IsIPv6Open())
                    {
                        _hostnameIPv6BracketDepth++;
                    }
                    else if (IsIPv6Close())
                    {
                        _hostnameIPv6BracketDepth--;
                    }
                    else if (IsPortPrefix() && _hostnameIPv6BracketDepth == 0)
                    {
                        ChangeState(ConstructorStringState.Port, skip: 1);
                    }
                    else if (IsPathnameStart())
                    {
                        ChangeState(ConstructorStringState.Pathname, skip: 0);
                    }
                    else if (IsSearchPrefix())
                    {
                        ChangeState(ConstructorStringState.Search, skip: 1);
                    }
                    else if (IsHashPrefix())
                    {
                        ChangeState(ConstructorStringState.Hash, skip: 1);
                    }

                    break;

                case ConstructorStringState.Port:
                    if (IsPathnameStart())
                    {
                        ChangeState(ConstructorStringState.Pathname, skip: 0);
                    }
                    else if (IsSearchPrefix())
                    {
                        ChangeState(ConstructorStringState.Search, skip: 1);
                    }
                    else if (IsHashPrefix())
                    {
                        ChangeState(ConstructorStringState.Hash, skip: 1);
                    }

                    break;

                case ConstructorStringState.Pathname:
                    if (IsSearchPrefix())
                    {
                        ChangeState(ConstructorStringState.Search, skip: 1);
                    }
                    else if (IsHashPrefix())
                    {
                        ChangeState(ConstructorStringState.Hash, skip: 1);
                    }

                    break;

                case ConstructorStringState.Search:
                    if (IsHashPrefix())
                    {
                        ChangeState(ConstructorStringState.Hash, skip: 1);
                    }

                    break;
            }

            _tokenIndex += _tokenIncrement;
        }

        // If parser's result contains "hostname" and not "port", set port to empty string
        if (_result.ContainsKey("hostname") && !_result.ContainsKey("port"))
        {
            _result["port"] = "";
        }

        return _result;
    }

    private void Rewind()
    {
        _tokenIndex = _componentStart;
        _tokenIncrement = 0;
    }

    private void RewindAndSetState(ConstructorStringState newState)
    {
        Rewind();
        _state = newState;
    }

    private void ChangeState(ConstructorStringState newState, int skip)
    {
        if (_state != ConstructorStringState.Init &&
            _state != ConstructorStringState.Authority &&
            _state != ConstructorStringState.Done)
        {
            _result[GetStateKey(_state)] = MakeComponentString();
        }

        if (_state != ConstructorStringState.Init && newState != ConstructorStringState.Done)
        {
            // Set hostname to empty string if transitioning from certain states
            if ((_state is ConstructorStringState.Protocol or ConstructorStringState.Authority or ConstructorStringState.Username or ConstructorStringState.Password) &&
                (newState is ConstructorStringState.Port or ConstructorStringState.Pathname or ConstructorStringState.Search or ConstructorStringState.Hash) &&
                !_result.ContainsKey("hostname"))
            {
                _result["hostname"] = "";
            }

            // Set pathname if transitioning to search or hash
            if ((_state is ConstructorStringState.Protocol or ConstructorStringState.Authority or ConstructorStringState.Username or ConstructorStringState.Password or ConstructorStringState.Hostname or ConstructorStringState.Port) &&
                (newState is ConstructorStringState.Search or ConstructorStringState.Hash) &&
                !_result.ContainsKey("pathname"))
            {
                if (_protocolMatchesSpecialScheme)
                {
                    _result["pathname"] = "/";
                }
                else
                {
                    _result["pathname"] = "";
                }
            }

            // Set search if transitioning to hash
            if ((_state is ConstructorStringState.Protocol or ConstructorStringState.Authority or ConstructorStringState.Username or ConstructorStringState.Password or ConstructorStringState.Hostname or ConstructorStringState.Port or ConstructorStringState.Pathname) &&
                newState == ConstructorStringState.Hash &&
                !_result.ContainsKey("search"))
            {
                _result["search"] = "";
            }
        }

        _state = newState;
        _tokenIndex += skip;
        _componentStart = _tokenIndex;
        _tokenIncrement = 0;
    }

    private string MakeComponentString()
    {
        var token = _tokenList[_tokenIndex];
        var componentStartToken = GetSafeToken(_componentStart);
        var startIndex = componentStartToken.Index;
        var endIndex = token.Index;
        return _input.Substring(startIndex, endIndex - startIndex);
    }

    private Token GetSafeToken(int index)
    {
        if (index < _tokenList.Count)
            return _tokenList[index];

        return _tokenList[^1];
    }

    private static string GetStateKey(ConstructorStringState state)
    {
        return state switch
        {
            ConstructorStringState.Protocol => "protocol",
            ConstructorStringState.Username => "username",
            ConstructorStringState.Password => "password",
            ConstructorStringState.Hostname => "hostname",
            ConstructorStringState.Port => "port",
            ConstructorStringState.Pathname => "pathname",
            ConstructorStringState.Search => "search",
            ConstructorStringState.Hash => "hash",
            _ => throw new InvalidOperationException($"Invalid state: {state}"),
        };
    }

    private bool IsNonSpecialPatternChar(int index, string value)
    {
        var token = GetSafeToken(index);
        if (token.Value != value)
            return false;

        return token.Type is TokenType.Char or TokenType.EscapedChar or TokenType.InvalidChar;
    }

    private bool IsProtocolSuffix() => IsNonSpecialPatternChar(_tokenIndex, ":");

    private bool IsIdentityTerminator() => IsNonSpecialPatternChar(_tokenIndex, "@");

    private bool IsPasswordPrefix() => IsNonSpecialPatternChar(_tokenIndex, ":");

    private bool IsPortPrefix() => IsNonSpecialPatternChar(_tokenIndex, ":");

    private bool IsPathnameStart() => IsNonSpecialPatternChar(_tokenIndex, "/");

    private bool IsSearchPrefix()
    {
        if (IsNonSpecialPatternChar(_tokenIndex, "?"))
            return true;

        if (_tokenList[_tokenIndex].Value != "?")
            return false;

        var previousIndex = _tokenIndex - 1;
        if (previousIndex < 0)
            return true;

        var previousToken = GetSafeToken(previousIndex);
        if (previousToken.Type is TokenType.Name or TokenType.Regexp or TokenType.Close or TokenType.Asterisk)
            return false;

        return true;
    }

    private bool IsHashPrefix() => IsNonSpecialPatternChar(_tokenIndex, "#");

    private bool IsGroupOpen() => _tokenList[_tokenIndex].Type == TokenType.Open;

    private bool IsGroupClose() => _tokenList[_tokenIndex].Type == TokenType.Close;

    private bool IsIPv6Open() => IsNonSpecialPatternChar(_tokenIndex, "[");

    private bool IsIPv6Close() => IsNonSpecialPatternChar(_tokenIndex, "]");

    private bool NextIsAuthoritySlashes()
    {
        return IsNonSpecialPatternChar(_tokenIndex + 1, "/") && IsNonSpecialPatternChar(_tokenIndex + 2, "/");
    }

    private void ComputeProtocolMatchesSpecialScheme()
    {
        var protocolString = MakeComponentString();

        // Compile a component to test against special schemes
        try
        {
            var component = UrlPatternComponent.Compile(protocolString, CanonicalizeProtocol, PatternOptions.Default);
            foreach (var scheme in SpecialSchemes)
            {
                if (component.RegularExpression.IsMatch(scheme))
                {
                    _protocolMatchesSpecialScheme = true;
                    return;
                }
            }
        }
        catch (UrlPatternException)
        {
            // If compilation fails, assume not a special scheme
        }

        _protocolMatchesSpecialScheme = false;
    }

    private static string CanonicalizeProtocol(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Protocol should be lowercased
        return value.ToLowerInvariant();
    }
}
