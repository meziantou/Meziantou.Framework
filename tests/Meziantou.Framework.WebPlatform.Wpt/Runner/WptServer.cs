using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The configuration of the WPT server (wptserve) emulated by the runner: the hosts that serve the files of the test suite
/// and the substitutions of the <c>.sub.</c> files (https://web-platform-tests.org/writing-tests/server-pipes.html#sub).
/// </summary>
internal static partial class WptServer
{
    private const string Domain = "web-platform.test";
    private const string AltDomain = "not-web-platform.test";

    /// <summary>The subdomains of the configuration of wptserve.</summary>
    private static readonly Dictionary<string, string> Subdomains = new(StringComparer.Ordinal)
    {
        [""] = "",
        ["www"] = "www.",
        ["www1"] = "www1.",
        ["www2"] = "www2.",
        ["天気の良い日"] = "xn--n8j6ds53lwwkrqhv28a.",
        ["élève"] = "xn--lve-6lad.",
    };

    /// <summary>
    /// The prefix of a subdomain of the configuration (e.g. <c>www.</c>), or null. The subdomains are the products of one or
    /// two labels of the configuration (e.g. <c>www2.www</c>), as in tools/serve/serve.py.
    /// </summary>
    private static string? GetSubdomainPrefix(string name)
    {
        if (Subdomains.TryGetValue(name, out var prefix))
            return prefix;

        // A file substituted as bytes has the UTF-8 bytes of the non-ASCII subdomains (e.g. élève)
        if (name.Any(c => c > '\x7F') && name.All(c => c <= '\xFF'))
        {
            var decoded = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(name));
            if (decoded != name && GetSubdomainPrefix(decoded) is { } decodedPrefix)
                return decodedPrefix;
        }

        var labels = name.Split('.');
        if (labels.Length == 2 && labels.All(label => label.Length > 0) && labels.All(label => label == NotSubdomain))
            return name + ".";

        if (labels.Length == 2 && labels.All(label => label.Length > 0 && Subdomains.ContainsKey(label)))
            return Subdomains[labels[0]] + Subdomains[labels[1]];

