using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Urls;

/// <summary>A mutable URLPatternInit (https://urlpattern.spec.whatwg.org/#dictdef-urlpatterninit): a null member does not exist.</summary>
internal sealed class UrlPatternInitRecord
{
    public string? Protocol { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? Hostname { get; set; }

    public string? Port { get; set; }

    public string? Pathname { get; set; }

    public string? Search { get; set; }

    public string? Hash { get; set; }

    public string? BaseUrl { get; set; }

    public static UrlPatternInitRecord From(UrlPatternInit init) => new()
    {
        Protocol = ToScalarValueString(init.Protocol),
        Username = ToScalarValueString(init.Username),
        Password = ToScalarValueString(init.Password),
        Hostname = ToScalarValueString(init.Hostname),
        Port = ToScalarValueString(init.Port),
        Pathname = ToScalarValueString(init.Pathname),
        Search = ToScalarValueString(init.Search),
        Hash = ToScalarValueString(init.Hash),
        BaseUrl = ToScalarValueString(init.BaseUrl),
    };

    private static string? ToScalarValueString(string? value) => value is null ? null : InfraString.ToScalarValueString(value);

    public string? this[UrlPatternComponentName name]
    {
        get => name switch
        {
            UrlPatternComponentName.Protocol => Protocol,
            UrlPatternComponentName.Username => Username,
            UrlPatternComponentName.Password => Password,
            UrlPatternComponentName.Hostname => Hostname,
            UrlPatternComponentName.Port => Port,
            UrlPatternComponentName.Pathname => Pathname,
            UrlPatternComponentName.Search => Search,
            _ => Hash,
        };
        set
        {
            switch (name)
            {
                case UrlPatternComponentName.Protocol:
                    Protocol = value;
                    break;
                case UrlPatternComponentName.Username:
                    Username = value;
                    break;
                case UrlPatternComponentName.Password:
                    Password = value;
                    break;
                case UrlPatternComponentName.Hostname:
                    Hostname = value;
                    break;
                case UrlPatternComponentName.Port:
                    Port = value;
                    break;
                case UrlPatternComponentName.Pathname:
                    Pathname = value;
                    break;
                case UrlPatternComponentName.Search:
                    Search = value;
                    break;
                default:
                    Hash = value;
                    break;
            }
        }
    }
}

internal enum UrlPatternComponentName
{
    Protocol,
    Username,
    Password,
    Hostname,
    Port,
    Pathname,
    Search,
    Hash,
}

/// <summary>https://urlpattern.spec.whatwg.org/#component</summary>
internal sealed class UrlPatternComponent
{
    public required string PatternString { get; init; }

    public required Regex RegularExpression { get; init; }

    public required string[] GroupNameList { get; init; }

    public required bool HasRegExpGroups { get; init; }

