using System.Collections.Frozen;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The value that a client hint gives to a server (https://wicg.github.io/client-hints-infrastructure/#find-client-hint-value).</summary>
internal enum ClientHintValue
{
    SaveData,
    DevicePixelRatio,
    Width,
    ViewportWidth,
    ViewportHeight,
    DeviceMemory,
    RoundTripTime,
    Downlink,
    EffectiveConnectionType,
    PrefersColorScheme,
    PrefersReducedMotion,
    PrefersReducedTransparency,
    UserAgent,
    UserAgentArchitecture,
    UserAgentBitness,
    UserAgentFormFactors,
    UserAgentFullVersion,
    UserAgentFullVersionList,
    UserAgentMobile,
    UserAgentModel,
    UserAgentPlatform,
    UserAgentPlatformVersion,
    UserAgentWow64,
}

/// <summary>
/// A client hints token (https://wicg.github.io/client-hints-infrastructure/#client-hints-token-definition): the name of a
/// request header that a server opts into with the <c>Accept-CH</c> header. The registry has the tokens of the
/// specification, and the legacy tokens that Chromium still accepts and that the web-platform-tests use (<c>DPR</c>,
/// <c>Width</c>, <c>Viewport-Width</c>, <c>Device-Memory</c>, <c>RTT</c>, <c>Downlink</c>, <c>ECT</c>): a legacy token sends
/// the header without the <c>Sec-CH-</c> prefix, with the same value and the same policy-controlled feature.
/// </summary>
internal sealed class ClientHintToken
{
    private ClientHintToken(int index, string headerName, ClientHintValue value, PolicyControlledFeature feature, bool isLowEntropy)
    {
        Index = index;
        HeaderName = headerName;
        Name = headerName.ToLowerInvariant();
        Value = value;
        Feature = feature;
        IsLowEntropy = isLowEntropy;
    }

    /// <summary>The position of the token in <see cref="All"/>: its bit in a <see cref="ClientHintsSet"/>.</summary>
    public int Index { get; }

    /// <summary>The token: the name of the header, in lowercase (e.g. <c>sec-ch-ua-platform</c>).</summary>
    public string Name { get; }

    /// <summary>The name of the request header (e.g. <c>Sec-CH-UA-Platform</c>).</summary>
    public string HeaderName { get; }

    public ClientHintValue Value { get; }

    /// <summary>The policy-controlled feature of the hint (https://wicg.github.io/client-hints-infrastructure/#policy-controlled-features), which delegates it to the other origins.</summary>
    public PolicyControlledFeature Feature { get; }

    /// <summary>Whether the hint is in the low-entropy hint table (https://wicg.github.io/client-hints-infrastructure/#low-entropy-hint-table): it is sent without opt-in.</summary>
    public bool IsLowEntropy { get; }

    /// <summary>The tokens, in the order the headers are appended to a request.</summary>
    public static IReadOnlyList<ClientHintToken> All { get; } = Create();

    private static readonly FrozenDictionary<string, ClientHintToken> ByName = All.ToFrozenDictionary(token => token.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The low-entropy hints.</summary>
    public static ClientHintsSet LowEntropy { get; } = ClientHintsSet.Create(All.Where(token => token.IsLowEntropy));

    /// <summary>The token of a name (ASCII case-insensitive), or null.</summary>
    public static ClientHintToken? Get(string name) => ByName.GetValueOrDefault(name);

    private static ClientHintToken[] Create()
    {
        var tokens = new List<ClientHintToken>();
        Add("Sec-CH-UA", ClientHintValue.UserAgent, "ch-ua", isLowEntropy: true);
        Add("Sec-CH-UA-Mobile", ClientHintValue.UserAgentMobile, "ch-ua-mobile", isLowEntropy: true);
        Add("Sec-CH-UA-Platform", ClientHintValue.UserAgentPlatform, "ch-ua-platform", isLowEntropy: true);
        Add("Save-Data", ClientHintValue.SaveData, "ch-save-data", isLowEntropy: true);
        Add("Sec-CH-UA-Arch", ClientHintValue.UserAgentArchitecture, "ch-ua-arch");
        Add("Sec-CH-UA-Bitness", ClientHintValue.UserAgentBitness, "ch-ua-bitness");
        Add("Sec-CH-UA-Form-Factors", ClientHintValue.UserAgentFormFactors, "ch-ua-form-factors");
        Add("Sec-CH-UA-Full-Version", ClientHintValue.UserAgentFullVersion, "ch-ua-full-version");
        Add("Sec-CH-UA-Full-Version-List", ClientHintValue.UserAgentFullVersionList, "ch-ua-full-version-list");
        Add("Sec-CH-UA-Model", ClientHintValue.UserAgentModel, "ch-ua-model");
        Add("Sec-CH-UA-Platform-Version", ClientHintValue.UserAgentPlatformVersion, "ch-ua-platform-version");
        Add("Sec-CH-UA-WoW64", ClientHintValue.UserAgentWow64, "ch-ua-wow64");
        Add("Sec-CH-Device-Memory", ClientHintValue.DeviceMemory, "ch-device-memory");
        Add("Device-Memory", ClientHintValue.DeviceMemory, "ch-device-memory");
        Add("Sec-CH-DPR", ClientHintValue.DevicePixelRatio, "ch-dpr");
        Add("DPR", ClientHintValue.DevicePixelRatio, "ch-dpr");
        Add("Sec-CH-Width", ClientHintValue.Width, "ch-width");
        Add("Width", ClientHintValue.Width, "ch-width");
        Add("Sec-CH-Viewport-Width", ClientHintValue.ViewportWidth, "ch-viewport-width");
        Add("Viewport-Width", ClientHintValue.ViewportWidth, "ch-viewport-width");
        Add("Sec-CH-Viewport-Height", ClientHintValue.ViewportHeight, "ch-viewport-height");
        Add("Sec-CH-RTT", ClientHintValue.RoundTripTime, "ch-rtt");
        Add("RTT", ClientHintValue.RoundTripTime, "ch-rtt");
        Add("Sec-CH-Downlink", ClientHintValue.Downlink, "ch-downlink");
        Add("Downlink", ClientHintValue.Downlink, "ch-downlink");
        Add("Sec-CH-ECT", ClientHintValue.EffectiveConnectionType, "ch-ect");
        Add("ECT", ClientHintValue.EffectiveConnectionType, "ch-ect");
        Add("Sec-CH-Prefers-Color-Scheme", ClientHintValue.PrefersColorScheme, "ch-prefers-color-scheme");
        Add("Sec-CH-Prefers-Reduced-Motion", ClientHintValue.PrefersReducedMotion, "ch-prefers-reduced-motion");
        Add("Sec-CH-Prefers-Reduced-Transparency", ClientHintValue.PrefersReducedTransparency, "ch-prefers-reduced-transparency");
        return [.. tokens];

        void Add(string headerName, ClientHintValue value, string feature, bool isLowEntropy = false)
            => tokens.Add(new ClientHintToken(tokens.Count, headerName, value, PolicyControlledFeature.Get(feature) ?? throw new InvalidOperationException($"The feature '{feature}' is not registered"), isLowEntropy));
    }

    public override string ToString() => Name;
}

/// <summary>A client hints set (https://wicg.github.io/client-hints-infrastructure/#client-hints-set): a set of client hints tokens.</summary>
internal readonly record struct ClientHintsSet(ulong Bits)
{
    public static ClientHintsSet Empty => default;

    public bool IsEmpty => Bits == 0;

    public bool Contains(ClientHintToken token) => (Bits & (1UL << token.Index)) != 0;

    public ClientHintsSet Add(ClientHintToken token) => new(Bits | (1UL << token.Index));

    public ClientHintsSet Union(ClientHintsSet other) => new(Bits | other.Bits);

    /// <summary>The tokens of the set that are not in <paramref name="other"/>.</summary>
    public ClientHintsSet Except(ClientHintsSet other) => new(Bits & ~other.Bits);

    public bool Overlaps(ClientHintsSet other) => (Bits & other.Bits) != 0;

    public static ClientHintsSet Create(IEnumerable<ClientHintToken> tokens)
    {
        var set = Empty;
        foreach (var token in tokens)
        {
            set = set.Add(token);
        }

        return set;
    }

    /// <summary>The set of the tokens named by <paramref name="names"/>: the names that are not client hints tokens are ignored.</summary>
    public static ClientHintsSet Create(IEnumerable<string> names)
    {
        var set = Empty;
        foreach (var name in names)
        {
            if (ClientHintToken.Get(name) is { } token)
            {
                set = set.Add(token);
            }
        }

        return set;
    }

    /// <summary>The names of the tokens of the set, in the order of the registry.</summary>
    public string[] ToNames()
    {
        var names = new List<string>();
        foreach (var token in ClientHintToken.All)
        {
            if (Contains(token))
            {
                names.Add(token.Name);
            }
        }

        return [.. names];
    }
}
