using System.Text;
using System.Text.Json;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A parsed payment method manifest (https://w3c.github.io/payment-method-manifest/#parsed-payment-method-manifest): the web
/// app manifests of the default payment applications of a payment method, and the origins whose payment handlers can
/// handle the payment method.
/// </summary>
public sealed class PaymentMethodManifest
{
    private PaymentMethodManifest(IReadOnlyList<string> defaultApplications, IReadOnlyList<string> supportedOrigins)
    {
        DefaultApplications = defaultApplications;
        SupportedOrigins = supportedOrigins;
    }

    /// <summary>The byte order mark of UTF-8, removed by UTF-8 decode (https://encoding.spec.whatwg.org/#utf-8-decode).</summary>
    internal static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>Gets the URLs of the web app manifests of the default payment applications (<c>https:</c> URLs, without duplicates).</summary>
    public IReadOnlyList<string> DefaultApplications { get; }

    /// <summary>Gets the serializations of the origins whose payment handlers can handle the payment method (<c>https:</c> origins, without duplicates).</summary>
    public IReadOnlyList<string> SupportedOrigins { get; }

    /// <summary>
    /// Validates and parses a payment method manifest (https://w3c.github.io/payment-method-manifest/#validate-and-parse):
    /// returns null when the content is not a JSON object, or when its <c>default_applications</c> or <c>supported_origins</c>
    /// member is not a non-empty array of valid URLs (<c>https:</c> URLs, relative to <paramref name="url"/>, for the
    /// applications; <c>https:</c> origins for the supported origins). The other members are ignored.
    /// </summary>
    /// <param name="content">The content of the manifest, encoded as UTF-8.</param>
    /// <param name="url">The URL of the manifest, against which the URLs of the applications are resolved.</param>
    /// <exception cref="ArgumentException"><paramref name="url"/> is not an absolute URL.</exception>
    public static PaymentMethodManifest? Parse(ReadOnlySpan<byte> content, string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var baseUrl = UrlParser.Parse(url, baseUrl: null) ?? throw new ArgumentException("The URL of the manifest is not an absolute URL", nameof(url));
        return Parse(content, baseUrl);
    }

    internal static PaymentMethodManifest? Parse(ReadOnlySpan<byte> content, UrlRecord url)
    {
        // UTF-8 decode (https://encoding.spec.whatwg.org/#utf-8-decode): the BOM is removed
        if (content.StartsWith(Utf8Bom))
        {
            content = content[3..];
        }

        var text = Html.SpeculationRuleSet.ReplaceLoneSurrogates(Encoding.UTF8.GetString(content));
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 512 });
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            // As JSON.parse(), the last member of a name wins
            JsonElement? defaultApplicationsValue = null;
            JsonElement? supportedOriginsValue = null;
            foreach (var property in root.EnumerateObject())
            {
                if (property.NameEquals("default_applications"))
                {
                    defaultApplicationsValue = property.Value;
                }
                else if (property.NameEquals("supported_origins"))
                {
                    supportedOriginsValue = property.Value;
                }
            }

            var defaultApplications = new List<string>();
            if (defaultApplicationsValue is { } defaultApps)
            {
                if (defaultApps.ValueKind != JsonValueKind.Array || defaultApps.GetArrayLength() == 0)
                    return null;

                foreach (var defaultApp in defaultApps.EnumerateArray())
                {
                    if (defaultApp.ValueKind != JsonValueKind.String || UrlParser.Parse(defaultApp.GetString()!, url) is not { Scheme: "https" } defaultAppUrl)
                        return null;

                    var serialized = defaultAppUrl.Serialize();
                    if (!defaultApplications.Contains(serialized, StringComparer.Ordinal))
                    {
                        defaultApplications.Add(serialized);
                    }
                }
            }

            var supportedOrigins = new List<string>();
            if (supportedOriginsValue is { } origins)
            {
                if (origins.ValueKind != JsonValueKind.Array || origins.GetArrayLength() == 0)
                    return null;

                foreach (var supportedOrigin in origins.EnumerateArray())
                {
                    if (supportedOrigin.ValueKind != JsonValueKind.String || UrlParser.Parse(supportedOrigin.GetString()!, baseUrl: null) is not { Scheme: "https" } originUrl)
                        return null;

                    // The path of a URL with a special scheme is never empty: the URL of an origin has the path "/" (as in the
                    // browsers, https://example.com/ is an origin, but not https://example.com/pay)
                    if (originUrl.IncludesCredentials || originUrl.HasOpaquePath || originUrl.SerializePath() != "/" || originUrl.Query is not null || originUrl.Fragment is not null)
                        return null;

                    var serialized = originUrl.SerializeOrigin();
                    if (!supportedOrigins.Contains(serialized, StringComparer.Ordinal))
                    {
                        supportedOrigins.Add(serialized);
                    }
                }
            }

            return new PaymentMethodManifest(defaultApplications, supportedOrigins);
        }
    }
}

