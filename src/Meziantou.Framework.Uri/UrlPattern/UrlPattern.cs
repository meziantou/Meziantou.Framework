using Meziantou.Framework.UrlPatternInternal;

namespace Meziantou.Framework;

/// <summary>Represents a URL pattern that can match URLs based on a convenient pattern syntax.</summary>
/// <remarks>
/// <para>The URL Pattern API provides a web platform primitive for matching URLs based on a convenient pattern syntax.</para>
/// <para>A URL pattern consists of several components (protocol, hostname, pathname, etc.), each of which represents a pattern that can be matched against the corresponding component of a URL.</para>
/// <see href="https://urlpattern.spec.whatwg.org/">WHATWG URL Pattern Spec</see>
/// <see href="https://developer.mozilla.org/en-US/docs/Web/API/URL_Pattern_API">MDN - URL Pattern API</see>
/// </remarks>
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

    /// <summary>
    /// Default ports for special schemes.
    /// </summary>
    private static readonly Dictionary<string, string> DefaultPorts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ftp"] = "21",
        ["http"] = "80",
        ["https"] = "443",
        ["ws"] = "80",
        ["wss"] = "443",
    };

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

        if (baseUrl is null && !init.ContainsKey("protocol"))
        {
            throw new UrlPatternException("A base URL must be provided when the pattern does not specify a protocol.");
        }

        if (baseUrl is not null)
        {
            init["baseURL"] = baseUrl;
        }

        return Create(ConvertDictionaryToInit(init), options);
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

        var processedInit = ProcessUrlPatternInit(init);

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
            DefaultPorts.TryGetValue(processedInit.Protocol, out var defaultPort) &&
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

        UrlPatternComponent pathnameComponent;
        if (ProtocolMatchesSpecialScheme(protocolComponent))
        {
            pathnameComponent = UrlPatternComponent.Compile(processedInit.Pathname, CanonicalizePathname, pathCompileOptions);
        }
        else
        {
            pathnameComponent = UrlPatternComponent.Compile(processedInit.Pathname, CanonicalizeOpaquePathname, compileOptions);
        }

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

    /// <summary>Tests if the pattern matches the given URL.</summary>
    /// <param name="url">The URL string to test.</param>
    /// <returns><c>true</c> if the pattern matches the URL; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-test">WHATWG URL Pattern Spec - test method</see>
    /// <see href="https://developer.mozilla.org/en-US/docs/Web/API/URLPattern/test">MDN - URLPattern.test()</see>
    /// </remarks>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public bool Test(string url)
    {
        return Test(url, baseUrl: null);
    }

    /// <summary>Tests if the pattern matches the given URL with a base URL.</summary>
    /// <param name="url">The URL string to test.</param>
    /// <param name="baseUrl">The base URL to use for resolving relative URLs.</param>
    /// <returns><c>true</c> if the pattern matches the URL; otherwise, <c>false</c>.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public bool Test(string url, string? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(url);

        try
        {
            var uri = ParseUrl(url, baseUrl);
            if (uri is null)
                return false;

            return MatchUrl(uri);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    /// <summary>Tests if the pattern matches the given URL.</summary>
    /// <param name="url">The URL to test.</param>
    /// <returns><c>true</c> if the pattern matches the URL; otherwise, <c>false</c>.</returns>
    public bool Test(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return MatchUrl(url);
    }

    /// <summary>Tests if the pattern matches the given URL input.</summary>
    /// <param name="input">The URL input dictionary to test.</param>
    /// <returns><c>true</c> if the pattern matches the input; otherwise, <c>false</c>.</returns>
    public bool Test(UrlPatternInit input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var processed = ProcessUrlPatternInit(input);
        return MatchInit(processed);
    }

    private bool MatchUrl(Uri url)
    {
        var protocol = url.Scheme;
        var username = Uri.UnescapeDataString(url.UserInfo.Split(':').FirstOrDefault() ?? "");
        var password = url.UserInfo.Contains(':', StringComparison.Ordinal) ? Uri.UnescapeDataString(url.UserInfo[(url.UserInfo.IndexOf(':', StringComparison.Ordinal) + 1)..]) : "";
        var hostname = url.Host;
        var port = url.IsDefaultPort ? "" : url.Port.ToString(CultureInfo.InvariantCulture);
        var pathname = url.AbsolutePath;
        var search = url.Query.TrimStart('?');
        var hash = url.Fragment.TrimStart('#');

        return MatchComponents(protocol, username, password, hostname, port, pathname, search, hash);
    }

    private bool MatchInit(UrlPatternInit init)
    {
        var protocol = init.Protocol ?? "";
        var username = init.Username ?? "";
        var password = init.Password ?? "";
        var hostname = init.Hostname ?? "";
        var port = init.Port ?? "";
        var pathname = init.Pathname ?? "";
        var search = init.Search ?? "";
        var hash = init.Hash ?? "";

        return MatchComponents(protocol, username, password, hostname, port, pathname, search, hash);
    }

    private bool MatchComponents(string protocol, string username, string password, string hostname, string port, string pathname, string search, string hash)
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
    private static UrlPatternInit ProcessUrlPatternInit(UrlPatternInit init)
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

            // Inherit from base URL if not specified
            result.Protocol ??= baseUri.Scheme;
            result.Hostname ??= baseUri.Host;
            result.Port ??= baseUri.IsDefaultPort ? "" : baseUri.Port.ToString(CultureInfo.InvariantCulture);

            // Pathname inheritance is more complex - only inherit if less specific
            if (result.Pathname is null && result.Search is null && result.Hash is null)
            {
                result.Pathname = baseUri.AbsolutePath;
            }
            else if (result.Pathname is null)
            {
                // If search or hash specified but not pathname, default pathname based on protocol
                result.Pathname = IsSpecialScheme(result.Protocol ?? "") ? "/" : "";
            }
            else if (!IsAbsolutePathname(result.Pathname))
            {
                // Resolve relative pathname
                var basePath = baseUri.AbsolutePath;
                var slashIndex = basePath.LastIndexOf('/');
                if (slashIndex >= 0)
                {
                    result.Pathname = basePath[..(slashIndex + 1)] + result.Pathname;
                }
            }

            // Search and hash are not inherited from base URL
        }

        return result;
    }

    private static bool IsAbsolutePathname(string pathname)
    {
        if (string.IsNullOrEmpty(pathname))
            return false;

        if (pathname[0] == '/')
            return true;

        if (pathname.Length >= 2)
        {
            if (pathname[0] == '\\' && pathname[1] == '/')
                return true;
            if (pathname[0] == '{' && pathname[1] == '/')
                return true;
        }

        return false;
    }

    private static bool IsSpecialScheme(string protocol)
    {
        return SpecialSchemes.Contains(protocol);
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
        foreach (var scheme in SpecialSchemes)
        {
            if (protocolComponent.RegularExpression.IsMatch(scheme))
            {
                return true;
            }
        }

        return false;
    }

    private static UrlPatternInit ConvertDictionaryToInit(Dictionary<string, string> dict)
    {
        var init = new UrlPatternInit();

        if (dict.TryGetValue("protocol", out var protocol))
            init.Protocol = protocol;
        if (dict.TryGetValue("username", out var username))
            init.Username = username;
        if (dict.TryGetValue("password", out var password))
            init.Password = password;
        if (dict.TryGetValue("hostname", out var hostname))
            init.Hostname = hostname;
        if (dict.TryGetValue("port", out var port))
            init.Port = port;
        if (dict.TryGetValue("pathname", out var pathname))
            init.Pathname = pathname;
        if (dict.TryGetValue("search", out var search))
            init.Search = search;
        if (dict.TryGetValue("hash", out var hash))
            init.Hash = hash;
        if (dict.TryGetValue("baseURL", out var baseUrl))
            init.BaseUrl = baseUrl;

        return init;
    }

    // Canonicalization callbacks
    // https://urlpattern.spec.whatwg.org/#canon-encoding-callbacks

    private static string CanonicalizeProtocol(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Remove trailing colon if present
        if (value.EndsWith(':'))
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

    private static string CanonicalizePathname(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Ensure starts with /
        if (!value.StartsWith('/'))
        {
            value = "/" + value;
        }

        return value;
    }

    private static string CanonicalizeOpaquePathname(string value)
    {
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
