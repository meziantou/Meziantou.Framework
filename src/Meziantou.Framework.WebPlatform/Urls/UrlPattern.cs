using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A URL pattern (https://urlpattern.spec.whatwg.org/#urlpattern): matches URLs, or the components of URLs, against patterns
/// using the syntax of path-to-regexp (e.g. <c>/books/:id(\\d+)</c>, <c>https://*.example.com/*</c>).
/// </summary>
/// <seealso href="https://urlpattern.spec.whatwg.org/#urlpattern"/>
[IdlInterface("URLPattern", "https://urlpattern.spec.whatwg.org/#urlpattern")]
public sealed class UrlPattern
{
    private static readonly UrlPatternOptions DefaultOptions = new();

    private readonly UrlPatternComponent _protocol;
    private readonly UrlPatternComponent _username;
    private readonly UrlPatternComponent _password;
    private readonly UrlPatternComponent _hostname;
    private readonly UrlPatternComponent _port;
    private readonly UrlPatternComponent _pathname;
    private readonly UrlPatternComponent _search;
    private readonly UrlPatternComponent _hash;

    /// <summary>Creates a pattern from a pattern string, relative to a base URL, or from a <see cref="UrlPatternInit"/> (then <paramref name="baseUrl"/> must not be given).</summary>
    /// <exception cref="TypeErrorException">The pattern is not valid.</exception>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-urlpattern"/>
    [IdlMember("constructor", "https://urlpattern.spec.whatwg.org/#dom-urlpattern-urlpattern")]
    public UrlPattern(UrlPatternInput input, string baseUrl, UrlPatternOptions? options = null)
        : this(input, baseUrl ?? throw new ArgumentNullException(nameof(baseUrl)), options, hasBaseUrl: true)
    {
    }

    /// <summary>Creates a pattern from a pattern string (an absolute URL pattern) or from a <see cref="UrlPatternInit"/> (the missing components match anything).</summary>
    /// <exception cref="TypeErrorException">The pattern is not valid.</exception>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-urlpattern-input-options"/>
    [IdlMember("constructor", "https://urlpattern.spec.whatwg.org/#dom-urlpattern-urlpattern-input-options")]
    public UrlPattern(UrlPatternInput input = default, UrlPatternOptions? options = null)
        : this(input, baseUrl: null, options, hasBaseUrl: false)
    {
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#url-pattern-create</summary>
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Disambiguates the constructors")]
    private UrlPattern(UrlPatternInput input, string? baseUrl, UrlPatternOptions? options, bool hasBaseUrl)
    {
        options ??= DefaultOptions;
        UrlPatternInitRecord init;
        if (input.Value is string inputString)
        {
            init = UrlPatternConstructorStringParser.Parse(InfraString.ToScalarValueString(inputString));
            if (baseUrl is null && init.Protocol is null)
                throw new TypeErrorException("A relative URL pattern requires a base URL");

            if (baseUrl is not null)
            {
                init.BaseUrl = InfraString.ToScalarValueString(baseUrl);
            }
        }
        else
        {
            if (baseUrl is not null)
                throw new TypeErrorException("The base URL must be given in the URLPatternInit");

            init = UrlPatternInitRecord.From(input.Init!);
        }

        var processedInit = UrlPatternCanonicalization.ProcessInit(init, isPattern: true, null, null, null, null, null, null, null, null);
        processedInit.Protocol ??= "*";
        processedInit.Username ??= "*";
        processedInit.Password ??= "*";
        processedInit.Hostname ??= "*";
        processedInit.Port ??= "*";
        processedInit.Pathname ??= "*";
        processedInit.Search ??= "*";
        processedInit.Hash ??= "*";

        if (UrlSchemes.GetDefaultPort(processedInit.Protocol) is >= 0 and var defaultPort && processedInit.Port == defaultPort.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            processedInit.Port = "";
        }

        _protocol = UrlPatternComponent.Compile(processedInit.Protocol, UrlPatternCanonicalization.CanonicalizeProtocol, UrlPatternCompileOptions.Default);
        _username = UrlPatternComponent.Compile(processedInit.Username, UrlPatternCanonicalization.CanonicalizeUsername, UrlPatternCompileOptions.Default);
        _password = UrlPatternComponent.Compile(processedInit.Password, UrlPatternCanonicalization.CanonicalizePassword, UrlPatternCompileOptions.Default);
        _hostname = IsIPv6HostnamePattern(processedInit.Hostname)
            ? UrlPatternComponent.Compile(processedInit.Hostname, UrlPatternCanonicalization.CanonicalizeIPv6Hostname, UrlPatternCompileOptions.Hostname)
            : UrlPatternComponent.Compile(processedInit.Hostname, UrlPatternCanonicalization.CanonicalizeHostname, UrlPatternCompileOptions.Hostname);
        _port = UrlPatternComponent.Compile(processedInit.Port, UrlPatternCanonicalization.CanonicalizePort, UrlPatternCompileOptions.Default);

        var compileOptions = UrlPatternCompileOptions.Default with { IgnoreCase = options.IgnoreCase };
        _pathname = _protocol.MatchesSpecialScheme()
            ? UrlPatternComponent.Compile(processedInit.Pathname, UrlPatternCanonicalization.CanonicalizePathname, UrlPatternCompileOptions.Pathname with { IgnoreCase = options.IgnoreCase })
            : UrlPatternComponent.Compile(processedInit.Pathname, UrlPatternCanonicalization.CanonicalizeOpaquePathname, compileOptions);
        _search = UrlPatternComponent.Compile(processedInit.Search, UrlPatternCanonicalization.CanonicalizeSearch, compileOptions);
        _hash = UrlPatternComponent.Compile(processedInit.Hash, UrlPatternCanonicalization.CanonicalizeHash, compileOptions);
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#hostname-pattern-is-an-ipv6-address</summary>
    private static bool IsIPv6HostnamePattern(string input)
    {
        if (input.Length < 2)
            return false;

        return input[0] == '[' || (input[0] is '{' or '\\' && input[1] == '[');
    }

    /// <summary>The normalized protocol pattern string.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-protocol"/>
    [IdlMember("protocol", "https://urlpattern.spec.whatwg.org/#dom-urlpattern-protocol")]
    public string Protocol => _protocol.PatternString;

    /// <summary>The normalized username pattern string.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-username"/>
    [IdlMember("username", "https://urlpattern.spec.whatwg.org/#dom-urlpattern-username")]
    public string Username => _username.PatternString;

    /// <summary>The normalized password pattern string.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-password"/>
    [IdlMember("password", "https://urlpattern.spec.whatwg.org/#dom-urlpattern-password")]
    public string Password => _password.PatternString;

    /// <summary>The normalized hostname pattern string.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-hostname"/>
    [IdlMember("hostname", "https://urlpattern.spec.whatwg.org/#dom-urlpattern-hostname")]
    public string Hostname => _hostname.PatternString;

    /// <summary>The normalized port pattern string.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-port"/>
    [IdlMember("port", "https://urlpattern.spec.whatwg.org/#dom-urlpattern-port")]
    public string Port => _port.PatternString;

    /// <summary>The normalized pathname pattern string.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-pathname"/>
    [IdlMember("pathname", "https://urlpattern.spec.whatwg.org/#dom-urlpattern-pathname")]
    public string Pathname => _pathname.PatternString;

    /// <summary>The normalized search pattern string.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-search"/>
    [IdlMember("search", "https://urlpattern.spec.whatwg.org/#dom-urlpattern-search")]
    public string Search => _search.PatternString;

    /// <summary>The normalized hash pattern string.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-hash"/>
    [IdlMember("hash", "https://urlpattern.spec.whatwg.org/#dom-urlpattern-hash")]
    public string Hash => _hash.PatternString;

    /// <summary>Whether a component of the pattern uses a custom regular expression (e.g. <c>:id(\\d+)</c>).</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-hasregexpgroups"/>
    [IdlMember("hasRegExpGroups", "https://urlpattern.spec.whatwg.org/#dom-urlpattern-hasregexpgroups")]
    public bool HasRegExpGroups => _protocol.HasRegExpGroups || _username.HasRegExpGroups || _password.HasRegExpGroups || _hostname.HasRegExpGroups
        || _port.HasRegExpGroups || _pathname.HasRegExpGroups || _search.HasRegExpGroups || _hash.HasRegExpGroups;

    /// <summary>Returns whether the pattern matches a URL (resolved against <paramref name="baseUrl"/>) or the components of a <see cref="UrlPatternInit"/>.</summary>
    /// <exception cref="TypeErrorException"><paramref name="baseUrl"/> is given with a <see cref="UrlPatternInit"/>.</exception>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-test"/>
    [IdlMember("test", "https://urlpattern.spec.whatwg.org/#dom-urlpattern-test")]
    public bool Test(UrlPatternInput input = default, string? baseUrl = null) => Match(input, baseUrl) is not null;

    /// <summary>Returns whether the pattern matches a URL.</summary>
    [NotInIdl]
    public bool Test(Url url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return Match(url.Record, url.Href) is not null;
    }

    /// <summary>Returns whether the pattern matches a URL record (https://urlpattern.spec.whatwg.org/#url-pattern-match).</summary>
    internal bool Test(UrlRecord url) => Match(url, url.Serialize()) is not null;

    /// <summary>
    /// https://urlpattern.spec.whatwg.org/#build-a-url-pattern-from-a-web-idl-value: a <see cref="UrlPattern"/> is returned
    /// as is, a pattern string is resolved against <paramref name="baseUrl"/>, and <paramref name="baseUrl"/> is the base URL
    /// of a <see cref="UrlPatternInit"/> that has none.
    /// </summary>
    /// <exception cref="TypeErrorException">The pattern is not valid.</exception>
    internal static UrlPattern FromWebIdlValue(UrlPatternCompatible input, UrlRecord baseUrl)
    {
        return input.Value switch
        {
            UrlPattern pattern => pattern,
            UrlPatternInit init => new UrlPattern(init.BaseUrl is null ? init.WithBaseUrl(baseUrl.Serialize()) : init),
            string value => new UrlPattern(value, baseUrl.Serialize()),
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>Matches a URL (resolved against <paramref name="baseUrl"/>) or the components of a <see cref="UrlPatternInit"/>, and returns the matched groups, or null if the pattern does not match.</summary>
    /// <exception cref="TypeErrorException"><paramref name="baseUrl"/> is given with a <see cref="UrlPatternInit"/>.</exception>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-exec"/>
    [IdlMember("exec", "https://urlpattern.spec.whatwg.org/#dom-urlpattern-exec")]
    public UrlPatternResult? Exec(UrlPatternInput input = default, string? baseUrl = null) => Match(input, baseUrl);

    /// <summary>Matches a URL and returns the matched groups, or null if the pattern does not match.</summary>
    [NotInIdl]
    public UrlPatternResult? Exec(Url url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return Match(url.Record, url.Href);
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#url-pattern-match</summary>
    private UrlPatternResult? Match(UrlPatternInput input, string? baseUrlString)
    {
        if (input.Value is string inputString)
        {
            List<UrlPatternInput> inputs = [input];
            UrlRecord? baseUrl = null;
            if (baseUrlString is not null)
            {
                baseUrl = UrlParser.Parse(InfraString.ToScalarValueString(baseUrlString), baseUrl: null);
                if (baseUrl is null)
                    return null;

                inputs.Add(baseUrlString);
            }

            var url = UrlParser.Parse(InfraString.ToScalarValueString(inputString), baseUrl);
            if (url is null)
                return null;

            return Match(inputs, url.Scheme, url.Username, url.Password, url.Host?.Serialized ?? "", url.Port < 0 ? "" : url.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), url.SerializePath(), url.Query ?? "", url.Fragment ?? "");
        }

        if (baseUrlString is not null)
            throw new TypeErrorException("The base URL must be given in the URLPatternInit");

        UrlPatternInitRecord applyResult;
        try
        {
            applyResult = UrlPatternCanonicalization.ProcessInit(UrlPatternInitRecord.From(input.Init!), isPattern: false, "", "", "", "", "", "", "", "");
        }
        catch (TypeErrorException)
        {
            return null;
        }

        return Match([input], applyResult.Protocol!, applyResult.Username!, applyResult.Password!, applyResult.Hostname!, applyResult.Port!, applyResult.Pathname!, applyResult.Search!, applyResult.Hash!);
    }

    private UrlPatternResult? Match(UrlRecord url, string serialization)
    {
        return Match([serialization], url.Scheme, url.Username, url.Password, url.Host?.Serialized ?? "", url.Port < 0 ? "" : url.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), url.SerializePath(), url.Query ?? "", url.Fragment ?? "");
    }

    private UrlPatternResult? Match(List<UrlPatternInput> inputs, string protocol, string username, string password, string hostname, string port, string pathname, string search, string hash)
    {
        // The matching has no side effects: it stops at the first component that does not match
        if (_protocol.RegularExpression.Match(protocol) is not { Success: true } protocolExecResult
            || _username.RegularExpression.Match(username) is not { Success: true } usernameExecResult
            || _password.RegularExpression.Match(password) is not { Success: true } passwordExecResult
            || _hostname.RegularExpression.Match(hostname) is not { Success: true } hostnameExecResult
            || _port.RegularExpression.Match(port) is not { Success: true } portExecResult
            || _pathname.RegularExpression.Match(pathname) is not { Success: true } pathnameExecResult
            || _search.RegularExpression.Match(search) is not { Success: true } searchExecResult
            || _hash.RegularExpression.Match(hash) is not { Success: true } hashExecResult)
        {
            return null;
        }

        return new UrlPatternResult
        {
            Inputs = inputs,
            Protocol = _protocol.CreateMatchResult(protocol, protocolExecResult),
            Username = _username.CreateMatchResult(username, usernameExecResult),
            Password = _password.CreateMatchResult(password, passwordExecResult),
            Hostname = _hostname.CreateMatchResult(hostname, hostnameExecResult),
            Port = _port.CreateMatchResult(port, portExecResult),
            Pathname = _pathname.CreateMatchResult(pathname, pathnameExecResult),
            Search = _search.CreateMatchResult(search, searchExecResult),
            Hash = _hash.CreateMatchResult(hash, hashExecResult),
        };
    }
}

/// <summary>The <c>(USVString or URLPatternInit)</c> union (https://urlpattern.spec.whatwg.org/#typedefdef-urlpatterninput).</summary>
public readonly struct UrlPatternInput : IUnionValue, IEquatable<UrlPatternInput>
{
    private readonly object? _value;

    private UrlPatternInput(object value) => _value = value;

    /// <summary>Gets the value: a <see cref="string"/> or a <see cref="UrlPatternInit"/> (an empty <see cref="UrlPatternInit"/> for the default value).</summary>
    [NotInIdl]
    public object Value => _value ?? UrlPatternInit.Empty;

    /// <summary>Gets the URL or pattern string, or null.</summary>
    [NotInIdl]
    public string? StringValue => _value as string;

    /// <summary>Gets the components, or null if the value is a string.</summary>
    [NotInIdl]
    public UrlPatternInit? Init => _value is null ? UrlPatternInit.Empty : _value as UrlPatternInit;

    /// <summary>Converts a string to an input.</summary>
    [NotInIdl]
    public static implicit operator UrlPatternInput(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts the components of a URL pattern to an input.</summary>
    [NotInIdl]
    public static implicit operator UrlPatternInput(UrlPatternInit value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates an input from a URL or pattern string.</summary>
    [NotInIdl]
    public static UrlPatternInput FromString(string value) => value;

    /// <summary>Creates an input from the components of a URL pattern or of a URL.</summary>
    [NotInIdl]
    public static UrlPatternInput FromUrlPatternInit(UrlPatternInit value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(UrlPatternInput other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is UrlPatternInput other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc/>
    [NotInIdl]
    public override string? ToString() => Value.ToString();

    /// <summary>Returns whether the two inputs hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(UrlPatternInput left, UrlPatternInput right) => left.Equals(right);

    /// <summary>Returns whether the two inputs hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(UrlPatternInput left, UrlPatternInput right) => !left.Equals(right);
}

/// <summary>
/// The <c>(USVString or URLPatternInit or URLPattern)</c> union (https://urlpattern.spec.whatwg.org/#typedefdef-urlpatterncompatible):
/// a URL pattern given to another API (e.g. the <c>urlPattern</c> condition of a service worker router rule).
/// </summary>
public readonly struct UrlPatternCompatible : IUnionValue, IEquatable<UrlPatternCompatible>
{
    private readonly object? _value;

    private UrlPatternCompatible(object value) => _value = value;

    /// <summary>Gets the value: a <see cref="string"/>, a <see cref="UrlPatternInit"/> or a <see cref="UrlPattern"/> (an empty <see cref="UrlPatternInit"/> for the default value).</summary>
    [NotInIdl]
    public object Value => _value ?? UrlPatternInit.Empty;

    /// <summary>Converts a pattern string to the union.</summary>
    [NotInIdl]
    public static implicit operator UrlPatternCompatible(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts the components of a URL pattern to the union.</summary>
    [NotInIdl]
    public static implicit operator UrlPatternCompatible(UrlPatternInit value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a URL pattern to the union.</summary>
    [NotInIdl]
    public static implicit operator UrlPatternCompatible(UrlPattern value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from a pattern string.</summary>
    [NotInIdl]
    public static UrlPatternCompatible FromString(string value) => value;

    /// <summary>Creates the union from the components of a URL pattern.</summary>
    [NotInIdl]
    public static UrlPatternCompatible FromUrlPatternInit(UrlPatternInit value) => value;

    /// <summary>Creates the union from a URL pattern.</summary>
    [NotInIdl]
    public static UrlPatternCompatible FromUrlPattern(UrlPattern value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(UrlPatternCompatible other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is UrlPatternCompatible other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc/>
    [NotInIdl]
    public override string? ToString() => Value.ToString();

    /// <summary>Returns whether the two unions hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(UrlPatternCompatible left, UrlPatternCompatible right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(UrlPatternCompatible left, UrlPatternCompatible right) => !left.Equals(right);
}

/// <summary>The components of a URL pattern or of a URL (https://urlpattern.spec.whatwg.org/#dictdef-urlpatterninit). A null member is not present.</summary>
/// <seealso href="https://urlpattern.spec.whatwg.org/#dictdef-urlpatterninit"/>
[IdlInterface("URLPatternInit", "https://urlpattern.spec.whatwg.org/#dictdef-urlpatterninit")]
public sealed class UrlPatternInit
{
    internal static UrlPatternInit Empty { get; } = new();

    /// <summary>Returns a copy of the components whose base URL is <paramref name="baseUrl"/>.</summary>
    internal UrlPatternInit WithBaseUrl(string baseUrl) => new()
    {
        Protocol = Protocol,
        Username = Username,
        Password = Password,
        Hostname = Hostname,
        Port = Port,
        Pathname = Pathname,
        Search = Search,
        Hash = Hash,
        BaseUrl = baseUrl,
    };

    /// <summary>Gets the <c>protocol</c> pattern or value, or null when it is not present.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-protocol"/>
    [IdlMember("protocol", "https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-protocol")]
    public string? Protocol { get; init; }

    /// <summary>Gets the <c>username</c> pattern or value, or null when it is not present.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-username"/>
    [IdlMember("username", "https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-username")]
    public string? Username { get; init; }

    /// <summary>Gets the <c>password</c> pattern or value, or null when it is not present.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-password"/>
    [IdlMember("password", "https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-password")]
    public string? Password { get; init; }

    /// <summary>Gets the <c>hostname</c> pattern or value, or null when it is not present.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-hostname"/>
    [IdlMember("hostname", "https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-hostname")]
    public string? Hostname { get; init; }

    /// <summary>Gets the <c>port</c> pattern or value, or null when it is not present.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-port"/>
    [IdlMember("port", "https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-port")]
    public string? Port { get; init; }

    /// <summary>Gets the <c>pathname</c> pattern or value, or null when it is not present.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-pathname"/>
    [IdlMember("pathname", "https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-pathname")]
    public string? Pathname { get; init; }

    /// <summary>Gets the <c>search</c> pattern or value, or null when it is not present.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-search"/>
    [IdlMember("search", "https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-search")]
    public string? Search { get; init; }

    /// <summary>Gets the <c>hash</c> pattern or value, or null when it is not present.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-hash"/>
    [IdlMember("hash", "https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-hash")]
    public string? Hash { get; init; }

    /// <summary>
    /// Gets the base URL against which the relative components are resolved, or null when it is not present.
    /// </summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-baseurl"/>
    [IdlMember("baseURL", "https://urlpattern.spec.whatwg.org/#dom-urlpatterninit-baseurl")]
    public string? BaseUrl { get; init; }
}

/// <summary>https://urlpattern.spec.whatwg.org/#dictdef-urlpatternoptions</summary>
/// <seealso href="https://urlpattern.spec.whatwg.org/#dictdef-urlpatternoptions"/>
[IdlInterface("URLPatternOptions", "https://urlpattern.spec.whatwg.org/#dictdef-urlpatternoptions")]
public sealed class UrlPatternOptions
{
    /// <summary>Whether the pathname, search and hash are matched case-insensitively.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatternoptions-ignorecase"/>
    [IdlMember("ignoreCase", "https://urlpattern.spec.whatwg.org/#dom-urlpatternoptions-ignorecase")]
    public bool IgnoreCase { get; init; }
}

/// <summary>The result of <see cref="UrlPattern.Exec(UrlPatternInput, string?)"/> (https://urlpattern.spec.whatwg.org/#dictdef-urlpatternresult).</summary>
/// <seealso href="https://urlpattern.spec.whatwg.org/#dictdef-urlpatternresult"/>
[IdlInterface("URLPatternResult", "https://urlpattern.spec.whatwg.org/#dictdef-urlpatternresult")]
public sealed class UrlPatternResult
{
    /// <summary>The inputs of the match: the URL or <see cref="UrlPatternInit"/>, followed by the base URL if any.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-inputs"/>
    [IdlMember("inputs", "https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-inputs")]
    public required IReadOnlyList<UrlPatternInput> Inputs { get; init; }

    /// <summary>Gets the match of the <c>protocol</c> component.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-protocol"/>
    [IdlMember("protocol", "https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-protocol")]
    public required UrlPatternComponentResult Protocol { get; init; }

    /// <summary>Gets the match of the <c>username</c> component.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-username"/>
    [IdlMember("username", "https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-username")]
    public required UrlPatternComponentResult Username { get; init; }

    /// <summary>Gets the match of the <c>password</c> component.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-password"/>
    [IdlMember("password", "https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-password")]
    public required UrlPatternComponentResult Password { get; init; }

    /// <summary>Gets the match of the <c>hostname</c> component.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-hostname"/>
    [IdlMember("hostname", "https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-hostname")]
    public required UrlPatternComponentResult Hostname { get; init; }

    /// <summary>Gets the match of the <c>port</c> component.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-port"/>
    [IdlMember("port", "https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-port")]
    public required UrlPatternComponentResult Port { get; init; }

    /// <summary>Gets the match of the <c>pathname</c> component.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-pathname"/>
    [IdlMember("pathname", "https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-pathname")]
    public required UrlPatternComponentResult Pathname { get; init; }

    /// <summary>Gets the match of the <c>search</c> component.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-search"/>
    [IdlMember("search", "https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-search")]
    public required UrlPatternComponentResult Search { get; init; }

    /// <summary>Gets the match of the <c>hash</c> component.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-hash"/>
    [IdlMember("hash", "https://urlpattern.spec.whatwg.org/#dom-urlpatternresult-hash")]
    public required UrlPatternComponentResult Hash { get; init; }
}

/// <summary>The match of a component (https://urlpattern.spec.whatwg.org/#dictdef-urlpatterncomponentresult).</summary>
/// <seealso href="https://urlpattern.spec.whatwg.org/#dictdef-urlpatterncomponentresult"/>
[IdlInterface("URLPatternComponentResult", "https://urlpattern.spec.whatwg.org/#dictdef-urlpatterncomponentresult")]
public sealed class UrlPatternComponentResult
{
    /// <summary>The value of the component that was matched.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatterncomponentresult-input"/>
    [IdlMember("input", "https://urlpattern.spec.whatwg.org/#dom-urlpatterncomponentresult-input")]
    public required string Input { get; init; }

    /// <summary>The values of the groups by name (the unnamed groups are named "0", "1", ...). The value of an optional group that did not match is null.</summary>
    /// <seealso href="https://urlpattern.spec.whatwg.org/#dom-urlpatterncomponentresult-groups"/>
    [IdlMember("groups", "https://urlpattern.spec.whatwg.org/#dom-urlpatterncomponentresult-groups")]
    public required IReadOnlyDictionary<string, string?> Groups { get; init; }
}