/// <summary>A web app manifest of a default payment application of a payment method (https://w3c.github.io/payment-method-manifest/#fetch-wam).</summary>
/// <param name="Url">The URL of the web app manifest.</param>
/// <param name="Content">The content of the web app manifest (to process with the steps for processing a web app manifest).</param>
public sealed record PaymentAppManifest(string Url, string Content);

/// <summary>The payment method manifest of a payment method identifier, fetched by <see cref="PaymentMethodManifestRequest.FetchAsync"/>.</summary>
public sealed class PaymentMethodManifestResult
{
    internal PaymentMethodManifestResult(string manifestUrl, PaymentMethodManifest manifest, IReadOnlyList<PaymentAppManifest> applications)
    {
        ManifestUrl = manifestUrl;
        Manifest = manifest;
        Applications = applications;
    }

    /// <summary>Gets the URL of the payment method manifest.</summary>
    public string ManifestUrl { get; }

    /// <summary>Gets the parsed payment method manifest.</summary>
    public PaymentMethodManifest Manifest { get; }

    /// <summary>Gets the web app manifests of the default applications that were fetched (an application whose manifest cannot be fetched is skipped).</summary>
    public IReadOnlyList<PaymentAppManifest> Applications { get; }
}

/// <summary>
/// A URL-based payment method identifier whose payment method manifest can be ingested, given to
/// <see cref="UserAgentUIBackend.IngestPaymentMethodManifestAsync"/>.
/// </summary>
public sealed class PaymentMethodManifestRequest : UserAgentPrompt
{
    // https://w3c.github.io/payment-method-manifest/#fetch-pmm: the URL list of the response of the identifier has at most 4 URLs
    private const int MaxIdentifierRedirects = 3;

    private readonly Window _window;
    private readonly UrlRecord _identifierUrl;

    internal PaymentMethodManifestRequest(Window window, UrlRecord identifierUrl)
        : base(window)
    {
        _window = window;
        _identifierUrl = identifierUrl;
        MethodIdentifier = identifierUrl.Serialize();
    }

    /// <summary>Gets the URL-based payment method identifier.</summary>
    public string MethodIdentifier { get; }

