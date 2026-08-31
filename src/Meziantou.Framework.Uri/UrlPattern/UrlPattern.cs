using System.Text.RegularExpressions;
using Meziantou.Framework.UrlPatternInternal;

namespace Meziantou.Framework;

/// <summary>Represents a URL pattern that can match URLs based on a convenient pattern syntax.</summary>
/// <remarks>
/// <para>The URL Pattern API provides a web platform primitive for matching URLs based on a convenient pattern syntax.</para>
/// <para>A URL pattern consists of several components (protocol, hostname, pathname, etc.), each of which represents a pattern that can be matched against the corresponding component of a URL.</para>
/// <see href="https://urlpattern.spec.whatwg.org/">WHATWG URL Pattern Spec</see>
/// <see href="https://developer.mozilla.org/en-US/docs/Web/API/URL_Pattern_API">MDN - URL Pattern API</see>
/// </remarks>
/// <example>
/// <code language="csharp">
/// // Create a pattern and test if a URL matches
/// var pattern = UrlPattern.Create("https://example.com/users/:id");
/// bool isMatch = pattern.IsMatch("https://example.com/users/123"); // true
///
/// // Extract captured groups from a URL
/// var result = pattern.Match("https://example.com/users/456");
/// string userId = result?.Pathname.Groups["id"]; // "456"
///
/// // Use wildcards to match any value
/// var wildcardPattern = UrlPattern.Create("https://*.example.com/*");
/// wildcardPattern.IsMatch("https://api.example.com/data"); // true
/// </code>
/// </example>
public sealed class UrlPattern
{
    private readonly UrlPatternComponent _protocolComponent;
    private readonly UrlPatternComponent _usernameComponent;
    private readonly UrlPatternComponent _passwordComponent;
    private readonly UrlPatternComponent _hostnameComponent;
    private readonly UrlPatternComponent _portComponent;
    private readonly UrlPatternComponent _pathnameComponent;
    private readonly UrlPatternComponent _searchComponent;
    private readonly UrlPatternComponent _hashComponent;

    private UrlPattern(
        UrlPatternComponent protocolComponent,
        UrlPatternComponent usernameComponent,
        UrlPatternComponent passwordComponent,
        UrlPatternComponent hostnameComponent,
        UrlPatternComponent portComponent,
        UrlPatternComponent pathnameComponent,
        UrlPatternComponent searchComponent,
        UrlPatternComponent hashComponent)
    {
        _protocolComponent = protocolComponent;
        _usernameComponent = usernameComponent;
        _passwordComponent = passwordComponent;
        _hostnameComponent = hostnameComponent;
        _portComponent = portComponent;
        _pathnameComponent = pathnameComponent;
        _searchComponent = searchComponent;
        _hashComponent = hashComponent;
    }

    /// <summary>Gets the normalized protocol pattern string.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-protocol">WHATWG URL Pattern Spec - protocol getter</see>
    /// </remarks>
    public string Protocol => _protocolComponent.PatternString;

    /// <summary>Gets the normalized username pattern string.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-username">WHATWG URL Pattern Spec - username getter</see>
    /// </remarks>
    public string Username => _usernameComponent.PatternString;

    /// <summary>Gets the normalized password pattern string.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-password">WHATWG URL Pattern Spec - password getter</see>
    /// </remarks>
    public string Password => _passwordComponent.PatternString;

    /// <summary>Gets the normalized hostname pattern string.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-hostname">WHATWG URL Pattern Spec - hostname getter</see>
    /// </remarks>
    public string Hostname => _hostnameComponent.PatternString;

    /// <summary>Gets the normalized port pattern string.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-port">WHATWG URL Pattern Spec - port getter</see>
    /// </remarks>
    public string Port => _portComponent.PatternString;

    /// <summary>Gets the normalized pathname pattern string.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-pathname">WHATWG URL Pattern Spec - pathname getter</see>
    /// </remarks>
    public string Pathname => _pathnameComponent.PatternString;

    /// <summary>Gets the normalized search pattern string.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-search">WHATWG URL Pattern Spec - search getter</see>
    /// </remarks>
    public string Search => _searchComponent.PatternString;

    /// <summary>Gets the normalized hash pattern string.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-hash">WHATWG URL Pattern Spec - hash getter</see>
    /// </remarks>
    public string Hash => _hashComponent.PatternString;

