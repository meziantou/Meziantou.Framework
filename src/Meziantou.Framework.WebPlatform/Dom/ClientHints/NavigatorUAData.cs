namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A brand of the user agent and its version (https://wicg.github.io/ua-client-hints/#dictdef-navigatoruabrandversion).</summary>
[IdlInterface("NavigatorUABrandVersion", "https://wicg.github.io/ua-client-hints/#dictdef-navigatoruabrandversion")]
public sealed class NavigatorUABrandVersion
{
    /// <summary>Gets or sets the brand: the commercial name of the user agent, or of an equivalence class (e.g. its rendering engine).</summary>
    [IdlMember("brand", "https://wicg.github.io/ua-client-hints/#dom-navigatoruabrandversion-brand")]
    public string? Brand { get; init; }

    /// <summary>Gets or sets the version of the brand.</summary>
    [IdlMember("version", "https://wicg.github.io/ua-client-hints/#dom-navigatoruabrandversion-version")]
    public string? Version { get; init; }
}

/// <summary>
/// The values returned by <see cref="NavigatorUAData.GetHighEntropyValues"/> (https://wicg.github.io/ua-client-hints/#dictdef-uadatavalues):
/// the low-entropy values, and the high-entropy values that were requested (the other members are null).
/// </summary>
[IdlInterface("UADataValues", "https://wicg.github.io/ua-client-hints/#dictdef-uadatavalues")]
public sealed class UADataValues
{
    /// <summary>Gets or sets the architecture of the platform (<c>x86</c>, <c>arm</c> or the empty string).</summary>
    [IdlMember("architecture", "https://wicg.github.io/ua-client-hints/#dom-uadatavalues-architecture")]
    public string? Architecture { get; init; }

    /// <summary>Gets or sets the bitness of the architecture of the platform (<c>32</c>, <c>64</c> or the empty string).</summary>
    [IdlMember("bitness", "https://wicg.github.io/ua-client-hints/#dom-uadatavalues-bitness")]
    public string? Bitness { get; init; }

    /// <summary>Gets or sets the brands of the user agent and their significant versions.</summary>
    [IdlMember("brands", "https://wicg.github.io/ua-client-hints/#dom-uadatavalues-brands")]
    public IReadOnlyList<NavigatorUABrandVersion>? Brands { get; init; }

    /// <summary>Gets or sets the form factors of the device.</summary>
    [IdlMember("formFactors", "https://wicg.github.io/ua-client-hints/#dom-uadatavalues-formfactors")]
    public IReadOnlyList<string>? FormFactors { get; init; }

    /// <summary>Gets or sets the brands of the user agent and their full versions.</summary>
    [IdlMember("fullVersionList", "https://wicg.github.io/ua-client-hints/#dom-uadatavalues-fullversionlist")]
    public IReadOnlyList<NavigatorUABrandVersion>? FullVersionList { get; init; }

    /// <summary>Gets or sets the model of the device.</summary>
    [IdlMember("model", "https://wicg.github.io/ua-client-hints/#dom-uadatavalues-model")]
    public string? Model { get; init; }

    /// <summary>Gets or sets whether the user agent prefers a mobile experience.</summary>
    [IdlMember("mobile", "https://wicg.github.io/ua-client-hints/#dom-uadatavalues-mobile")]
    public bool? Mobile { get; init; }

    /// <summary>Gets or sets the platform brand.</summary>
    [IdlMember("platform", "https://wicg.github.io/ua-client-hints/#dom-uadatavalues-platform")]
    public string? Platform { get; init; }

    /// <summary>Gets or sets the version of the platform.</summary>
    [IdlMember("platformVersion", "https://wicg.github.io/ua-client-hints/#dom-uadatavalues-platformversion")]
    public string? PlatformVersion { get; init; }

    /// <summary>Gets or sets the full version of the user agent (deprecated in favor of <see cref="FullVersionList"/>).</summary>
    [IdlMember("uaFullVersion", "https://wicg.github.io/ua-client-hints/#dom-uadatavalues-uafullversion")]
    public string? UAFullVersion { get; init; }

    /// <summary>Gets or sets whether the user agent runs in 32-bit mode on 64-bit Windows.</summary>
    [IdlMember("wow64", "https://wicg.github.io/ua-client-hints/#dom-uadatavalues-wow64")]
    public bool? Wow64 { get; init; }
}

/// <summary>The low-entropy values returned by <see cref="NavigatorUAData.ToJson"/> (https://wicg.github.io/ua-client-hints/#dictdef-ualowentropyjson).</summary>
[IdlInterface("UALowEntropyJSON", "https://wicg.github.io/ua-client-hints/#dictdef-ualowentropyjson")]
public sealed class UALowEntropyJson
{
    /// <summary>Gets or sets the brands of the user agent and their significant versions.</summary>
    [IdlMember("brands", "https://wicg.github.io/ua-client-hints/#dom-ualowentropyjson-brands")]
    public IReadOnlyList<NavigatorUABrandVersion>? Brands { get; init; }

    /// <summary>Gets or sets whether the user agent prefers a mobile experience.</summary>
    [IdlMember("mobile", "https://wicg.github.io/ua-client-hints/#dom-ualowentropyjson-mobile")]
    public bool? Mobile { get; init; }

    /// <summary>Gets or sets the platform brand.</summary>
    [IdlMember("platform", "https://wicg.github.io/ua-client-hints/#dom-ualowentropyjson-platform")]
    public string? Platform { get; init; }
}

/// <summary>
/// The identity of the user agent given by the User-Agent Client Hints (https://wicg.github.io/ua-client-hints/#navigatoruadata,
/// <c>navigator.userAgentData</c> in the windows and the workers of the secure contexts): the values of
/// <see cref="BrowsingContextOptions.ClientHints"/>, also sent in the <c>Sec-CH-UA-*</c> request headers.
/// </summary>
[IdlInterface("NavigatorUAData", "https://wicg.github.io/ua-client-hints/#navigatoruadata")]
public sealed class NavigatorUAData
{
    private readonly GlobalScope _global;

    internal NavigatorUAData(GlobalScope global) => _global = global;

    private ClientHintsOptions Values => _global.Options.ClientHints ?? ClientHintsOptions.Default;

    /// <summary>Gets the brands of the user agent and their significant versions (https://wicg.github.io/ua-client-hints/#dom-navigatoruadata-brands).</summary>
    [IdlMember("brands", "https://wicg.github.io/ua-client-hints/#dom-navigatoruadata-brands")]
    public IReadOnlyList<NavigatorUABrandVersion> Brands => Values.Brands;

    /// <summary>Gets whether the user agent prefers a mobile experience (https://wicg.github.io/ua-client-hints/#dom-navigatoruadata-mobile).</summary>
    [IdlMember("mobile", "https://wicg.github.io/ua-client-hints/#dom-navigatoruadata-mobile")]
    public bool Mobile => Values.Mobile;

    /// <summary>Gets the platform brand (https://wicg.github.io/ua-client-hints/#dom-navigatoruadata-platform).</summary>
    [IdlMember("platform", "https://wicg.github.io/ua-client-hints/#dom-navigatoruadata-platform")]
    public string Platform => Values.Platform;

    /// <summary>
    /// Returns the low-entropy values and the requested high-entropy values (https://wicg.github.io/ua-client-hints/#dom-navigatoruadata-gethighentropyvalues):
    /// <c>architecture</c>, <c>bitness</c>, <c>formFactors</c>, <c>fullVersionList</c>, <c>model</c>, <c>platformVersion</c>,
    /// <c>uaFullVersion</c> and <c>wow64</c>; the other names are ignored. The document of a window that is not allowed to
    /// use the <c>ch-ua-high-entropy-values</c> policy-controlled feature only gets the low-entropy values. The task
    /// completes in a task of the event loop of the global object.
    /// </summary>
    /// <param name="hints">The names of the high-entropy values.</param>
    [IdlMember("getHighEntropyValues", "https://wicg.github.io/ua-client-hints/#dom-navigatoruadata-gethighentropyvalues")]
    public Task<UADataValues> GetHighEntropyValues(IReadOnlyList<string> hints)
    {
        ArgumentNullException.ThrowIfNull(hints);
        var values = Values;
        if (_global is Window window && !PermissionsPolicyAlgorithms.IsAllowedToUse(window.Document, PolicyControlledFeature.UserAgentHighEntropyValues))
            return Task.FromResult(new UADataValues { Brands = values.Brands, Mobile = values.Mobile, Platform = values.Platform });

        var result = new UADataValues
        {
            Brands = values.Brands,
            Mobile = values.Mobile,
            Platform = values.Platform,
            Architecture = hints.Contains("architecture") ? values.Architecture : null,
            Bitness = hints.Contains("bitness") ? values.Bitness : null,
            FormFactors = hints.Contains("formFactors") ? values.FormFactors : null,
            FullVersionList = hints.Contains("fullVersionList") ? values.FullVersionList : null,
            Model = hints.Contains("model") ? values.Model : null,
            PlatformVersion = hints.Contains("platformVersion") ? values.PlatformVersion : null,
            UAFullVersion = hints.Contains("uaFullVersion") ? values.FullVersion : null,
            Wow64 = hints.Contains("wow64") ? values.Wow64 : null,
        };

        var completion = new TaskCompletionSource<UADataValues>();
        _global.QueueGlobalTask(() => completion.TrySetResult(result));
        return completion.Task;
    }

    /// <summary>Returns the low-entropy values (https://wicg.github.io/ua-client-hints/#dom-navigatoruadata-tojson).</summary>
    [IdlMember("toJSON", "https://wicg.github.io/ua-client-hints/#dom-navigatoruadata-tojson")]
    public UALowEntropyJson ToJson()
    {
        var values = Values;
        return new UALowEntropyJson { Brands = values.Brands, Mobile = values.Mobile, Platform = values.Platform };
    }
}