    /// <summary>
    /// Fetches the payment method manifest (https://w3c.github.io/payment-method-manifest/#fetch-payment-method-manifests)
    /// and the web app manifests of its default applications (https://w3c.github.io/payment-method-manifest/#fetch-the-web-app-manifest-for-a-default-payment-app),
    /// with the document of the request as client and without credentials: a <c>HEAD</c> request to the identifier (up to 3
    /// redirects, all same site with the identifier) finds the first <c>Link</c> header whose relation is
    /// <c>payment-method-manifest</c>, then the manifest is fetched (<c>https:</c> only, without redirect) and parsed. Returns
    /// null when a step fails.
    /// </summary>
    /// <remarks>
    /// As in Chromium, whose user agent fetches the manifests without CORS, the responses are read whatever their
    /// <c>Access-Control-Allow-Origin</c> header (the specification uses the "cors" mode).
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the fetches.</param>
    public async Task<PaymentMethodManifestResult?> FetchAsync(CancellationToken cancellationToken = default)
    {
        // The HEAD request of the identifier, whose redirects are followed one at a time to check that they stay same site
        var identifierOrigin = OriginRecord.FromUrl(_identifierUrl);
        var url = _identifierUrl;
        FetchResponse identifierResponse;
        for (var redirects = 0; ; redirects++)
        {
            var response = await FetchResourceAsync(url, "HEAD", referrer: null, FetchRedirectMode.Manual, cancellationToken).ConfigureAwait(false);
            if (response.IsNetworkError)
                return null;

            if (response.Status is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.GetLocationUrl(requestFragment: null, out var failure);
                if (failure || location is null || redirects >= MaxIdentifierRedirects || !OriginRecord.IsSameSite(OriginRecord.FromUrl(location), identifierOrigin))
                    return null;

                url = location;
                continue;
            }

            identifierResponse = response;
            break;
        }

        if (!HeaderList.IsOkStatus(identifierResponse.Status))
            return null;

        var linkHeaders = HeaderList.GetDecodeAndSplit(identifierResponse.HeaderList, "Link");
        if (LinkHeader.FindPaymentMethodManifest(linkHeaders ?? []) is not { } manifestUrlString)
        {
            // The specification parses the body of the identifier as a manifest; the body of a response to a HEAD request is empty
            return null;
        }

        if (UrlParser.Parse(manifestUrlString, url) is not { Scheme: "https" } manifestUrl)
            return null;

        var manifestResponse = await FetchResourceAsync(manifestUrl, "GET", referrer: url, FetchRedirectMode.Error, cancellationToken).ConfigureAwait(false);
        if (manifestResponse.IsNetworkError || !HeaderList.IsOkStatus(manifestResponse.Status) || manifestResponse.Body is not { } manifestBody)
            return null;

        var responseUrl = manifestResponse.Url ?? manifestUrl;
        if (PaymentMethodManifest.Parse(manifestBody, responseUrl) is not { } manifest)
            return null;

        var applications = new List<PaymentAppManifest>(manifest.DefaultApplications.Count);
        foreach (var application in manifest.DefaultApplications)
        {
            var applicationUrl = UrlParser.Parse(application, baseUrl: null)!;
            var response = await FetchResourceAsync(applicationUrl, "GET", referrer: _identifierUrl, FetchRedirectMode.Error, cancellationToken).ConfigureAwait(false);
            if (response.IsNetworkError || !HeaderList.IsOkStatus(response.Status) || response.Body is not { } body)
                continue;

            applications.Add(new PaymentAppManifest(application, Encoding.UTF8.GetString(body.AsSpan().StartsWith(PaymentMethodManifest.Utf8Bom) ? body.AsSpan(3) : body)));
        }

        return new PaymentMethodManifestResult(responseUrl.Serialize(), manifest, applications);
    }

    /// <summary>Fetches a URL from a task of the event loop of the window, and returns the internal response.</summary>
    private async Task<FetchResponse> FetchResourceAsync(UrlRecord url, string method, UrlRecord? referrer, FetchRedirectMode redirectMode, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<FetchResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _window.QueueGlobalTask(() =>
        {
            var document = _window.Document;
            var request = new FetchRequest(url.Clone())
            {
                Method = method,
                Client = document,
                Origin = document.Origin.Serialize(),
                Mode = RequestMode.NoCors,
                IsUserAgentRequest = true,
                CredentialsMode = FetchCredentialsMode.Omit,
                RedirectMode = redirectMode,
                ServiceWorkersMode = ServiceWorkersMode.None,
                ReferrerPolicy = referrer is null ? "strict-origin-when-cross-origin" : "",
                Referrer = referrer?.Serialize() ?? "client",
            };
            Fetching.Fetch(request, response => completion.TrySetResult(response), _window.QueueGlobalTask, cancellationToken);
        });

        var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return result.Internal;
    }
}