    /// <summary>Gets whether this pattern contains one or more groups which use regular expression matching.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-hasregexpgroups">WHATWG URL Pattern Spec - hasRegExpGroups getter</see>
    /// </remarks>
    public bool HasRegExpGroups =>
        _protocolComponent.HasRegexpGroups ||
        _usernameComponent.HasRegexpGroups ||
        _passwordComponent.HasRegexpGroups ||
        _hostnameComponent.HasRegexpGroups ||
        _portComponent.HasRegexpGroups ||
        _pathnameComponent.HasRegexpGroups ||
        _searchComponent.HasRegexpGroups ||
        _hashComponent.HasRegexpGroups;

    /// <summary>Creates a new URLPattern from a pattern string.</summary>
    /// <param name="pattern">A pattern string using the URL pattern syntax.</param>
    /// <returns>A new URLPattern.</returns>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-urlpattern">WHATWG URL Pattern Spec - URLPattern constructor</see>
    /// <see href="https://developer.mozilla.org/en-US/docs/Web/API/URLPattern/URLPattern">MDN - URLPattern constructor</see>
    /// </remarks>
    public static UrlPattern Create(string pattern)
    {
        return Create(pattern, baseUrl: null, options: null);
    }

    /// <summary>Creates a new URLPattern from a pattern string with options.</summary>
    /// <param name="pattern">A pattern string using the URL pattern syntax.</param>
    /// <param name="options">Options for pattern matching.</param>
    /// <returns>A new URLPattern.</returns>
    public static UrlPattern Create(string pattern, UrlPatternOptions? options)
    {
        return Create(pattern, baseUrl: null, options);
    }

    /// <summary>Creates a new URLPattern from a pattern string and base URL.</summary>
    /// <param name="pattern">A pattern string using the URL pattern syntax.</param>
    /// <param name="baseUrl">The base URL to use for relative patterns.</param>
    /// <returns>A new URLPattern.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public static UrlPattern Create(string pattern, string? baseUrl)
    {
        return Create(pattern, baseUrl, options: null);
    }

    /// <summary>Creates a new URLPattern from a pattern string, base URL, and options.</summary>
    /// <param name="pattern">A pattern string using the URL pattern syntax.</param>
    /// <param name="baseUrl">The base URL to use for relative patterns.</param>
    /// <param name="options">Options for pattern matching.</param>
    /// <returns>A new URLPattern.</returns>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-urlpattern">WHATWG URL Pattern Spec - URLPattern constructor</see>
    /// </remarks>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public static UrlPattern Create(string pattern, string? baseUrl, UrlPatternOptions? options)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        // Parse the constructor string
        var parser = new ConstructorStringParser(pattern);
        var init = parser.Parse();

        if (baseUrl is null && init.Protocol is null)
        {
            throw new UrlPatternException("A base URL must be provided when the pattern does not specify a protocol.");
        }

        if (baseUrl is not null)
        {
            init.BaseUrl = baseUrl;
        }

