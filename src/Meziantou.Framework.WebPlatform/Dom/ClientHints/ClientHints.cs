using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The Client Hints infrastructure (https://wicg.github.io/client-hints-infrastructure/): the client hints that the requests
/// of a document carry (<see cref="AppendToRequest"/>), from the Accept-CH cache of the browsing context group (see
/// <see cref="BrowsingContext.AcceptCHCache"/>, written by the responses of the top-level navigations), the client hints set
/// of the environment (see <see cref="Document.ClientHintsSet"/>, written by the <c>meta</c> elements) and the permissions
/// policy of the document, with the values of <see cref="BrowsingContextOptions.ClientHints"/> and of the viewport.
/// </summary>
/// <remarks>
/// Where the specification is silent or the web-platform-tests expect something else, the behavior is the one of Chromium
/// (the only implementation):
/// <list type="bullet">
/// <item>The hints of the request of a top-level navigation are the ones of the origin of its URL (the origin of the
/// document that is requested), for each URL of its redirect chain; the specification uses the hints of the document that
/// initiates the navigation, which would send the hints of an origin to another one.</item>
/// <item>The hints of a nested document are the ones of its top-level document, filtered by its own permissions policy; the
/// specification uses the entry of the Accept-CH cache of the origin of the nested document, so a cross-origin iframe
/// could not get the hints that its embedder delegates to it.</item>
/// <item>The <c>allow</c> attribute of an iframe overrides the allowlist of the hints it names for the navigation
/// requests of the iframe.</item>
/// <item>The hints are only sent to the potentially trustworthy URLs, and not with the requests of the scripts of the
/// service workers (the specification only excludes the scripts of the dedicated and shared workers).</item>
/// </list>
/// </remarks>
internal static class ClientHints
{
    private static readonly char[] AsciiWhitespace = [' ', '\t', '\n', '\f', '\r'];

    /// <summary>Whether the client hints are enabled (see <see cref="WebPlatformFeatures.ClientHintsEnabled"/>).</summary>
    public static bool IsEnabled(BrowsingContextOptions options) => options.EffectiveFeatures.ClientHintsEnabled;

    // ----------------------------------------------------------------------------------------------------------------
    // The Accept-CH and Critical-CH headers
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The client hints tokens of a header whose value is a structured field list of tokens (<c>Accept-CH</c>,
    /// <c>Critical-CH</c>): null when the header is missing, and the empty set when its value is not a list (e.g. an empty
    /// header followed by another one), so that a malformed <c>Accept-CH</c> header opts out of the hints.
    /// </summary>
    public static ClientHintsSet? ParseHeader(IReadOnlyList<KeyValuePair<string, string>> headers, string name)
        => HeaderList.Get(headers, name) is { } value ? ParseTokenList(value) ?? ClientHintsSet.Empty : null;

    /// <summary>
    /// The client hints tokens of a structured field list (https://www.rfc-editor.org/rfc/rfc8941#name-lists), or null when
    /// the value is not a list. The members that are not tokens, and the tokens that are not client hints tokens, are ignored.
    /// </summary>
    public static ClientHintsSet? ParseTokenList(string value)
    {
        var set = ClientHintsSet.Empty;
        var span = value.AsSpan().Trim(' ');
        if (span.IsEmpty)
            return set;

        // The members of the list: the commas of the strings do not separate them
        var start = 0;
        var inString = false;
        for (var i = 0; i <= span.Length; i++)
        {
            if (i < span.Length)
            {
                var c = span[i];
                if (inString)
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c != ',')
                    continue;
            }

            // An inner list is a member too, but only the tokens are hints
            var member = span[start..i].Trim([' ', '\t']);
            if (member.Length > 0 && member[0] == '(')
            {
                if (member[^1] != ')' && member.IndexOf(");", StringComparison.Ordinal) < 0)
                    return null;
            }
            else if (StructuredFields.ParseItem(member.ToString()) is not { } item)
            {
                return null;
            }
            else if (item.Value is StructuredFieldToken token && ClientHintToken.Get(token.Value) is { } hint)
            {
                set = set.Add(hint);
            }

            start = i + 1;
        }

        return inString ? null : set;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The hints of the environments
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The hints that the requests of a document can carry, before the low-entropy hints and the permissions policy
    /// (https://wicg.github.io/client-hints-infrastructure/#update-the-client-hints-set-from-cache): the entry of the Accept-CH
    /// cache of the origin of its top-level document, and the client hints set of that document (its <c>meta</c> elements).
    /// </summary>
    public static ClientHintsSet GetEnvironmentHints(Document document)
    {
        if (document.BrowsingContext?.Top is not { } top)
            return ClientHintsSet.Empty;

        var topDocument = top.Document;
        var hints = topDocument.ClientHintsSet;
        return topDocument.Origin.IsOpaque ? hints : hints.Union(top.AcceptCHCache.Get(topDocument.Origin.Serialize()));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Request processing
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://wicg.github.io/client-hints-infrastructure/#abstract-opdef-append-client-hints-to-request: appends the client
    /// hints of a request to the headers that are sent (not to the header list of the request, so they are computed again
    /// for the URL of a redirect: https://wicg.github.io/client-hints-infrastructure/#abstract-opdef-remove-client-hints-from-redirect-if-needed).
    /// The requests of the workers carry no hint: the permissions policy does not apply to them yet
    /// (https://w3c.github.io/webappsec-permissions-policy/#algo-should-request-be-allowed-to-use-feature).
    /// </summary>
    public static void AppendToRequest(FetchRequest request, List<KeyValuePair<string, string>> headers)
    {
        var url = request.CurrentUrl;
        if (url.Scheme is not ("http" or "https") || request.Mode == RequestMode.WebSocket || !ReferrerPolicies.IsPotentiallyTrustworthy(url))
            return;

        // The scripts of the dedicated and shared workers and, as in Chromium, of the service workers
        if (request.Destination is RequestDestination.Worker or RequestDestination.SharedWorker or RequestDestination.ServiceWorker)
            return;

        BrowsingContextOptions options;
        ClientHintsSet hints;
        Document? policyDocument;
        Document valuesDocument;
        Element? container = null;
        if (request.Mode == RequestMode.Navigate && request.Navigable is { } navigable)
        {
            options = navigable.Options;
            if (!IsEnabled(options))
                return;

            if (navigable.Parent is null)
            {
                // A top-level navigation: the hints that the origin of the URL opted into, without delegation. A sandboxed
                // navigable creates documents with an opaque origin, which have no entry in the cache.
                // The isolated partition of a cross-site navigational prefetch has no entry either.
                hints = navigable.HasSandboxedOriginFlag || request.UseIsolatedCredentials ? ClientHintsSet.Empty : navigable.AcceptCHCache.Get(url.SerializeOrigin());
                policyDocument = null;
                valuesDocument = navigable.Document;
            }
            else if (navigable.Container is { } navigableContainer)
            {
                // The navigation of a child navigable: the hints of the document of its container, delegated to the URL
                container = navigableContainer;
                policyDocument = container.NodeDocument;
                hints = GetEnvironmentHints(policyDocument);
                valuesDocument = navigable.Top.Document;
            }
            else
            {
                return;
            }
        }
        else
        {
            if (request.Client is not { WorkerGlobal: null, BrowsingContext: { } browsingContext } client)
                return;

            options = browsingContext.Options;
            if (!IsEnabled(options))
                return;

            policyDocument = client;
            hints = GetEnvironmentHints(client);
            valuesDocument = client;
        }

        hints = hints.Union(ClientHintToken.LowEntropy);

        // The origin that the hints are delegated to: the origin of the URL, or the opaque origin of the document that the
        // navigation of a sandboxed iframe would create (only the features whose allowlist is * are delegated to it)
        var origin = container is not null && request.Navigable is { HasSandboxedOriginFlag: true } ? OriginRecord.CreateOpaque() : OriginRecord.FromUrl(url);
        var containerPolicy = container is null ? null : PermissionsPolicyAlgorithms.ProcessPolicyAttributes(container);
        var values = new ValueProvider(options.ClientHints ?? ClientHintsOptions.Default, valuesDocument, request);
        foreach (var token in ClientHintToken.All)
        {
            if (!hints.Contains(token) || FetchHeaders.Contains(headers, token.HeaderName))
                continue;

            if (policyDocument is not null && !IsAllowedByPolicy(policyDocument, container, containerPolicy, token.Feature, origin))
                continue;

            if (values.Get(token.Value) is { } value)
            {
                headers.Add(new(token.HeaderName, value));
            }
        }
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-permissions-policy/#algo-should-request-be-allowed-to-use-feature: whether a request
    /// of a document to an origin is allowed to use the feature of a hint. For the navigation of an iframe whose
    /// <c>allow</c> attribute names the feature, it is the inherited policy of a document of the origin in the iframe.
    /// </summary>
    private static bool IsAllowedByPolicy(Document document, Element? container, Dictionary<PolicyControlledFeature, PermissionsPolicyAllowlist>? containerPolicy, PolicyControlledFeature feature, OriginRecord origin)
    {
        if (container is not null && containerPolicy is not null && containerPolicy.ContainsKey(feature))
            return PermissionsPolicyAlgorithms.DefineInheritedPolicy(feature, container, origin);

        return PermissionsPolicyAlgorithms.CheckPermissionsPolicy(document.PermissionsPolicyRecord, feature, origin, document.Origin);
    }

    /// <summary>https://wicg.github.io/client-hints-infrastructure/#find-client-hint-value: the values of the hints of a request (null: the hint is omitted).</summary>
    private struct ValueProvider(ClientHintsOptions options, Document document, FetchRequest request)
    {
        private Css.Om.MediaEnvironment? _environment;

        // The viewport of the document, and the preferences of the user (see BrowsingContextOptions.Viewport)
        private Css.Om.MediaEnvironment Environment => _environment ??= StyleEngine.CreateEnvironment(document);

        public string? Get(ClientHintValue value) => value switch
        {
            ClientHintValue.SaveData => options.SaveData ? "on" : null,
            ClientHintValue.UserAgent => SerializeBrands(options.Brands),
            ClientHintValue.UserAgentFullVersionList => SerializeBrands(options.FullVersionList),
            ClientHintValue.UserAgentMobile => SerializeBoolean(options.Mobile),
            ClientHintValue.UserAgentWow64 => SerializeBoolean(options.Wow64),
            ClientHintValue.UserAgentPlatform => SerializeString(options.Platform),
            ClientHintValue.UserAgentPlatformVersion => SerializeString(options.PlatformVersion),
            ClientHintValue.UserAgentArchitecture => SerializeString(options.Architecture),
            ClientHintValue.UserAgentBitness => SerializeString(options.Bitness),
            ClientHintValue.UserAgentModel => SerializeString(options.Model),
            ClientHintValue.UserAgentFullVersion => SerializeString(options.FullVersion),
            ClientHintValue.UserAgentFormFactors => SerializeStrings(options.FormFactors),
            ClientHintValue.DeviceMemory => options.DeviceMemory is { } memory && double.IsFinite(memory) && memory > 0 ? SerializeNumber(GetDeviceMemory(memory)) : null,
            ClientHintValue.RoundTripTime => options.RoundTripTime is { } roundTripTime ? SerializeNumber(Math.Min(3000, Math.Round(Math.Max(0, roundTripTime.TotalMilliseconds) / 50, MidpointRounding.AwayFromZero) * 50)) : null,
            ClientHintValue.Downlink => options.Downlink is { } downlink && double.IsFinite(downlink) ? SerializeNumber(Math.Min(10, Math.Round(Math.Max(0, downlink) * 20, MidpointRounding.AwayFromZero) / 20)) : null,
            ClientHintValue.EffectiveConnectionType => options.EffectiveConnectionType is { Length: > 0 } type && FetchHeaders.IsToken(type) ? type : null,
            ClientHintValue.DevicePixelRatio => SerializeNumber(Environment.DevicePixelRatio),
            ClientHintValue.ViewportWidth => SerializeNumber(Math.Round(Environment.Width, MidpointRounding.AwayFromZero)),
            ClientHintValue.ViewportHeight => SerializeNumber(Math.Round(Environment.Height, MidpointRounding.AwayFromZero)),
            ClientHintValue.PrefersColorScheme => SerializeString(Environment.PrefersColorScheme),
            ClientHintValue.PrefersReducedMotion => SerializeString(Environment.PrefersReducedMotion ? "reduce" : "no-preference"),
            ClientHintValue.PrefersReducedTransparency => SerializeString(Environment.PrefersReducedTransparency ? "reduce" : "no-preference"),
            ClientHintValue.Width => GetImageWidth(),
            _ => null,
        };

        /// <summary>
        /// https://wicg.github.io/responsive-image-client-hints/#sec-ch-width: the width of the image of an <c>img</c> element
        /// that has a source size (a <c>sizes</c> attribute), in device pixels. The other requests have no width.
        /// </summary>
        private string? GetImageWidth()
        {
            if (request.Destination != RequestDestination.Image || request.IsSpeculative || request.Initiator is not HtmlImageElement image)
                return null;

            return image.GetSourceSizeOfSizesAttribute() is { } sourceSize && double.IsFinite(sourceSize)
                ? SerializeNumber(Math.Ceiling(sourceSize * Environment.DevicePixelRatio))
                : null;
        }
    }

    /// <summary>https://www.w3.org/TR/device-memory/#computing-device-memory-value: a power of two between 0.25 and 8 GiB.</summary>
    private static double GetDeviceMemory(double memory)
    {
        var value = 0.25;
        while (value < 8 && value * 2 <= memory)
        {
            value *= 2;
        }

        return value;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The serialization of the structured fields (https://www.rfc-editor.org/rfc/rfc8941#name-serializing-structured-fiel)
    // ----------------------------------------------------------------------------------------------------------------

    private static string SerializeBoolean(bool value) => value ? "?1" : "?0";

    /// <summary>An integer or a decimal with three fractional digits at most.</summary>
    private static string SerializeNumber(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>A string, or null when it has a character that a structured field string cannot have (the hint is omitted).</summary>
    private static string? SerializeString(string? value)
    {
        value ??= "";
        var result = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            if (c is < ' ' or > '~')
                return null;

            if (c is '"' or '\\')
            {
                result.Append('\\');
            }

            result.Append(c);
        }

        return result.Append('"').ToString();
    }

    /// <summary>https://wicg.github.io/ua-client-hints/#create-a-brand-version-list: a list of strings with a <c>v</c> parameter.</summary>
    private static string? SerializeBrands(IReadOnlyList<NavigatorUABrandVersion> brands)
    {
        var result = new StringBuilder();
        foreach (var brand in brands)
        {
            if (SerializeString(brand.Brand) is not { } name || SerializeString(brand.Version) is not { } version)
                continue;

            if (result.Length > 0)
            {
                result.Append(", ");
            }

            result.Append(name).Append(";v=").Append(version);
        }

        return result.Length == 0 ? null : result.ToString();
    }

    /// <summary>https://wicg.github.io/ua-client-hints/#sec-ch-ua-form-factors: a list of strings, in lexical order.</summary>
    private static string? SerializeStrings(IReadOnlyList<string> values)
    {
        var serialized = new List<string>(values.Count);
        foreach (var value in values.Order(StringComparer.Ordinal))
        {
            if (SerializeString(value) is { } item)
            {
                serialized.Add(item);
            }
        }

        return serialized.Count == 0 ? null : string.Join(", ", serialized);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Clear-Site-Data
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://w3c.github.io/webappsec-clear-site-data/#clear-site-data-for-response, for the client hints only: the
    /// <c>"clientHints"</c>, <c>"cookies"</c>, <c>"cache"</c> and <c>"*"</c> types of the <c>Clear-Site-Data</c> header of a
    /// response of a potentially trustworthy URL remove the entry of the origin of the URL from the Accept-CH cache (the
    /// other data of the origin is not cleared: the header is not implemented otherwise).
    /// </summary>
    public static void ProcessClearSiteData(AcceptCHCache cache, UrlRecord url, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (!ReferrerPolicies.IsPotentiallyTrustworthy(url) || HeaderList.Get(headers, "Clear-Site-Data") is not { } value)
            return;

        // The types are quoted strings, separated by commas; the unknown types are ignored
        foreach (var member in value.Split(','))
        {
            if (member.Trim(' ', '\t') is "\"clientHints\"" or "\"cookies\"" or "\"cache\"" or "\"*\"")
            {
                cache.Remove(url.SerializeOrigin());
                return;
            }
        }
    }

    /// <summary>
    /// The <c>Clear-Site-Data</c> header of a response of the fetch algorithm. The responses of the navigations are processed
    /// with their <c>Accept-CH</c> header (see <see cref="BrowsingContext.ProcessNavigationResponseClientHints"/>), which the
    /// header clears too.
    /// </summary>
    public static void ProcessResponse(FetchRequest request, FetchResponse response)
    {
        if (request.Mode == RequestMode.Navigate || response.IsNetworkError || HeaderList.Get(response.HeaderList, "Clear-Site-Data") is null)
            return;

        // The cache is partitioned (only the top-level documents write it): the response clears the entry of its origin
        // when it is the origin of the top-level document of the client
        if (request.Client is { WorkerGlobal: null, BrowsingContext.Top: { } top } && IsEnabled(top.Options)
            && top.Document.Origin is { IsOpaque: false } topOrigin && topOrigin.Serialize() == request.CurrentUrl.SerializeOrigin())
        {
            ProcessClearSiteData(top.AcceptCHCache, request.CurrentUrl, response.HeaderList);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The meta elements
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://wicg.github.io/client-hints-infrastructure/#delegate-ch-algo: the <c>&lt;meta http-equiv="delegate-ch"&gt;</c>
    /// element of the top-level document of a secure context adds hints to the client hints set of the document (not to the
    /// Accept-CH cache), and delegates them to the origins it lists: <c>sec-ch-dpr https://cdn.example; sec-ch-width</c>. As in
    /// Chromium, <c>&lt;meta http-equiv="accept-ch"&gt;</c> (a list of hints, as the header) adds hints without delegation.
    /// The element is ignored once a <c>script</c>, <c>style</c> or <c>link</c> element (of a resource that is fetched) is
    /// in the document: the hints of the requests of a document do not change while it loads.
    /// </summary>
    public static void ProcessPragma(HtmlMetaElement meta, Document document, bool delegates)
    {
        if (meta.GetAttributeValue("content") is not { Length: > 0 } content)
            return;

        if (document.BrowsingContext is not { Parent: null } browsingContext || !ReferenceEquals(browsingContext.Document, document) || !IsEnabled(browsingContext.Options))
            return;

        if (document.RelevantWindow is not { IsSecureContext: true } || HasElementThatBeganToExecute(document))
            return;

        if (!delegates)
        {
            if (ParseTokenList(content) is { } hints)
            {
                document.ClientHintsSet = document.ClientHintsSet.Union(hints);
            }

            return;
        }

        // https://w3c.github.io/webappsec-permissions-policy/#algo-parse-policy-directive: the features are the hints, and
        // only the origins of their allowlists are kept
        DeclaredPermissionsPolicy? declared = null;
        foreach (var serializedDeclaration in content.Split(';'))
        {
            var tokens = serializedDeclaration.Split(AsciiWhitespace, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || ClientHintToken.Get(tokens[0]) is not { } hint)
                continue;

            document.ClientHintsSet = document.ClientHintsSet.Add(hint);

            // The allowlist of the feature in the declared policy of the document, or its default allowlist
            var policy = document.PermissionsPolicyRecord;
            var feature = hint.Feature;
            if (!policy.IsEnabledByInheritance(feature))
                continue;

            var hasDeclaration = (declared ?? policy.Declared).Declarations.TryGetValue(feature, out var allowlist);
            if (hasDeclaration ? allowlist!.IsAll : feature.IsAllowedForAllByDefault)
                continue;

            var origins = new List<string>();
            foreach (var element in tokens.AsSpan(1))
            {
                if (UrlParser.Parse(element, baseUrl: null) is { } url && OriginRecord.FromUrl(url) is { IsOpaque: false } target && !target.Host.Serialized.Contains('*', StringComparison.Ordinal))
                {
                    origins.Add(target.Serialize());
                }
            }

            if (origins.Count == 0)
                continue;

            // The declared policy is copied before it is changed: the empty policy is shared by the documents
            if (declared is null)
            {
                declared = new DeclaredPermissionsPolicy();
                foreach (var (declaredFeature, declaredAllowlist) in policy.Declared.Declarations)
                {
                    declared.Declarations[declaredFeature] = declaredAllowlist;
                }

                foreach (var (declaredFeature, endpoint) in policy.Declared.ReportingConfiguration)
                {
                    declared.ReportingConfiguration[declaredFeature] = endpoint;
                }
            }

            var extended = new PermissionsPolicyAllowlist { SelfOrigin = hasDeclaration ? allowlist!.SelfOrigin : document.Origin, SrcOrigin = hasDeclaration ? allowlist!.SrcOrigin : null };
            if (hasDeclaration)
            {
                extended.Expressions.AddRange(allowlist!.Expressions);
            }

            extended.Expressions.AddRange(origins);
            declared.Declarations[feature] = extended;
        }

        if (declared is not null)
        {
            document.PermissionsPolicyRecord = document.PermissionsPolicyRecord.WithDeclared(declared);
        }
    }

    /// <summary>
    /// Whether a <c>script</c> or <c>style</c> element, or a <c>link</c> element whose resource is fetched, is in the document
    /// (https://wicg.github.io/client-hints-infrastructure/#delegate-ch-algo, step 2: "any link, style, or script elements have
    /// begun to execute"). The state is remembered: removing the element does not undo it.
    /// </summary>
    private static bool HasElementThatBeganToExecute(Document document)
    {
        if (document.ClientHintsPragmasAreIgnored)
            return true;

        foreach (var node in document.Descendants())
        {
            if (node is not Element element)
                continue;

            var began = element.LocalName switch
            {
                "script" or "style" => element.NamespaceUri is Namespaces.Html or Namespaces.Svg,
                "link" => element is HtmlLinkElement && element.GetAttributeValue("rel") is { } rel && Array.Exists(rel.Split(AsciiWhitespace, StringSplitOptions.RemoveEmptyEntries), IsFetchingLinkType),
                _ => false,
            };

            if (began)
            {
                document.ClientHintsPragmasAreIgnored = true;
                return true;
            }
        }

        return false;
    }

    private static bool IsFetchingLinkType(string keyword)
        => InfraAscii.EqualsIgnoreAsciiCase(keyword, "stylesheet") || InfraAscii.EqualsIgnoreAsciiCase(keyword, "preload") || InfraAscii.EqualsIgnoreAsciiCase(keyword, "modulepreload")
            || InfraAscii.EqualsIgnoreAsciiCase(keyword, "prefetch");
}
