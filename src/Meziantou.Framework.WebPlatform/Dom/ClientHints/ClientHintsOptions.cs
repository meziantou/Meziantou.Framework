using System.Reflection;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The values of the client hints of the browsing contexts (see <see cref="BrowsingContextOptions.ClientHints"/>): the
/// identity of the user agent given by the User-Agent Client Hints (https://wicg.github.io/ua-client-hints/, the
/// <c>Sec-CH-UA-*</c> request headers and <c>navigator.userAgentData</c>), and the device and network hints of the
/// Client Hints infrastructure (https://wicg.github.io/client-hints-infrastructure/). The hints of the viewport and of the
/// preferences of the user (<c>Sec-CH-DPR</c>, <c>Sec-CH-Viewport-Width</c>, <c>Sec-CH-Prefers-Color-Scheme</c>...) are the
/// values of <see cref="BrowsingContextOptions.Viewport"/>.
/// </summary>
/// <remarks>
/// The default values describe the library, not the machine it runs on: an unknown platform, no architecture, no device
/// memory and no network information. A hint whose value is null is not sent.
/// </remarks>
public sealed record ClientHintsOptions
{
    private static readonly Version LibraryVersion = typeof(ClientHintsOptions).Assembly.GetName().Version ?? new Version(1, 0, 0);