    /// <summary>https://urlpattern.spec.whatwg.org/#compile-a-component</summary>
    public static UrlPatternComponent Compile(string input, Func<string, string> encodingCallback, UrlPatternCompileOptions options)
    {
        var partList = UrlPatternParser.ParsePatternString(input, options, encodingCallback);
        var (regularExpressionString, nameList) = UrlPatternParser.GenerateRegularExpressionAndNameList(partList, options);
        var regularExpression = EcmaScriptRegex.Create(regularExpressionString, options.IgnoreCase)
            ?? throw new TypeErrorException($"Invalid URL pattern '{input}': the regular expression is not valid");

        return new UrlPatternComponent
        {
            PatternString = UrlPatternParser.GeneratePatternString(partList, options),
            RegularExpression = regularExpression,
            GroupNameList = [.. nameList],
            HasRegExpGroups = partList.Exists(part => part.Type == UrlPatternPartType.Regexp),
        };
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#protocol-component-matches-a-special-scheme</summary>
    public bool MatchesSpecialScheme()
    {
        foreach (var scheme in (ReadOnlySpan<string>)["ftp", "file", "http", "https", "ws", "wss"])
        {
            if (RegularExpression.IsMatch(scheme))
                return true;
        }

        return false;
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#create-a-component-match-result</summary>
    public UrlPatternComponentResult CreateMatchResult(string input, Match execResult)
    {
        var groups = new Dictionary<string, string?>(GroupNameList.Length, StringComparer.Ordinal);
        for (var index = 1; index <= GroupNameList.Length; index++)
        {
            var group = execResult.Groups[index];
            groups[GroupNameList[index - 1]] = group.Success ? group.Value : null;
        }

        return new UrlPatternComponentResult { Input = input, Groups = groups };
    }
}

/// <summary>The constructor string parser (https://urlpattern.spec.whatwg.org/#constructor-string-parsing).</summary>
internal sealed class UrlPatternConstructorStringParser
{
    private enum State
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

    private readonly CodePointString _input;
    private readonly List<UrlPatternToken> _tokenList;
    private readonly UrlPatternInitRecord _result = new();
    private int _componentStart;
    private int _tokenIndex;
    private int _tokenIncrement = 1;
    private int _groupDepth;
    private int _hostnameIPv6BracketDepth;
    private bool _protocolMatchesSpecialSchemeFlag;
    private State _state = State.Init;

    private UrlPatternConstructorStringParser(string input)
    {
        _input = new CodePointString(input);
        _tokenList = UrlPatternParser.Tokenize(input, lenient: true);
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#parse-a-constructor-string</summary>
    public static UrlPatternInitRecord Parse(string input) => new UrlPatternConstructorStringParser(input).Parse();

    private UrlPatternInitRecord Parse()
    {
        while (_tokenIndex < _tokenList.Count)
        {
            _tokenIncrement = 1;
            if (_tokenList[_tokenIndex].Type == UrlPatternTokenType.End)
            {
                if (_state == State.Init)
                {
                    // A relative constructor string
                    Rewind();
                    if (IsHashPrefix())
                    {
                        ChangeState(State.Hash, 1);
                    }
                    else if (IsSearchPrefix())
                    {
                        ChangeState(State.Search, 1);
                    }
                    else
                    {
                        ChangeState(State.Pathname, 0);
                    }

                    _tokenIndex += _tokenIncrement;
                    continue;
                }

                if (_state == State.Authority)
                {
                    // No "@": there is no username or password
                    RewindAndSetState(State.Hostname);
                    _tokenIndex += _tokenIncrement;
                    continue;
                }

                ChangeState(State.Done, 0);
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
                case State.Init:
                    if (IsProtocolSuffix())
                    {
                        RewindAndSetState(State.Protocol);
                    }

                    break;

                case State.Protocol:
                    if (IsProtocolSuffix())
                    {
                        ComputeProtocolMatchesSpecialSchemeFlag();
                        var nextState = State.Pathname;
                        var skip = 1;
                        if (NextIsAuthoritySlashes())
                        {
                            nextState = State.Authority;
                            skip = 3;
                        }
                        else if (_protocolMatchesSpecialSchemeFlag)
                        {
                            nextState = State.Authority;
                        }

                        ChangeState(nextState, skip);
                    }

                    break;

                case State.Authority:
                    if (IsIdentityTerminator())
                    {
                        RewindAndSetState(State.Username);
                    }
                    else if (IsPathnameStart() || IsSearchPrefix() || IsHashPrefix())
                    {
                        RewindAndSetState(State.Hostname);
                    }

                    break;

                case State.Username:
                    if (IsPasswordPrefix())
                    {
                        ChangeState(State.Password, 1);
                    }
                    else if (IsIdentityTerminator())
                    {
                        ChangeState(State.Hostname, 1);
                    }

                    break;

                case State.Password:
                    if (IsIdentityTerminator())
                    {
                        ChangeState(State.Hostname, 1);
                    }

                    break;

                case State.Hostname:
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
                        ChangeState(State.Port, 1);
                    }
                    else if (IsPathnameStart())
                    {
                        ChangeState(State.Pathname, 0);
                    }
                    else if (IsSearchPrefix())
                    {
                        ChangeState(State.Search, 1);
                    }
                    else if (IsHashPrefix())
                    {
                        ChangeState(State.Hash, 1);
                    }

                    break;

                case State.Port:
                    if (IsPathnameStart())
                    {
                        ChangeState(State.Pathname, 0);
                    }
                    else if (IsSearchPrefix())
                    {
                        ChangeState(State.Search, 1);
                    }
                    else if (IsHashPrefix())
                    {
                        ChangeState(State.Hash, 1);
                    }

                    break;

                case State.Pathname:
                    if (IsSearchPrefix())
                    {
                        ChangeState(State.Search, 1);
                    }
                    else if (IsHashPrefix())
                    {
                        ChangeState(State.Hash, 1);
                    }

                    break;

                case State.Search:
                    if (IsHashPrefix())
                    {
                        ChangeState(State.Hash, 1);
                    }

                    break;
            }

            _tokenIndex += _tokenIncrement;
        }

        // A hostname without port: the default port
        if (_result.Hostname is not null && _result.Port is null)
        {
            _result.Port = "";
        }

        return _result;
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#change-state</summary>
    private void ChangeState(State newState, int skip)
    {
        if (_state is not State.Init and not State.Authority and not State.Done)
        {
            _result[ToComponentName(_state)] = MakeComponentString();
        }

        if (_state != State.Init && newState != State.Done)
        {
            if (_state is State.Protocol or State.Authority or State.Username or State.Password
                && newState is State.Port or State.Pathname or State.Search or State.Hash
                && _result.Hostname is null)
            {
                _result.Hostname = "";
            }

            if (_state is State.Protocol or State.Authority or State.Username or State.Password or State.Hostname or State.Port
                && newState is State.Search or State.Hash
                && _result.Pathname is null)
            {
                _result.Pathname = _protocolMatchesSpecialSchemeFlag ? "/" : "";
            }

            if (_state is State.Protocol or State.Authority or State.Username or State.Password or State.Hostname or State.Port or State.Pathname
                && newState == State.Hash
                && _result.Search is null)
            {
                _result.Search = "";
            }
        }

        _state = newState;
        _tokenIndex += skip;
        _componentStart = _tokenIndex;
        _tokenIncrement = 0;
    }

    private static UrlPatternComponentName ToComponentName(State state) => state switch
    {
        State.Protocol => UrlPatternComponentName.Protocol,
        State.Username => UrlPatternComponentName.Username,
        State.Password => UrlPatternComponentName.Password,
        State.Hostname => UrlPatternComponentName.Hostname,
        State.Port => UrlPatternComponentName.Port,
        State.Pathname => UrlPatternComponentName.Pathname,
        State.Search => UrlPatternComponentName.Search,
        State.Hash => UrlPatternComponentName.Hash,
        _ => throw new InvalidOperationException("The state has no component"),
    };

    private void Rewind()
    {
        _tokenIndex = _componentStart;
        _tokenIncrement = 0;
    }

    private void RewindAndSetState(State state)
    {
        Rewind();
        _state = state;
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#get-a-safe-token</summary>
    private UrlPatternToken GetSafeToken(int index) => index < _tokenList.Count ? _tokenList[index] : _tokenList[^1];

    /// <summary>https://urlpattern.spec.whatwg.org/#is-a-non-special-pattern-char</summary>
    private bool IsNonSpecialPatternChar(int index, string value)
    {
        var token = GetSafeToken(index);
        if (token.Value != value)
            return false;

        return token.Type is UrlPatternTokenType.Char or UrlPatternTokenType.EscapedChar or UrlPatternTokenType.InvalidChar;
    }

    private bool IsProtocolSuffix() => IsNonSpecialPatternChar(_tokenIndex, ":");

    private bool NextIsAuthoritySlashes() => IsNonSpecialPatternChar(_tokenIndex + 1, "/") && IsNonSpecialPatternChar(_tokenIndex + 2, "/");

    private bool IsIdentityTerminator() => IsNonSpecialPatternChar(_tokenIndex, "@");

    private bool IsPasswordPrefix() => IsNonSpecialPatternChar(_tokenIndex, ":");

    private bool IsPortPrefix() => IsNonSpecialPatternChar(_tokenIndex, ":");

    private bool IsPathnameStart() => IsNonSpecialPatternChar(_tokenIndex, "/");

    /// <summary>https://urlpattern.spec.whatwg.org/#is-a-search-prefix</summary>
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
        return previousToken.Type is not (UrlPatternTokenType.Name or UrlPatternTokenType.Regexp or UrlPatternTokenType.Close or UrlPatternTokenType.Asterisk);
    }

    private bool IsHashPrefix() => IsNonSpecialPatternChar(_tokenIndex, "#");

    private bool IsGroupOpen() => _tokenList[_tokenIndex].Type == UrlPatternTokenType.Open;

    private bool IsGroupClose() => _tokenList[_tokenIndex].Type == UrlPatternTokenType.Close;

    private bool IsIPv6Open() => IsNonSpecialPatternChar(_tokenIndex, "[");

    private bool IsIPv6Close() => IsNonSpecialPatternChar(_tokenIndex, "]");

    /// <summary>https://urlpattern.spec.whatwg.org/#make-a-component-string</summary>
    private string MakeComponentString()
    {
        var token = _tokenList[_tokenIndex];
        var componentStartToken = GetSafeToken(_componentStart);
        var componentStartInputIndex = componentStartToken.Index;
        var endIndex = token.Index;
        return _input.Substring(componentStartInputIndex, endIndex - componentStartInputIndex);
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#compute-protocol-matches-a-special-scheme-flag</summary>
    private void ComputeProtocolMatchesSpecialSchemeFlag()
    {
        var protocolString = MakeComponentString();
        var protocolComponent = UrlPatternComponent.Compile(protocolString, UrlPatternCanonicalization.CanonicalizeProtocol, UrlPatternCompileOptions.Default);
        if (protocolComponent.MatchesSpecialScheme())
        {
            _protocolMatchesSpecialSchemeFlag = true;
        }
    }
}

/// <summary>The encoding callbacks (https://urlpattern.spec.whatwg.org/#canon-encoding-callbacks) and URLPatternInit processing.</summary>
internal static class UrlPatternCanonicalization
{
    /// <summary>https://urlpattern.spec.whatwg.org/#create-a-dummy-url</summary>
    private static UrlRecord CreateDummyUrl() => UrlParser.Parse("https://dummy.invalid/", baseUrl: null)!;

    /// <summary>https://urlpattern.spec.whatwg.org/#canonicalize-a-protocol</summary>
    public static string CanonicalizeProtocol(string value)
    {
        if (value.Length == 0)
            return value;

        var parseResult = UrlParser.Parse(value + "://dummy.invalid/", baseUrl: null) ?? throw new TypeErrorException($"Invalid protocol '{value}'");
        return parseResult.Scheme;
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#canonicalize-a-username</summary>
    public static string CanonicalizeUsername(string value)
    {
        if (value.Length == 0)
            return value;

        // https://url.spec.whatwg.org/#set-the-username
        return PercentEncoding.Utf8PercentEncode(value, PercentEncodeSet.Userinfo);
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#canonicalize-a-password</summary>
    public static string CanonicalizePassword(string value)
    {
        if (value.Length == 0)
            return value;

        // https://url.spec.whatwg.org/#set-the-password
        return PercentEncoding.Utf8PercentEncode(value, PercentEncodeSet.Userinfo);
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#canonicalize-a-hostname</summary>
    public static string CanonicalizeHostname(string value)
    {
        if (value.Length == 0)
            return value;

        var dummyUrl = CreateDummyUrl();
        if (!UrlParser.ParseWithStateOverride(value, dummyUrl, UrlParserState.Hostname))
            throw new TypeErrorException($"Invalid hostname '{value}'");

        return dummyUrl.Host?.Serialized ?? "";
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#canonicalize-an-ipv6-hostname</summary>
    public static string CanonicalizeIPv6Hostname(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c) && c is not '[' and not ']' and not ':')
                throw new TypeErrorException($"Invalid IPv6 hostname '{value}'");

            result.Append(char.ToLowerInvariant(c));
        }

        return result.ToString();
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#canonicalize-a-port</summary>
    public static string CanonicalizePort(string portValue) => CanonicalizePort(portValue, protocolValue: null);

    /// <summary>https://urlpattern.spec.whatwg.org/#canonicalize-a-port</summary>
    public static string CanonicalizePort(string portValue, string? protocolValue)
    {
        if (portValue.Length == 0)
            return portValue;

        var dummyUrl = CreateDummyUrl();
        dummyUrl.Scheme = protocolValue ?? "";
        if (!UrlParser.ParseWithStateOverride(portValue, dummyUrl, UrlParserState.Port))
            throw new TypeErrorException($"Invalid port '{portValue}'");

        return dummyUrl.Port < 0 ? "" : dummyUrl.Port.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#canonicalize-a-pathname</summary>
    public static string CanonicalizePathname(string value)
    {
        if (value.Length == 0)
            return value;

        var leadingSlash = value[0] == '/';
        var modifiedValue = (leadingSlash ? "" : "/-") + value;
        var dummyUrl = CreateDummyUrl();
        dummyUrl.Path.Clear();
        UrlParser.ParseWithStateOverride(modifiedValue, dummyUrl, UrlParserState.PathStart);
        var result = dummyUrl.SerializePath();
        if (!leadingSlash)
        {
            // A ".." segment may remove the "-" segment of the prefix (e.g. "a/../.." is "/"): the substring from 2 is empty
            result = result.Length > 2 ? result[2..] : "";
        }

        return result;
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#canonicalize-an-opaque-pathname</summary>
    public static string CanonicalizeOpaquePathname(string value)
    {
        if (value.Length == 0)
            return value;

        var dummyUrl = CreateDummyUrl();
        dummyUrl.OpaquePath = "";
        if (!UrlParser.ParseWithStateOverride(value, dummyUrl, UrlParserState.OpaquePath))
            throw new TypeErrorException($"Invalid pathname '{value}'");

        return dummyUrl.SerializePath();
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#canonicalize-a-search</summary>
    public static string CanonicalizeSearch(string value)
    {
        if (value.Length == 0)
            return value;

        var dummyUrl = CreateDummyUrl();
        dummyUrl.Query = "";
        UrlParser.ParseWithStateOverride(value, dummyUrl, UrlParserState.Query);
        return dummyUrl.Query ?? "";
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#canonicalize-a-hash</summary>
    public static string CanonicalizeHash(string value)
    {
        if (value.Length == 0)
            return value;

        var dummyUrl = CreateDummyUrl();
        dummyUrl.Fragment = "";
        UrlParser.ParseWithStateOverride(value, dummyUrl, UrlParserState.Fragment);
        return dummyUrl.Fragment ?? "";
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#process-a-urlpatterninit</summary>
    public static UrlPatternInitRecord ProcessInit(UrlPatternInitRecord init, bool isPattern, string? protocol, string? username, string? password, string? hostname, string? port, string? pathname, string? search, string? hash)
    {
        var result = new UrlPatternInitRecord
        {
            Protocol = protocol,
            Username = username,
            Password = password,
            Hostname = hostname,
            Port = port,
            Pathname = pathname,
            Search = search,
            Hash = hash,
        };

        UrlRecord? baseUrl = null;
        if (init.BaseUrl is not null)
        {
            baseUrl = UrlParser.Parse(init.BaseUrl, baseUrl: null) ?? throw new TypeErrorException($"Invalid base URL '{init.BaseUrl}'");
            if (init.Protocol is null)
            {
                result.Protocol = ProcessBaseUrlString(baseUrl.Scheme, isPattern);
            }

            if (!isPattern && init.Protocol is null && init.Hostname is null && init.Port is null && init.Username is null)
            {
                result.Username = ProcessBaseUrlString(baseUrl.Username, isPattern);
            }

            if (!isPattern && init.Protocol is null && init.Hostname is null && init.Port is null && init.Username is null && init.Password is null)
            {
                result.Password = ProcessBaseUrlString(baseUrl.Password, isPattern);
            }

            if (init.Protocol is null && init.Hostname is null)
            {
                result.Hostname = ProcessBaseUrlString(baseUrl.Host?.Serialized ?? "", isPattern);
            }

            if (init.Protocol is null && init.Hostname is null && init.Port is null)
            {
                result.Port = baseUrl.Port < 0 ? "" : baseUrl.Port.ToString(CultureInfo.InvariantCulture);
            }

            if (init.Protocol is null && init.Hostname is null && init.Port is null && init.Pathname is null)
            {
                result.Pathname = ProcessBaseUrlString(baseUrl.SerializePath(), isPattern);
            }

            if (init.Protocol is null && init.Hostname is null && init.Port is null && init.Pathname is null && init.Search is null)
            {
                result.Search = ProcessBaseUrlString(baseUrl.Query ?? "", isPattern);
            }

            if (init.Protocol is null && init.Hostname is null && init.Port is null && init.Pathname is null && init.Search is null && init.Hash is null)
            {
                result.Hash = ProcessBaseUrlString(baseUrl.Fragment ?? "", isPattern);
            }
        }

        if (init.Protocol is not null)
        {
            // https://urlpattern.spec.whatwg.org/#process-protocol-for-init
            var strippedValue = init.Protocol.EndsWith(':', StringComparison.Ordinal) ? init.Protocol[..^1] : init.Protocol;
            result.Protocol = isPattern ? strippedValue : CanonicalizeProtocol(strippedValue);
        }

        if (init.Username is not null)
        {
            result.Username = isPattern ? init.Username : CanonicalizeUsername(init.Username);
        }

        if (init.Password is not null)
        {
            result.Password = isPattern ? init.Password : CanonicalizePassword(init.Password);
        }

        if (init.Hostname is not null)
        {
            result.Hostname = isPattern ? init.Hostname : CanonicalizeHostname(init.Hostname);
        }

        var resultProtocolString = result.Protocol ?? "";
        if (init.Port is not null)
        {
            result.Port = isPattern ? init.Port : CanonicalizePort(init.Port, resultProtocolString);
        }

        if (init.Pathname is not null)
        {
            result.Pathname = init.Pathname;
            if (baseUrl is not null && !baseUrl.HasOpaquePath && !IsAbsolutePathname(result.Pathname, isPattern))
            {
                var baseUrlPath = ProcessBaseUrlString(baseUrl.SerializePath(), isPattern);
                var slashIndex = baseUrlPath.LastIndexOf('/', StringComparison.Ordinal);
                if (slashIndex >= 0)
                {
                    result.Pathname = baseUrlPath[..(slashIndex + 1)] + result.Pathname;
                }
            }

            // https://urlpattern.spec.whatwg.org/#process-pathname-for-init
            if (!isPattern)
            {
                result.Pathname = resultProtocolString.Length == 0 || UrlSchemes.IsSpecial(resultProtocolString)
                    ? CanonicalizePathname(result.Pathname)
                    : CanonicalizeOpaquePathname(result.Pathname);
            }
        }

        if (init.Search is not null)
        {
            // https://urlpattern.spec.whatwg.org/#process-search-for-init
            var strippedValue = init.Search.StartsWith('?', StringComparison.Ordinal) ? init.Search[1..] : init.Search;
            result.Search = isPattern ? strippedValue : CanonicalizeSearch(strippedValue);
        }

        if (init.Hash is not null)
        {
            // https://urlpattern.spec.whatwg.org/#process-hash-for-init
            var strippedValue = init.Hash.StartsWith('#', StringComparison.Ordinal) ? init.Hash[1..] : init.Hash;
            result.Hash = isPattern ? strippedValue : CanonicalizeHash(strippedValue);
        }

        return result;
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#process-a-base-url-string</summary>
    private static string ProcessBaseUrlString(string input, bool isPattern) => isPattern ? UrlPatternParser.EscapePatternString(input) : input;

    /// <summary>https://urlpattern.spec.whatwg.org/#is-an-absolute-pathname</summary>
    private static bool IsAbsolutePathname(string input, bool isPattern)
    {
        if (input.Length == 0)
            return false;

        if (input[0] == '/')
            return true;

        if (!isPattern)
            return false;

        if (input.Length < 2)
            return false;

        return input[0] is '\\' or '{' && input[1] == '/';
    }
}
