using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Http.Fuzz;

/// <summary>
/// A <c>Strict-Transport-Security</c> header that conforms to RFC 6797 must set the policy it describes, and any other
/// header must leave the policy alone or set one it can describe. The HSTS policies must agree with a dictionary that
/// models them, and evicting policies must only take hosts off. The links of a <c>Link</c> header must parse again to
/// the same links once written in a canonical form.
/// </summary>
public static class HttpFuzzTargets
{
    private const long MaxMaxAgeInSeconds = 100L * 365 * 24 * 60 * 60;

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] HstsTokens = ["max-age", "max-age=", "includeSubDomains", "preload", ";", "; ", "=", "\"", "\\", "0", "31536000", "99999999999999999999", " ", "\t", "MAX-AGE", "includesubdomains"];

    private static readonly string[] LinkTokens = ["<", ">", ";", ",", "=", "\"", "\\", " ", "\t", "\r\n", "rel", "rel=next", "rel=\"prev\"", "title*=UTF-8''", "anchor", "https://example.com/", "</a>; rel=next"];

    // The labels of the hosts of the policy scripts: a few domains, with case variants, and names that are not valid hosts
    private static readonly string[] Labels = ["a", "b", "example", "com", "org", "co", "uk", "EXAMPLE", "Com", "xn--bcher-kva", "", "-", "a-b", "123", "localhost", "\u00E9", "\uFF21", "a b"];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("http-hsts-header", HstsHeader) { Seeds = GetHstsHeaderSeeds, Dictionary = HstsTokens },
        new("http-hsts-policies", HstsPolicies) { Seeds = GetHstsPoliciesSeeds },
        new("http-link-header", LinkHeader) { Seeds = GetLinkHeaderSeeds, Dictionary = LinkTokens },
    ];

    /// <summary>Sends a request whose response has the header, and reads the policy it leaves for the host.</summary>
    private static void HstsHeader(ReadOnlySpan<byte> data)
    {
        var header = FuzzText.Decode(data);
        var clock = new ManualClock(Start);
        var policies = new HstsDomainPolicyCollection(clock, includePreloadDomains: false);

        // A known policy shows a header that is ignored (it stays) and one with max-age=0 (it is removed)
        var initialExpiration = Start.AddDays(1);
        policies.Add("example.com", initialExpiration, includeSubdomains: false);

        var received = SendAsync(policies, header).GetAwaiter().GetResult();

        var hasPolicy = policies.TryGetPolicy("example.com", out var policy);
        var isUnchanged = hasPolicy && policy!.ExpiresAt == initialExpiration && !policy.IncludeSubdomains;
        FuzzAssert.Equal(hasPolicy && policy!.IncludeSubdomains, policies.MustUpgradeRequest("www.example.com"), "MustUpgradeRequest does not agree with the policy for a subdomain");
        if (received is null)
        {
            FuzzAssert.True(isUnchanged, "A response without the header changes the policy");
            return;
        }

        if (!isUnchanged)
        {
            FuzzAssert.True(received.Contains("max-age", StringComparison.OrdinalIgnoreCase), "A header without max-age changes the policy");
            FuzzAssert.True(!hasPolicy || !policy!.IncludeSubdomains || received.Contains("includeSubDomains", StringComparison.OrdinalIgnoreCase), "A header without includeSubDomains sets a policy that includes the subdomains");
        }

        // The header the handler read is the one the response has; HttpHeaders may have changed the value
        if (received != header)
            return;

        var expected = ParseStrictTransportSecurity(header);
        switch (expected.Kind)
        {
            case HeaderKind.Valid when expected.MaxAge == 0:
                FuzzAssert.False(hasPolicy, $"The valid header '{header}' with max-age=0 does not remove the policy");
                break;

            case HeaderKind.Valid:
                FuzzAssert.True(hasPolicy, $"The valid header '{header}' removes the policy");
                FuzzAssert.Equal(Start.AddSeconds(Math.Min(expected.MaxAge, MaxMaxAgeInSeconds)), policy!.ExpiresAt, $"The valid header '{header}' sets another expiration date");
                FuzzAssert.Equal(expected.IncludeSubdomains, policy.IncludeSubdomains, $"The valid header '{header}' sets another includeSubDomains");
                break;

            case HeaderKind.Invalid:
                FuzzAssert.True(isUnchanged, $"The invalid header '{header}' changes the policy");
                break;
        }
    }

    /// <summary>Sends a request to https://example.com/ whose response has the header, and gives the header the handler reads.</summary>
    private static async Task<string?> SendAsync(HstsDomainPolicyCollection policies, string header)
    {
        using var inner = new HeaderHandler(header);
        using var handler = new HstsClientHandler(inner, policies);
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        using var response = await invoker.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
        return response.Headers.TryGetValues("Strict-Transport-Security", out var values) ? values.First() : null;
    }

    /// <summary>
    /// Runs a script of operations (add, remove, look up, move the clock forward, clear) on collections of policies, and
    /// checks the answers against a dictionary of the policies.
    /// </summary>
    private static void HstsPolicies(ReadOnlySpan<byte> data)
    {
        var clock = new ManualClock(Start);
        var policies = new HstsDomainPolicyCollection(clock, includePreloadDomains: false);
        var maxLearnedPolicies = (FuzzInput.ReadByte(ref data) % 8) + 1;
        var bounded = new HstsDomainPolicyCollection(clock, includePreloadDomains: false, maxLearnedPolicies);
        var preloaded = new HstsDomainPolicyCollection(clock, includePreloadDomains: true);
        var model = new Dictionary<string, (DateTimeOffset ExpiresAt, bool IncludeSubdomains)>(StringComparer.Ordinal);

        // A host that is not ASCII is converted to Punycode, which the model does not do: once one is added, the
        // model only checks that the operations do not fail
        var isModelExact = true;
        for (var operations = 0; !data.IsEmpty && operations < 512; operations++)
        {
            var operation = FuzzInput.ReadByte(ref data);
            var host = ReadHost(ref data);
            var key = GetModelKey(host);
            switch (operation % 8)
            {
                case 0 or 1:
                    {
                        var includeSubdomains = (operation & 0x10) != 0;
                        var minutes = (sbyte)FuzzInput.ReadByte(ref data);
                        var expiresAt = clock.Now.AddMinutes(minutes);
                        if (!TryAdd(policies, host, expiresAt, includeSubdomains, useMaxAge: operation % 8 == 0, clock))
                        {
                            FuzzAssert.False(TryAdd(bounded, host, expiresAt, includeSubdomains, useMaxAge: false, clock), $"The bounded collection accepts the host '{host}'");
                            break;
                        }

                        FuzzAssert.True(TryAdd(bounded, host, expiresAt, includeSubdomains, useMaxAge: false, clock), $"The bounded collection rejects the host '{host}'");
                        FuzzAssert.True(TryAdd(preloaded, host, expiresAt, includeSubdomains, useMaxAge: false, clock), $"The preloaded collection rejects the host '{host}'");
                        if (key is null)
                        {
                            isModelExact = false;
                        }
                        else
                        {
                            model[key] = (expiresAt, includeSubdomains);
                        }

                        FuzzAssert.True(bounded.Count() <= maxLearnedPolicies, $"The bounded collection keeps more than {maxLearnedPolicies} policies");
                        break;
                    }

                case 2:
                    {
                        var removed = policies.Remove(host);
                        bounded.Remove(host);

                        // A host that is not ASCII can be the Punycode or the ASCII form of a policy of the model
                        if (key is null && removed)
                        {
                            isModelExact = false;
                        }
                        else if (isModelExact && key is not null)
                        {
                            if (!model.Remove(key, out var removedPolicy))
                            {
                                FuzzAssert.False(removed, $"Remove removes a policy for '{host}', which has none");
                            }
                            else if (removedPolicy.ExpiresAt >= clock.Now)
                            {
                                // An expired policy may already be gone
                                FuzzAssert.True(removed, $"Remove does not remove the policy of '{host}'");
                            }
                        }

                        break;
                    }

                case 3 or 4:
                    {
                        var mustUpgrade = operation % 8 == 3 ? policies.MustUpgradeRequest(host) : policies.MustUpgradeRequest(host.AsSpan());
                        FuzzAssert.Equal(mustUpgrade, policies.MustUpgradeRequest(host.AsSpan()), $"The overloads of MustUpgradeRequest do not agree for '{host}'");
                        if (isModelExact && key is not null)
                        {
                            FuzzAssert.Equal(ModelMustUpgrade(model, key, clock.Now), mustUpgrade, $"MustUpgradeRequest('{host}') does not agree with the model");
                        }

                        FuzzAssert.True(!bounded.MustUpgradeRequest(host) || mustUpgrade, $"The bounded collection upgrades '{host}', which the collection does not");
                        FuzzAssert.True(!mustUpgrade || preloaded.MustUpgradeRequest(host), $"The preloaded collection does not upgrade '{host}'");
                        break;
                    }

                case 5:
                    {
                        var hasPolicy = policies.TryGetPolicy(host, out var policy);
                        if (isModelExact && key is not null)
                        {
                            var expected = model.TryGetValue(key, out var modelPolicy) && modelPolicy.ExpiresAt >= clock.Now;
                            FuzzAssert.Equal(expected, hasPolicy, $"TryGetPolicy('{host}') does not agree with the model");
                            if (expected)
                            {
                                FuzzAssert.Equal(key, policy!.Host, $"The policy of '{host}' has another host");
                                FuzzAssert.Equal(modelPolicy.ExpiresAt, policy.ExpiresAt, $"The policy of '{host}' has another expiration date");
                                FuzzAssert.Equal(modelPolicy.IncludeSubdomains, policy.IncludeSubdomains, $"The policy of '{host}' has another includeSubDomains");
                                FuzzAssert.False(policy.IsPreloaded, $"The policy of '{host}' is preloaded");
                            }
                        }

                        _ = preloaded.TryGetPolicy(host, out _);
                        break;
                    }

                case 6:
                    clock.Now = clock.Now.AddMinutes(FuzzInput.ReadByte(ref data));
                    break;

                default:
                    policies.ClearLearnedPolicies();
                    bounded.ClearLearnedPolicies();
                    preloaded.ClearLearnedPolicies();
                    model.Clear();
                    isModelExact = true;
                    break;
            }
        }

        if (!isModelExact)
            return;

        // The enumeration may still have expired policies, but has every policy in effect
        var enumerated = policies.ToDictionary(policy => policy.Host, StringComparer.Ordinal);
        foreach (var (host, policy) in enumerated)
        {
            FuzzAssert.True(model.ContainsKey(host), $"The enumeration has the policy of '{host}', which the model does not have");
        }

        foreach (var (host, policy) in model.Where(entry => entry.Value.ExpiresAt >= clock.Now))
        {
            FuzzAssert.True(enumerated.ContainsKey(host), $"The enumeration does not have the policy of '{host}'");
        }
    }

    /// <summary>Parses a Link header, writes its links in a canonical form, and parses them again.</summary>
    private static void LinkHeader(ReadOnlySpan<byte> data)
    {
        var header = FuzzText.Decode(data);
        var links = LinkHeaderValue.Parse(header).ToList();
        FuzzAssert.True(links.Count <= header.AsSpan().Count('<'), "There are more links than '<'");
        foreach (var link in links)
        {
            FuzzAssert.True(header.Contains(link.Url, StringComparison.Ordinal), $"The URL '{link.Url}' is not in the header");
            FuzzAssert.False(link.Url.AsSpan().ContainsAny('<', '>'), $"The URL '{link.Url}' has a '<' or a '>'");
            FuzzAssert.Equal(link.Url.Trim(), link.Url, "The URL is not trimmed");
            FuzzAssert.Equal(link.GetParameterValue("rel") ?? "", link.Rel, "Rel is not the rel parameter");
            foreach (var (name, _) in link.Parameters)
            {
                FuzzAssert.True(name.Length > 0, "A parameter has no name");
                FuzzAssert.False(name.AsSpan().ContainsAny(" \t\r\n=;,"), $"The parameter name '{name}' has a separator");
                FuzzAssert.Equal(name.ToLowerInvariant(), name, $"The parameter name '{name}' is not lowercase");
            }
        }

        var formatted = Format(links);
        var reparsed = LinkHeaderValue.Parse(formatted).ToList();
        FuzzAssert.Equal(formatted, Format(reparsed), "The links written in a canonical form parse to other links");

        using var response = new HttpResponseMessage();
        if (response.Headers.TryAddWithoutValidation("Link", header) && response.Headers.TryGetValues("Link", out var values) && values.SingleOrDefault() == header)
        {
            FuzzAssert.Equal(formatted, Format([.. LinkHeaderValue.Parse(response)]), "The links of the response are not the links of the header");
        }
    }

    private static string Format(List<LinkHeaderValue> links)
    {
        var result = new StringBuilder();
        foreach (var link in links)
        {
            if (result.Length > 0)
            {
                result.Append(", ");
            }

            result.Append('<').Append(link.Url).Append('>');
            foreach (var (name, value) in link.Parameters)
            {
                result.Append("; ").Append(name).Append("=\"").Append(value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
            }
        }

        return result.ToString();
    }

    private static bool TryAdd(HstsDomainPolicyCollection policies, string host, DateTimeOffset expiresAt, bool includeSubdomains, bool useMaxAge, ManualClock clock)
    {
        try
        {
            if (useMaxAge)
            {
                policies.Add(host, expiresAt - clock.Now, includeSubdomains);
            }
            else
            {
                policies.Add(host, expiresAt, includeSubdomains);
            }

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string ReadHost(ref ReadOnlySpan<byte> data)
    {
        // 1 to 4 labels, and up to 2 trailing dots
        var shape = FuzzInput.ReadByte(ref data);
        var host = new StringBuilder();
        for (var i = 0; i <= (shape & 3); i++)
        {
            if (i > 0)
            {
                host.Append('.');
            }

            host.Append(FuzzInput.Pick(Labels, FuzzInput.ReadByte(ref data)));
        }

        host.Append('.', (shape >> 2) % 3);
        return host.ToString();
    }

    /// <summary>Gets the key of an ASCII host in the model: lowercase, without its trailing dots.</summary>
    private static string? GetModelKey(string host)
    {
        if (!Ascii.IsValid(host))
            return null;

        return host.TrimEnd('.').ToLowerInvariant();
    }

    /// <summary>A host is upgraded when a policy in effect is for the host itself, or for a parent domain and includes the subdomains.</summary>
    private static bool ModelMustUpgrade(Dictionary<string, (DateTimeOffset ExpiresAt, bool IncludeSubdomains)> model, string host, DateTimeOffset now)
    {
        var start = 0;
        while (true)
        {
            if (model.TryGetValue(host[start..], out var policy) && policy.ExpiresAt >= now && (policy.IncludeSubdomains || start == 0))
                return true;

            var dot = host.AsSpan(start).IndexOf('.');
            if (dot < 0)
                return false;

            start += dot + 1;
        }
    }

    /// <summary>
    /// Parses a header with the grammar of RFC 6797 (section 6.1) and RFC 7230 (tokens, quoted strings, optional
    /// whitespace): <see cref="HeaderKind.Unknown"/> is a header that does not follow the grammar, or that the RFC
    /// leaves open, such as a repeated extension directive or an escape in max-age.
    /// </summary>
    private static (HeaderKind Kind, long MaxAge, bool IncludeSubdomains) ParseStrictTransportSecurity(string header)
    {
        var directives = new List<(string Name, string? Value, bool HasEscape)>();
        var position = 0;
        while (true)
        {
            SkipWhitespace(header, ref position);
            if (position == header.Length)
                break;

            if (header[position] == ';')
            {
                position++;
                continue;
            }

            var name = ReadToken(header, ref position);
            if (name is null)
                return default;

            SkipWhitespace(header, ref position);
            string? value = null;
            var hasEscape = false;
            if (position < header.Length && header[position] == '=')
            {
                position++;
                SkipWhitespace(header, ref position);
                value = position < header.Length && header[position] == '"' ? ReadQuotedString(header, ref position, out hasEscape) : ReadToken(header, ref position);
                if (value is null)
                    return default;

                SkipWhitespace(header, ref position);
            }

            if (position < header.Length && header[position] != ';')
                return default;

            directives.Add((name.ToLowerInvariant(), value, hasEscape));
        }

        var maxAge = directives.Where(directive => directive.Name == "max-age").ToList();
        var includeSubdomains = directives.Where(directive => directive.Name == "includesubdomains").ToList();
        if (maxAge.Count > 1 || includeSubdomains.Count > 1)
            return (HeaderKind.Invalid, 0, false);

        if (directives.GroupBy(directive => directive.Name, StringComparer.Ordinal).Any(group => group.Count() > 1) || includeSubdomains.Any(directive => directive.Value is not null))
            return default;

        if (maxAge.Count == 0)
            return (HeaderKind.Invalid, 0, false);

        var (_, maxAgeValue, maxAgeHasEscape) = maxAge[0];
        if (maxAgeHasEscape || maxAgeValue is null)
            return default;

        if (maxAgeValue.Length == 0 || maxAgeValue.AsSpan().ContainsAnyExceptInRange('0', '9'))
            return (HeaderKind.Invalid, 0, false);

        // delta-seconds has no upper bound
        var seconds = maxAgeValue.TrimStart('0').Length > 18 ? long.MaxValue : long.Parse(maxAgeValue, System.Globalization.CultureInfo.InvariantCulture);
        return (HeaderKind.Valid, seconds, includeSubdomains.Count == 1);

        static void SkipWhitespace(string header, ref int position)
        {
            while (position < header.Length && header[position] is ' ' or '\t')
            {
                position++;
            }
        }

        static string? ReadToken(string header, ref int position)
        {
            var start = position;
            while (position < header.Length && IsTokenCharacter(header[position]))
            {
                position++;
            }

            return position == start ? null : header[start..position];
        }

        static string? ReadQuotedString(string header, ref int position, out bool hasEscape)
        {
            hasEscape = false;
            var value = new StringBuilder();
            position++;
            while (position < header.Length)
            {
                var c = header[position++];
                if (c == '"')
                    return value.ToString();

                if (c == '\\')
                {
                    if (position == header.Length || !IsQuotedPairCharacter(header[position]))
                        return null;

                    hasEscape = true;
                    value.Append(header[position++]);
                }
                else if (c is '\t' or ' ' or '!' or (>= '\x23' and <= '\x5B') or (>= '\x5D' and <= '\x7E') or (>= '\x80' and <= '\xFF'))
                {
                    value.Append(c);
                }
                else
                {
                    return null;
                }
            }

            return null;
        }

        static bool IsTokenCharacter(char c) => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c, StringComparison.Ordinal);

        static bool IsQuotedPairCharacter(char c) => c is '\t' or ' ' or (>= '\x21' and <= '\x7E') or (>= '\x80' and <= '\xFF');
    }

    private static IEnumerable<FuzzSeed> GetHstsHeaderSeeds()
    {
        string[] headers =
        [
            "max-age=31536000",
            "max-age=31536000; includeSubDomains",
            "max-age=31536000; includeSubDomains; preload",
            "max-age=0",
            "MAX-AGE = \"600\" ; INCLUDESUBDOMAINS",
            "includeSubDomains; max-age=63072000",
            "max-age=1; max-age=2",
            "max-age=10; includeSubDomains; includeSubDomains",
            "max-age=99999999999999999999",
            "max-age=abc",
            "max-age=",
            "max-age=60; ext=\"a;includeSubDomains\"",
            "max-age=60; ext=\"a\\\"b\"",
            ";; max-age=60 ;",
            "includeSubDomains",
            "",
        ];
        return headers.Select((header, index) => FuzzSeed.FromText($"header-{index}", header));
    }

    private static IEnumerable<FuzzSeed> GetHstsPoliciesSeeds()
    {
        // The limit, then operations: an operation byte, a host (shape, labels) and the operands of the operation
        yield return new FuzzSeed("add-then-look-up", (byte[])[3, 0x10, 1, 2, 3, 60, 3, 2, 0, 2, 3, 5, 1, 2, 3, 4, 1, 2, 3]);
        yield return new FuzzSeed("expire", (byte[])[1, 0, 0, 2, 10, 6, 0, 0, 20, 3, 0, 2, 5, 0, 2]);
        yield return new FuzzSeed("subdomains", (byte[])[2, 0x10, 1, 2, 3, 100, 0, 1, 3, 100, 3, 2, 0, 2, 3, 3, 3, 1, 2, 3, 2, 1, 2, 3, 3, 2, 0, 2, 3]);
        yield return new FuzzSeed("evict", (byte[])[0, 0, 0, 0, 10, 0, 0, 1, 10, 0, 0, 4, 10, 0, 1, 1, 3, 10, 3, 0, 0, 3, 0, 1, 7, 0, 0]);
        yield return new FuzzSeed("idn", (byte[])[4, 0, 1, 15, 3, 20, 3, 1, 9, 3, 5, 1, 16, 3, 7, 0, 0]);
    }

    private static IEnumerable<FuzzSeed> GetLinkHeaderSeeds()
    {
        string[] headers =
        [
            "</style.css>; rel=preload; as=style; fetchpriority=\"high\"",
            "<sample>; rel=abc, <plop>; rel\t=\"d\\\"e;f,\"; title = test title; abc",
            "<a>; rel=prev, <b>;rel=next",
            "<https://api.github.com/repositories/1/issues?page=2>; rel=\"next\", <https://api.github.com/repositories/1/issues?page=5>; rel=\"last\"",
            "<http://example.org/>; rel=\"start http://example.net/relation/other\"",
            "<a>; abc\r\n; def",
            "<a>; Rel=next; TITLE=hello",
            "<a,b>; rel=\"x\\\\\", junk, <c",
            "<>; =v; ;; rel=",
            "</TheBook/chapter2>; rel=\"previous\"; title*=UTF-8'de'letztes%20Kapitel",
        ];
        return headers.Select((header, index) => FuzzSeed.FromText($"link-{index}", header));
    }

    private enum HeaderKind
    {
        Unknown,
        Valid,
        Invalid,
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Answers every request with a response that has the header.</summary>
    private sealed class HeaderHandler(string header) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { RequestMessage = request };
            response.Headers.TryAddWithoutValidation("Strict-Transport-Security", header);
            return Task.FromResult(response);
        }
    }
}