    /// <summary>Gets the default values: the brand of the library, a desktop device, an unknown platform, and no device nor network information.</summary>
    public static ClientHintsOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets the brands of the user agent and their significant versions (https://wicg.github.io/ua-client-hints/#brands):
    /// <c>navigator.userAgentData.brands</c> and the <c>Sec-CH-UA</c> header. A brand should be shorter than 32 characters. The
    /// default is the brand of the library with its major version; no arbitrary ("GREASE") brand is added, and the order is
    /// kept: add one and shuffle the list to follow the recommendation of the specification.
    /// </summary>
    public IReadOnlyList<NavigatorUABrandVersion> Brands { get; init; } = [new() { Brand = "Meziantou.Framework.WebPlatform", Version = LibraryVersion.Major.ToString(System.Globalization.CultureInfo.InvariantCulture) }];

    /// <summary>
    /// Gets or sets the brands of the user agent and their full versions (the <c>fullVersionList</c> of
    /// <c>getHighEntropyValues()</c> and the <c>Sec-CH-UA-Full-Version-List</c> header). The default is the brand of the
    /// library with its version.
    /// </summary>
    public IReadOnlyList<NavigatorUABrandVersion> FullVersionList { get; init; } = [new() { Brand = "Meziantou.Framework.WebPlatform", Version = GetLibraryFullVersion() }];

    /// <summary>
    /// Gets or sets the full version of the user agent (the deprecated <c>uaFullVersion</c> of <c>getHighEntropyValues()</c>
    /// and the <c>Sec-CH-UA-Full-Version</c> header). The default is the version of the library.
    /// </summary>
    public string FullVersion { get; init; } = GetLibraryFullVersion();

    /// <summary>Gets or sets whether the user agent prefers a mobile experience (<c>navigator.userAgentData.mobile</c> and the <c>Sec-CH-UA-Mobile</c> header). The default is false.</summary>
    public bool Mobile { get; init; }

    /// <summary>
    /// Gets or sets the platform brand (<c>navigator.userAgentData.platform</c> and the <c>Sec-CH-UA-Platform</c> header),
    /// e.g. <c>Windows</c>, <c>macOS</c>, <c>Linux</c> or <c>Android</c>. The default is <c>Unknown</c>.
    /// </summary>
    public string Platform { get; init; } = "Unknown";

    /// <summary>
    /// Gets or sets the version of the platform (the <c>platformVersion</c> of <c>getHighEntropyValues()</c> and the
    /// <c>Sec-CH-UA-Platform-Version</c> header), e.g. <c>15.0.0</c>; the empty string on Linux
    /// (https://wicg.github.io/ua-client-hints/#get-the-platform-version). The default is the empty string.
    /// </summary>
    public string PlatformVersion { get; init; } = "";

    /// <summary>
    /// Gets or sets the architecture of the platform (the <c>architecture</c> of <c>getHighEntropyValues()</c> and the
    /// <c>Sec-CH-UA-Arch</c> header): <c>x86</c>, <c>arm</c> or the empty string (the default).
    /// </summary>
    public string Architecture { get; init; } = "";

    /// <summary>
    /// Gets or sets the bitness of the architecture of the platform (the <c>bitness</c> of <c>getHighEntropyValues()</c> and
    /// the <c>Sec-CH-UA-Bitness</c> header): <c>32</c>, <c>64</c> or the empty string (the default).
    /// </summary>
    public string Bitness { get; init; } = "";

    /// <summary>
    /// Gets or sets the model of the device (the <c>model</c> of <c>getHighEntropyValues()</c> and the <c>Sec-CH-UA-Model</c>
    /// header). The default is the empty string, as on the platforms that do not expose it.
    /// </summary>
    public string Model { get; init; } = "";

    /// <summary>
    /// Gets or sets whether the user agent runs in 32-bit mode on 64-bit Windows (the <c>wow64</c> of
    /// <c>getHighEntropyValues()</c> and the <c>Sec-CH-UA-WoW64</c> header). The default is false.
    /// </summary>
    public bool Wow64 { get; init; }

    /// <summary>
    /// Gets or sets the form factors of the device (the <c>formFactors</c> of <c>getHighEntropyValues()</c> and the
    /// <c>Sec-CH-UA-Form-Factors</c> header), e.g. <c>Desktop</c>, <c>Mobile</c> or <c>Tablet</c>. The header lists them in
    /// lexical order. The default is <c>Desktop</c>.
    /// </summary>
    public IReadOnlyList<string> FormFactors { get; init; } = ["Desktop"];

    /// <summary>
    /// Gets or sets the memory of the device, in GiB (the <c>Sec-CH-Device-Memory</c> header,
    /// https://www.w3.org/TR/device-memory/#sec-device-memory-client-hint-header): the value is rounded down to a power of
    /// two between 0.25 and 8. The default is null: the hint is not sent.
    /// </summary>
    public double? DeviceMemory { get; init; }

    /// <summary>
    /// Gets or sets whether the user prefers a reduced data usage (https://wicg.github.io/savedata/#save-data-request-header-field):
    /// the <c>Save-Data: on</c> header is then sent with the requests to the secure origins, without opt-in. The default is false.
    /// </summary>
    public bool SaveData { get; init; }

    /// <summary>
    /// Gets or sets the effective round-trip time of the connection (the <c>RTT</c> hint, https://wicg.github.io/netinfo/#rtt-request-header-field).
    /// As in Chromium, the value is rounded to a multiple of 50 milliseconds, and is 3 seconds at most. The default is null:
    /// the hint is not sent.
    /// </summary>
    public TimeSpan? RoundTripTime { get; init; }

    /// <summary>
    /// Gets or sets the effective bandwidth of the connection, in megabits per second (the <c>Downlink</c> hint,
    /// https://wicg.github.io/netinfo/#downlink-request-header-field). As in Chromium, the value is rounded to a multiple of
    /// 50 kilobits per second, and is 10 megabits per second at most. The default is null: the hint is not sent.
    /// </summary>
    public double? Downlink { get; init; }

    /// <summary>
    /// Gets or sets the effective connection type (the <c>ECT</c> hint, https://wicg.github.io/netinfo/#ect-request-header-field):
    /// <c>slow-2g</c>, <c>2g</c>, <c>3g</c> or <c>4g</c>. The default is null: the hint is not sent.
    /// </summary>
    public string? EffectiveConnectionType { get; init; }

    private static string GetLibraryFullVersion()
    {
        var version = typeof(ClientHintsOptions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (version is not null)
        {
            // The build metadata (e.g. the commit of "1.2.3+abcdef") is not part of the version
            var metadata = version.IndexOf('+', StringComparison.Ordinal);
            version = metadata < 0 ? version : version[..metadata];
        }

        return version is { Length: > 0 } ? version : LibraryVersion.ToString(3);
    }
}