/// <summary>The <c>Link</c> header of a response (https://www.rfc-editor.org/rfc/rfc8288#section-3).</summary>
internal static class LinkHeader
{
    /// <summary>
    /// Returns the target of the first link whose relation types include <c>payment-method-manifest</c> (ASCII
    /// case-insensitive), or null (https://w3c.github.io/payment-method-manifest/#fetch-pmm). The values that are not a valid
    /// link-value are ignored.
    /// </summary>
    public static string? FindPaymentMethodManifest(IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            if (!TryParse(value, out var target, out var parameters))
                continue;

            // https://www.rfc-editor.org/rfc/rfc8288#section-3.3: the occurrences of rel after the first one are ignored
            foreach (var (name, parameterValue) in parameters)
            {
                if (!Infra.InfraAscii.EqualsIgnoreAsciiCase(name, "rel"))
                    continue;

                if (parameterValue is not null)
                {
                    foreach (var relationType in parameterValue.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (Infra.InfraAscii.EqualsIgnoreAsciiCase(relationType, "payment-method-manifest"))
                            return target;
                    }
                }

                break;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a link-value: <c>"&lt;" URI-Reference "&gt;" *( OWS ";" OWS link-param )</c>, where
    /// <c>link-param = token BWS [ "=" BWS ( token / quoted-string ) ]</c>. The value of a quoted-string is unescaped.
    /// </summary>
    public static bool TryParse(string value, out string target, out List<(string Name, string? Value)> parameters)
    {
        target = "";
        parameters = [];
        var span = value.AsSpan().Trim(" \t");
        if (!span.StartsWith('<'))
            return false;

        var end = span.IndexOf('>');
        if (end < 0)
            return false;

        target = span[1..end].ToString();
        var position = end + 1;
        while (true)
        {
            position = SkipWhitespace(span, position);
            if (position == span.Length)
                return true;

            if (span[position] != ';')
                return false;

            position = SkipWhitespace(span, position + 1);
            var nameStart = position;
            while (position < span.Length && IsTokenChar(span[position]))
            {
                position++;
            }

            if (position == nameStart)
                return false;

            var name = span[nameStart..position].ToString();
            position = SkipWhitespace(span, position);
            if (position == span.Length || span[position] != '=')
            {
                parameters.Add((name, null));
                continue;
            }

            position = SkipWhitespace(span, position + 1);
            if (position < span.Length && span[position] == '"')
            {
                var builder = new StringBuilder();
                position++;
                while (true)
                {
                    if (position == span.Length)
                        return false;

                    var c = span[position++];
                    if (c == '"')
                        break;

                    if (c == '\\')
                    {
                        if (position == span.Length)
                            return false;

                        c = span[position++];
                    }

                    builder.Append(c);
                }

                parameters.Add((name, builder.ToString()));
            }
            else
            {
                var valueStart = position;
                while (position < span.Length && IsTokenChar(span[position]))
                {
                    position++;
                }

                if (position == valueStart)
                    return false;

                parameters.Add((name, span[valueStart..position].ToString()));
            }
        }
    }

    /// <summary>Serializes a link-value (the parameter values are quoted strings).</summary>
    public static string Serialize(string target, IEnumerable<(string Name, string? Value)> parameters)
    {
        var builder = new StringBuilder();
        builder.Append('<').Append(target).Append('>');
        foreach (var (name, value) in parameters)
        {
            builder.Append("; ").Append(name);
            if (value is not null)
            {
                builder.Append("=\"");
                foreach (var c in value)
                {
                    if (c is '"' or '\\')
                    {
                        builder.Append('\\');
                    }

                    builder.Append(c);
                }

                builder.Append('"');
            }
        }

        return builder.ToString();
    }

    /// <summary>https://www.rfc-editor.org/rfc/rfc9110#name-tokens: tchar.</summary>
    public static bool IsTokenChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';

    private static int SkipWhitespace(ReadOnlySpan<char> span, int position)
    {
        while (position < span.Length && span[position] is ' ' or '\t')
        {
            position++;
        }

        return position;
    }
}