        return name == NotSubdomain ? NotSubdomain + "." : null;
    }

    /// <summary>The subdomain of the configuration of wptserve that does not resolve (<c>not_subdomains</c>): its requests are network errors.</summary>
    private const string NotSubdomain = "nonexistent";

    private static readonly Dictionary<string, int[]> Ports = new(StringComparer.Ordinal)
    {
        ["http"] = [8000, 8001],
        ["https"] = [8443, 8444],
        ["ws"] = [8888],
        ["wss"] = [8889],
        ["h2"] = [9000],
        ["webtransport-h3"] = [11000],
    };

    /// <summary>
    /// Whether a URL is served by the WPT server: the domains of the configuration and their subdomains (they serve the same
    /// files), except the subdomain that does not resolve, and the loopback address the servers listen on (127.0.0.1). The
    /// ports of the other scheme are not served (e.g. http://web-platform.test:8443/, the URL of the downgrade redirections of
    /// common/security-features).
    /// </summary>
    public static bool IsHost(Url url) => url.Protocol is "http:" or "https:"
        && (url.Hostname is Domain or AltDomain or "127.0.0.1" || url.Hostname.EndsWith("." + Domain, StringComparison.Ordinal) || url.Hostname.EndsWith("." + AltDomain, StringComparison.Ordinal))
        && !url.Hostname.StartsWith(NotSubdomain + ".", StringComparison.Ordinal)
        && (url.Port.Length == 0 || url.Port is var port && (url.Protocol == "http:" ? Ports["http"] : [.. Ports["https"], .. Ports["h2"]]).Any(p => p.ToString(CultureInfo.InvariantCulture) == port));

    /// <summary>
    /// Whether a URL is served by the WebSocket server of wptserve: the hosts of <see cref="IsHost"/>, on the <c>ws</c> port for
    /// the <c>http:</c> URLs and on the <c>wss</c> and <c>h2</c> ports for the <c>https:</c> URLs (the URL of the opening
    /// handshake of a WebSocket has the <c>http</c> or <c>https</c> scheme).
    /// </summary>
    public static bool IsWebSocketHost(Url url) => url.Protocol is "http:" or "https:"
        && (url.Hostname is Domain or AltDomain || url.Hostname.EndsWith("." + Domain, StringComparison.Ordinal) || url.Hostname.EndsWith("." + AltDomain, StringComparison.Ordinal))
        && !url.Hostname.StartsWith(NotSubdomain + ".", StringComparison.Ordinal)
        && url.Port is var port && (url.Protocol == "http:" ? Ports["ws"] : [.. Ports["wss"], .. Ports["h2"]]).Any(p => p.ToString(CultureInfo.InvariantCulture) == port);

    /// <summary>Whether a URL is served by the HTTP/2 server of wptserve (the <c>h2</c> port): its responses have no reason phrase.</summary>
    public static bool IsHttp2(Url url) => url.Protocol == "https:" && Ports["h2"].Any(p => p.ToString(CultureInfo.InvariantCulture) == url.Port);

    /// <summary>Whether the substitutions apply to a response: a <c>.sub.</c> file, or the <c>pipe=sub</c> query parameter.</summary>
    public static bool HasSubstitutions(Url url) => WptPipes.HasSubstitutions(WptPipes.Get(url));

    /// <summary>
    /// Replaces the <c>{{...}}</c> templates of a response (https://web-platform-tests.org/writing-tests/server-pipes.html#sub).
    /// As wptserve does, the values are HTML-escaped (<c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c> and <c>'</c>) by the
    /// <c>sub</c> pipe of the query and the <c>.sub.</c> markup files, not by <c>sub(none)</c>, the other <c>.sub.</c> files
    /// (e.g. scripts) and the .sub.headers files (see <see cref="WptPipes.Get"/>).
    /// </summary>
    /// <param name="content">The content of the response.</param>
    /// <param name="url">The URL of the request.</param>
    /// <param name="requestHeaders">The headers of the request (<c>{{headers[name]}}</c>, <c>{{header_or_default(name, default)}}</c>), or null when they are unknown.</param>
    /// <param name="escape">Whether the values are HTML-escaped (false for the .sub.headers files).</param>
    /// <param name="variables">
    /// The variables of the response (<c>{{$name:expression}}</c> sets a variable to the value of the expression,
    /// <c>{{$name}}</c> is its value), shared by the substitutions of a file, or null for a new set of variables.
    /// </param>
    public static string Substitute(string content, Url url, IReadOnlyList<KeyValuePair<string, string>>? requestHeaders = null, bool escape = true, Dictionary<string, string>? variables = null)
    {
        escape &= WptPipes.Get(url).Find(pipe => pipe.Name == "sub") is not { Arguments: ["none", ..] };
        variables ??= new(StringComparer.Ordinal);
        return TemplateRegex().Replace(content, match =>
        {
            var expression = match.Groups["expression"].Value.Trim();
            string? value;
            if (expression.StartsWith('$', StringComparison.Ordinal))
            {
                // https://web-platform-tests.org/writing-tests/server-pipes.html#sub: the variables
                var separator = expression.IndexOf(':', StringComparison.Ordinal);
                if (separator < 0)
                {
                    value = variables.GetValueOrDefault(expression[1..].Trim());
                }
                else
                {
                    value = Evaluate(expression[(separator + 1)..].Trim(), url, requestHeaders ?? []);
                    if (value is not null)
                    {
                        variables[expression[1..separator].Trim()] = value;
                    }
                }
            }
            else
            {
                value = Evaluate(expression, url, requestHeaders ?? []);
            }

            return value is not null ? (escape ? EscapeHtml(value) : value) : match.Value;
        });
    }

    /// <summary>Replaces the templates of a response whose encoding is unknown (the templates are ASCII).</summary>
    public static byte[] Substitute(byte[] content, Url url, IReadOnlyList<KeyValuePair<string, string>>? requestHeaders = null) => Encoding.Latin1.GetBytes(Substitute(Encoding.Latin1.GetString(content), url, requestHeaders));

    /// <summary>Python's <c>html.escape(value, quote=True)</c>.</summary>
    private static string EscapeHtml(string value)
    {
        if (value.AsSpan().IndexOfAny("&<>\"'") < 0)
            return value;

        return value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#x27;", StringComparison.Ordinal);
    }

    private static string? Evaluate(string expression, Url url, IReadOnlyList<KeyValuePair<string, string>> requestHeaders)
    {
        if (expression is "host")
            return Domain;

        // header_or_default(name, default): the value of the header of the request, else the default value
        if (HeaderOrDefaultRegex().Match(expression) is { Success: true } headerOrDefault)
            return HeaderList.Get(requestHeaders, headerOrDefault.Groups["name"].Value) ?? headerOrDefault.Groups["default"].Value;

        if (expression is "uuid()")
            return Guid.NewGuid().ToString("D");

        var match = IndexedRegex().Match(expression);
        if (!match.Success)
            return null;

        var name = match.Groups["name"].Value;
        var first = match.Groups["first"].Value;
        var second = match.Groups["second"].Success ? match.Groups["second"].Value : null;
        switch (name)
        {
            case "domains" when second is null:
                return GetSubdomainPrefix(first) is { } prefix ? prefix + Domain : null;

            case "hosts" when second is not null:
                return GetSubdomainPrefix(second) is { } hostPrefix ? hostPrefix + (first == "alt" ? AltDomain : Domain) : null;

            case "ports" when second is not null:
                return Ports.TryGetValue(first, out var ports) && int.TryParse(second, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < ports.Length
                    ? ports[index].ToString(CultureInfo.InvariantCulture)
                    : null;

            case "GET" when second is null:
                return url.SearchParams.Get(first) ?? "";

            case "location" when second is null:
                return first switch
                {
                    "scheme" => url.Protocol.TrimEnd(':'),
                    "host" => url.Host,
                    "hostname" => url.Hostname,
                    "port" => url.Port.Length > 0 ? url.Port : (url.Protocol == "https:" ? "443" : "80"),
                    "path" => url.Pathname,
                    "pathname" => url.Pathname,
                    "query" => url.Search,
                    "server" => url.Protocol + "//" + url.Host,
                    "href" => url.Href,
                    _ => null,
                };

            case "headers" when second is null:
                return HeaderList.Get(requestHeaders, first) ?? "";
        }

        return null;
    }

    /// <summary>
    /// The directory listing of wptserve (<c>DirectoryHandler</c> of tools/wptserve/wptserve/handlers.py): the response to a
    /// request for a directory whose path ends with a slash. The <c>.headers</c> file of a file is listed with it.
    /// </summary>
    public static byte[] GetDirectoryListing(string urlPath, string directory)
    {
        var items = new StringBuilder();
        if (urlPath != "/")
        {
            items.Append("<li class=\"dir\"><a href=\"").Append(Escape(new Uri(new Uri("http://host" + urlPath), "..").AbsolutePath)).Append("\">..</a></li>\n");
        }

        // A .headers file sorts after the file it provides the headers for (x, x.headers, then x-y)
        var names = Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).OfType<string>()
            .OrderBy(name => name.EndsWith(".headers", StringComparison.Ordinal) ? name[..^".headers".Length] : name, StringComparer.Ordinal)
            .ThenBy(name => name, StringComparer.Ordinal)
            .ToList();
        var entries = new List<(string Name, string? Headers)>();
        string? previous = null;
        foreach (var name in names)
        {
            if (previous is not null && previous + ".headers" == name)
            {
                entries[^1] = (previous, name);
                previous = null;
                continue;
            }

            entries.Add((name, null));
            previous = name;
        }

        foreach (var (name, headers) in entries)
        {
            var link = Escape(Uri.EscapeDataString(name));
            var isDirectory = Directory.Exists(Path.Combine(directory, name));
            items.Append("<li class=\"").Append(isDirectory ? "dir" : "file").Append("\"><a href=\"").Append(link).Append(isDirectory ? "/" : "").Append("\">").Append(Escape(name)).Append("</a>");
            if (headers is not null)
            {
                items.Append(" (<a href=\"").Append(Escape(Uri.EscapeDataString(headers))).Append("\">.headers</a>)");
            }

            items.Append("</li>\n");
        }

        var path = Escape(urlPath);
        return Encoding.UTF8.GetBytes($"""
            <!doctype html>
            <meta name="viewport" content="width=device-width">
            <title>Directory listing for {path}</title>
            <h1>Directory listing for {path}</h1>
            <ul>
            {items.ToString().TrimEnd('\n')}
            </ul>

            """);

        static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value);
    }

    [GeneratedRegex(@"\{\{(?<expression>[^{}]+)\}\}", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TemplateRegex();

    [GeneratedRegex(@"^header_or_default\(\s*(?<name>[^,\s]+)\s*,\s*(?<default>[^)]*?)\s*\)$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HeaderOrDefaultRegex();

    [GeneratedRegex(@"^(?<name>\w+)\[(?<first>[^\]]*)\](\[(?<second>[^\]]*)\])?$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IndexedRegex();
}