        return Create(init, options);
    }

    /// <summary>Creates a new URLPattern from a URLPatternInit dictionary.</summary>
    /// <param name="init">A dictionary containing patterns for each URL component.</param>
    /// <returns>A new URLPattern.</returns>
    public static UrlPattern Create(UrlPatternInit init)
    {
        return Create(init, options: null);
    }

    /// <summary>Creates a new URLPattern from a URLPatternInit dictionary and options.</summary>
    /// <param name="init">A dictionary containing patterns for each URL component.</param>
    /// <param name="options">Options for pattern matching.</param>
    /// <returns>A new URLPattern.</returns>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#create">WHATWG URL Pattern Spec - Create</see>
    /// </remarks>
    public static UrlPattern Create(UrlPatternInit init, UrlPatternOptions? options)
    {
        ArgumentNullException.ThrowIfNull(init);
        options ??= new UrlPatternOptions();

        var processedInit = ProcessUrlPatternInit(init, isPattern: true);

        // Default missing components to wildcard
        processedInit.Protocol ??= "*";
        processedInit.Username ??= "*";
        processedInit.Password ??= "*";
        processedInit.Hostname ??= "*";
        processedInit.Port ??= "*";
        processedInit.Pathname ??= "*";
        processedInit.Search ??= "*";
        processedInit.Hash ??= "*";

        // If protocol is a special scheme and port matches its default port, set port to empty string
        if (SpecialSchemes.Contains(processedInit.Protocol) &&
            SpecialSchemes.TryGetDefaultPort(processedInit.Protocol, out var defaultPort) &&
            processedInit.Port == defaultPort)
        {
            processedInit.Port = "";
        }

        var ignoreCase = options.IgnoreCase;

        // Compile components
        var protocolComponent = UrlPatternComponent.Compile(processedInit.Protocol, CanonicalizeProtocol, PatternOptions.Default);
        var usernameComponent = UrlPatternComponent.Compile(processedInit.Username, CanonicalizeUsername, PatternOptions.Default);
        var passwordComponent = UrlPatternComponent.Compile(processedInit.Password, CanonicalizePassword, PatternOptions.Default);

        UrlPatternComponent hostnameComponent;
        if (IsIPv6Hostname(processedInit.Hostname))
        {
            hostnameComponent = UrlPatternComponent.Compile(processedInit.Hostname, CanonicalizeIPv6Hostname, PatternOptions.Hostname);
        }
        else
        {
            hostnameComponent = UrlPatternComponent.Compile(processedInit.Hostname, CanonicalizeHostname, PatternOptions.Hostname);
        }

        var portComponent = UrlPatternComponent.Compile(processedInit.Port, CanonicalizePort, PatternOptions.Default);

        var compileOptions = PatternOptions.Default.WithIgnoreCase(ignoreCase);
        var pathCompileOptions = PatternOptions.Pathname.WithIgnoreCase(ignoreCase);

        // The spec canonicalizes a pathname by percent-encoding it, which this implementation does not do,
        // so the pathname is matched as written and needs no encoding callback. It must not gain a leading
        // "/" here: the callback runs on every fixed-text part, so that would insert a separator in front of
        // any literal following a group, turning "/books/:id.json" into "/books/:id/.json". The spec avoids
        // the same trap by prefixing "/-" before parsing and stripping it afterwards. Only the compile
        // options differ between an opaque path and a special-scheme path.
        var pathnameOptions = ProtocolMatchesSpecialScheme(protocolComponent) ? pathCompileOptions : compileOptions;
        var pathnameComponent = UrlPatternComponent.Compile(processedInit.Pathname, encodingCallback: null, pathnameOptions);

        var searchComponent = UrlPatternComponent.Compile(processedInit.Search, CanonicalizeSearch, compileOptions);
        var hashComponent = UrlPatternComponent.Compile(processedInit.Hash, CanonicalizeHash, compileOptions);

        return new UrlPattern(
            protocolComponent,
            usernameComponent,
            passwordComponent,
            hostnameComponent,
            portComponent,
            pathnameComponent,
            searchComponent,
            hashComponent);
    }

    /// <summary>Indicates whether the pattern finds a match in the specified URL.</summary>
    /// <param name="url">The URL string to test.</param>
    /// <returns><see langword="true"/> if the pattern matches the URL; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-test">WHATWG URL Pattern Spec - test method</see>
    /// <see href="https://developer.mozilla.org/en-US/docs/Web/API/URLPattern/test">MDN - URLPattern.test()</see>
    /// </remarks>
    /// <exception cref="RegexMatchTimeoutException">A component pattern took more than one second to match.</exception>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public bool IsMatch(string url)
    {
        return IsMatch(url, baseUrl: null);
    }

    /// <summary>Indicates whether the pattern finds a match in the specified URL with a base URL.</summary>
    /// <param name="url">The URL string to test.</param>
    /// <param name="baseUrl">The base URL to use for resolving relative URLs.</param>
    /// <returns><see langword="true"/> if the pattern matches the URL; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="RegexMatchTimeoutException">A component pattern took more than one second to match.</exception>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public bool IsMatch(string url, string? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(url);

        var uri = ParseUrl(url, baseUrl);
        if (uri is null)
            return false;

        return IsMatchUrl(uri);
    }

    /// <summary>Indicates whether the pattern finds a match in the specified URL.</summary>
    /// <param name="url">The URL to test.</param>
    /// <returns><see langword="true"/> if the pattern matches the URL; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="RegexMatchTimeoutException">A component pattern took more than one second to match.</exception>
    public bool IsMatch(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return IsMatchUrl(url);
    }

    /// <summary>Indicates whether the pattern finds a match in the specified URL input.</summary>
    /// <param name="input">The URL input dictionary to test.</param>
    /// <returns><see langword="true"/> if the pattern matches the input; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="RegexMatchTimeoutException">A component pattern took more than one second to match.</exception>
    public bool IsMatch(UrlPatternInit input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var processed = ProcessUrlPatternInit(input, isPattern: false);
        return IsMatchInit(processed);
    }

    /// <summary>Searches the specified URL for the first occurrence of the pattern and returns the match result with captured groups.</summary>
    /// <param name="url">The URL string to match.</param>
    /// <returns>A <see cref="UrlPatternResult"/> containing the match result, or <see langword="null"/> if no match.</returns>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-exec">WHATWG URL Pattern Spec - exec method</see>
    /// <see href="https://developer.mozilla.org/en-US/docs/Web/API/URLPattern/exec">MDN - URLPattern.exec()</see>
    /// </remarks>
    /// <exception cref="RegexMatchTimeoutException">A component pattern took more than one second to match.</exception>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public UrlPatternResult? Match(string url)
    {
        return Match(url, baseUrl: null);
    }

    /// <summary>Searches the specified URL with a base URL for the first occurrence of the pattern and returns the match result with captured groups.</summary>
    /// <param name="url">The URL string to match.</param>
    /// <param name="baseUrl">The base URL to use for resolving relative URLs.</param>
    /// <returns>A <see cref="UrlPatternResult"/> containing the match result, or <see langword="null"/> if no match.</returns>
    /// <exception cref="RegexMatchTimeoutException">A component pattern took more than one second to match.</exception>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public UrlPatternResult? Match(string url, string? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(url);

        var uri = ParseUrl(url, baseUrl);
        if (uri is null)
            return null;

        return MatchUrl(uri, url);
    }

    /// <summary>Searches the specified URL for the first occurrence of the pattern and returns the match result with captured groups.</summary>
    /// <param name="url">The URL to match.</param>
    /// <returns>A <see cref="UrlPatternResult"/> containing the match result, or <see langword="null"/> if no match.</returns>
    /// <exception cref="RegexMatchTimeoutException">A component pattern took more than one second to match.</exception>
    public UrlPatternResult? Match(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return MatchUrl(url, url.ToString());
    }

    /// <summary>Searches the specified URL input for the first occurrence of the pattern and returns the match result with captured groups.</summary>
    /// <param name="input">The URL input dictionary to match.</param>
    /// <returns>A <see cref="UrlPatternResult"/> containing the match result, or <see langword="null"/> if no match.</returns>
    /// <exception cref="RegexMatchTimeoutException">A component pattern took more than one second to match.</exception>
    public UrlPatternResult? Match(UrlPatternInit input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var processed = ProcessUrlPatternInit(input, isPattern: false);
        return MatchInit(processed);
    }

    private UrlPatternResult? MatchUrl(Uri url, string originalInput)
    {
        var (protocol, username, password, hostname, port, pathname, search, hash) = GetUrlComponents(url);

        return MatchComponents(protocol, username, password, hostname, port, pathname, search, hash, originalInput);
    }

    /// <summary>Splits the URL into the eight component values that are matched against the pattern.</summary>
    private static (string Protocol, string Username, string Password, string Hostname, string Port, string Pathname, string Search, string Hash) GetUrlComponents(Uri url)
    {
        var userInfo = url.UserInfo;
        var separatorIndex = userInfo.IndexOf(':', StringComparison.Ordinal);
        var username = separatorIndex == -1 ? userInfo : userInfo[..separatorIndex];
        var password = separatorIndex == -1 ? "" : userInfo[(separatorIndex + 1)..];

        return (
            url.Scheme,
            Uri.UnescapeDataString(username),
            Uri.UnescapeDataString(password),
            url.Host,
            url.IsDefaultPort ? "" : url.Port.ToString(CultureInfo.InvariantCulture),
            url.AbsolutePath,
            url.Query.TrimStart('?'),
            url.Fragment.TrimStart('#'));
    }

    private UrlPatternResult? MatchInit(UrlPatternInit init)
    {
        var (protocol, username, password, hostname, port, pathname, search, hash) = CanonicalizeInitComponents(init);

        return MatchComponents(protocol, username, password, hostname, port, pathname, search, hash, originalInput: null, originalInit: init);
    }

    /// <summary>Canonicalizes the components of an init used as a match input.</summary>
    /// <remarks>
    /// The pattern is canonicalized when it is compiled, so an init used as an input has to be
    /// canonicalized the same way for the two to be comparable. A <see cref="Uri"/> input already
    /// arrives canonical, which is why <see cref="MatchUrl"/> does not go through this.
    /// <see href="https://urlpattern.spec.whatwg.org/#canon-processing-for-init">WHATWG URL Pattern Spec - URLPatternInit processing</see>
    /// </remarks>
    private static (string Protocol, string Username, string Password, string Hostname, string Port, string Pathname, string Search, string Hash) CanonicalizeInitComponents(UrlPatternInit init)
    {
        return (
            CanonicalizeProtocol(init.Protocol ?? ""),
            init.Username ?? "",
            init.Password ?? "",
            CanonicalizeHostname(init.Hostname ?? ""),
            init.Port ?? "",
            init.Pathname ?? "",
            CanonicalizeSearch(init.Search ?? ""),
            CanonicalizeHash(init.Hash ?? ""));
    }

    private UrlPatternResult? MatchComponents(string protocol, string username, string password, string hostname, string port, string pathname, string search, string hash, string? originalInput, UrlPatternInit? originalInit = null)
    {
        var protocolMatch = _protocolComponent.RegularExpression.Match(protocol);
        if (!protocolMatch.Success)
            return null;

        var usernameMatch = _usernameComponent.RegularExpression.Match(username);
        if (!usernameMatch.Success)
            return null;

        var passwordMatch = _passwordComponent.RegularExpression.Match(password);
        if (!passwordMatch.Success)
            return null;

        var hostnameMatch = _hostnameComponent.RegularExpression.Match(hostname);
        if (!hostnameMatch.Success)
            return null;

        var portMatch = _portComponent.RegularExpression.Match(port);
        if (!portMatch.Success)
            return null;

        var pathnameMatch = _pathnameComponent.RegularExpression.Match(pathname);
        if (!pathnameMatch.Success)
            return null;

        var searchMatch = _searchComponent.RegularExpression.Match(search);
        if (!searchMatch.Success)
            return null;

        var hashMatch = _hashComponent.RegularExpression.Match(hash);
        if (!hashMatch.Success)
            return null;

        // The result reports the input as it was supplied, not as it was canonicalized for matching
        var input = originalInput is not null
            ? new UrlPatternInput(originalInput)
            : new UrlPatternInput(originalInit ?? new UrlPatternInit
            {
                Protocol = protocol,
                Username = username,
                Password = password,
                Hostname = hostname,
                Port = port,
                Pathname = pathname,
                Search = search,
                Hash = hash,
            });

        return new UrlPatternResult(
            [input],
            CreateComponentResult(protocol, protocolMatch, _protocolComponent.GroupNameList),
            CreateComponentResult(username, usernameMatch, _usernameComponent.GroupNameList),
            CreateComponentResult(password, passwordMatch, _passwordComponent.GroupNameList),
            CreateComponentResult(hostname, hostnameMatch, _hostnameComponent.GroupNameList),
            CreateComponentResult(port, portMatch, _portComponent.GroupNameList),
            CreateComponentResult(pathname, pathnameMatch, _pathnameComponent.GroupNameList),
            CreateComponentResult(search, searchMatch, _searchComponent.GroupNameList),
            CreateComponentResult(hash, hashMatch, _hashComponent.GroupNameList));
    }

    private static UrlPatternComponentResult CreateComponentResult(string input, Match match, List<string> groupNameList)
    {
        var groups = new Dictionary<string, string?>(StringComparer.Ordinal);

        // The GroupNameList contains the names in order, and they correspond to
        // positional groups in the regex (1-indexed, since group 0 is the full match).
        for (var i = 0; i < groupNameList.Count; i++)
        {
            var groupName = groupNameList[i];
            // Groups are 1-indexed in regex (group 0 is the full match)
            var groupIndex = i + 1;
            if (groupIndex < match.Groups.Count)
            {
                var group = match.Groups[groupIndex];
                groups[groupName] = group.Success ? group.Value : null;
            }
            else
            {
                groups[groupName] = null;
            }
        }

        return new UrlPatternComponentResult(input, groups);
    }

    private bool IsMatchUrl(Uri url)
    {
        var (protocol, username, password, hostname, port, pathname, search, hash) = GetUrlComponents(url);

        return IsMatchComponents(protocol, username, password, hostname, port, pathname, search, hash);
    }

    private bool IsMatchInit(UrlPatternInit init)
    {
        var (protocol, username, password, hostname, port, pathname, search, hash) = CanonicalizeInitComponents(init);

        return IsMatchComponents(protocol, username, password, hostname, port, pathname, search, hash);
    }

    private bool IsMatchComponents(string protocol, string username, string password, string hostname, string port, string pathname, string search, string hash)
    {
        if (!_protocolComponent.RegularExpression.IsMatch(protocol))
            return false;

        if (!_usernameComponent.RegularExpression.IsMatch(username))
            return false;

        if (!_passwordComponent.RegularExpression.IsMatch(password))
            return false;

        if (!_hostnameComponent.RegularExpression.IsMatch(hostname))
            return false;

        if (!_portComponent.RegularExpression.IsMatch(port))
            return false;

        if (!_pathnameComponent.RegularExpression.IsMatch(pathname))
            return false;

        if (!_searchComponent.RegularExpression.IsMatch(search))
            return false;

        if (!_hashComponent.RegularExpression.IsMatch(hash))
            return false;

        return true;
    }

    private static Uri? ParseUrl(string url, string? baseUrl)
    {
        Uri? baseUri = null;
        if (!string.IsNullOrEmpty(baseUrl))
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out baseUri))
            {
                return null;
            }
        }

        if (baseUri is not null)
        {
            if (Uri.TryCreate(baseUri, url, out var result))
            {
                return result;
            }

            return null;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri;
        }

        return null;
    }

    /// <summary>Processes a URLPatternInit to resolve base URL and fill in defaults.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#canon-processing-for-init">WHATWG URL Pattern Spec - URLPatternInit processing</see>
    /// </remarks>
    private static UrlPatternInit ProcessUrlPatternInit(UrlPatternInit init, bool isPattern)
    {
        var result = new UrlPatternInit
        {
            Protocol = init.Protocol,
            Username = init.Username,
            Password = init.Password,
            Hostname = init.Hostname,
            Port = init.Port,
            Pathname = init.Pathname,
            Search = init.Search,
            Hash = init.Hash,
        };

        if (!string.IsNullOrEmpty(init.BaseUrl))
        {
            if (!Uri.TryCreate(init.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new UrlPatternException($"Invalid base URL: {init.BaseUrl}");
            }

            // A component is inherited only when the init specifies nothing at least as specific as it,
            // following the two orders of the spec:
            //   protocol, hostname, port, pathname, search, hash
            //   protocol, hostname, port, username, password
            var hasProtocol = init.Protocol is not null;
            var hasHostname = hasProtocol || init.Hostname is not null;
            var hasPort = hasHostname || init.Port is not null;
            var hasPathname = hasPort || init.Pathname is not null;
            var hasSearch = hasPathname || init.Search is not null;
            var hasHash = hasSearch || init.Hash is not null;

            if (!hasProtocol)
            {
                result.Protocol = ProcessBaseUrlString(baseUri.Scheme, isPattern);
            }

            // The username and password are never inherited when building a pattern
            if (!isPattern && !hasPort)
            {
                var userInfo = baseUri.UserInfo;
                var separatorIndex = userInfo.IndexOf(':', StringComparison.Ordinal);
                if (init.Username is null)
                {
                    result.Username = separatorIndex == -1 ? userInfo : userInfo[..separatorIndex];

                    if (init.Password is null)
                    {
                        result.Password = separatorIndex == -1 ? "" : userInfo[(separatorIndex + 1)..];
                    }
                }
            }

            if (!hasHostname)
            {
                result.Hostname = ProcessBaseUrlString(baseUri.Host, isPattern);
            }

            if (!hasPort)
            {
                result.Port = baseUri.IsDefaultPort ? "" : baseUri.Port.ToString(CultureInfo.InvariantCulture);
            }

            if (!hasPathname)
            {
                result.Pathname = ProcessBaseUrlString(baseUri.AbsolutePath, isPattern);
            }
            else if (result.Pathname is not null && !IsAbsolutePathname(result.Pathname, isPattern))
            {
                // Resolve a relative pathname against the directory of the base URL
                var basePath = ProcessBaseUrlString(baseUri.AbsolutePath, isPattern);
                var slashIndex = basePath.LastIndexOf('/', StringComparison.Ordinal);
                if (slashIndex >= 0)
                {
                    result.Pathname = basePath[..(slashIndex + 1)] + result.Pathname;
                }
            }

            if (!hasSearch)
            {
                result.Search = ProcessBaseUrlString(baseUri.Query.TrimStart('?'), isPattern);
            }

            if (!hasHash)
            {
                result.Hash = ProcessBaseUrlString(baseUri.Fragment.TrimStart('#'), isPattern);
            }
        }

        return result;
    }

    /// <summary>Prepares a value taken from the base URL for use as a component value.</summary>
    /// <remarks>
    /// A base URL supplies literal text, so when it feeds a pattern it must be escaped: without this,
    /// a ':', '*', '{' or '(' in the base URL would be read as pattern syntax.
    /// <see href="https://urlpattern.spec.whatwg.org/#process-a-base-url-string">WHATWG URL Pattern Spec - Process a base URL string</see>
    /// </remarks>
    private static string ProcessBaseUrlString(string input, bool isPattern)
    {
        return isPattern ? PatternParser.EscapePatternString(input) : input;
    }

    private static bool IsAbsolutePathname(string pathname, bool isPattern)
    {
        if (string.IsNullOrEmpty(pathname))
            return false;

        if (pathname[0] == '/')
            return true;

        // The '\\/' and '{/' forms are pattern syntax, so they only make a pathname absolute in a pattern
        if (isPattern && pathname.Length >= 2)
        {
            if (pathname[0] == '\\' && pathname[1] == '/')
                return true;
            if (pathname[0] == '{' && pathname[1] == '/')
                return true;
        }

        return false;
    }

    private static bool IsIPv6Hostname(string hostname)
    {
        if (hostname.Length < 2)
            return false;

        if (hostname[0] == '[')
            return true;

        if (hostname[0] == '{' && hostname.Length > 1 && hostname[1] == '[')
            return true;

        if (hostname[0] == '\\' && hostname.Length > 1 && hostname[1] == '[')
            return true;

        return false;
    }

    private static bool ProtocolMatchesSpecialScheme(UrlPatternComponent protocolComponent)
    {
        foreach (var scheme in SpecialSchemes.All)
        {
            if (protocolComponent.RegularExpression.IsMatch(scheme))
            {
                return true;
            }
        }

        return false;
    }

    // Canonicalization callbacks
    // https://urlpattern.spec.whatwg.org/#canon-encoding-callbacks
    private static string CanonicalizeProtocol(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Remove trailing colon if present
        if (value.EndsWith(':', StringComparison.Ordinal))
        {
            value = value[..^1];
        }

        return value.ToLowerInvariant();
    }

    private static string CanonicalizeUsername(string value)
    {
        return Uri.EscapeDataString(value);
    }

    private static string CanonicalizePassword(string value)
    {
        return Uri.EscapeDataString(value);
    }

    private static string CanonicalizeHostname(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value.ToLowerInvariant();
    }

    private static string CanonicalizeIPv6Hostname(string value)
    {
        // IPv6 hostnames are already in a canonical form
        return value.ToLowerInvariant();
    }

    private static string CanonicalizePort(string value)
    {
        // Validate that port is numeric or empty
        if (string.IsNullOrEmpty(value))
            return value;

        foreach (var c in value)
        {
            if (!char.IsDigit(c))
            {
                return value;
            }
        }

        return value;
    }

    private static string CanonicalizeSearch(string value)
    {
        // Remove leading ? if present
        if (!string.IsNullOrEmpty(value) && value[0] == '?')
        {
            return value[1..];
        }

        return value;
    }

    private static string CanonicalizeHash(string value)
    {
        // Remove leading # if present
        if (!string.IsNullOrEmpty(value) && value[0] == '#')
        {
            return value[1..];
        }

        return value;
    }
}
