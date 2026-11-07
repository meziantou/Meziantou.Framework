namespace Meziantou.Framework.WebPlatform.Dom;

public sealed partial class BrowsingContextOptions
{
    /// <summary>
    /// Gets or sets the values of the client hints of the browsing contexts (https://wicg.github.io/client-hints-infrastructure/,
    /// https://wicg.github.io/ua-client-hints/): the identity of the user agent exposed by <c>navigator.userAgentData</c> and
    /// sent in the <c>Sec-CH-UA-*</c> request headers, the memory of the device and the network information. The default is
    /// null: <see cref="ClientHintsOptions.Default"/>, with <see cref="ClientHintsOptions.Mobile"/> set when the viewport is the
    /// one of a mobile device (<see cref="ViewportOptions.IsMobile"/>).
    /// </summary>
    /// <remarks>
    /// The low-entropy hints (<c>Sec-CH-UA</c>, <c>Sec-CH-UA-Mobile</c>, <c>Sec-CH-UA-Platform</c>, and <c>Save-Data</c> when
    /// <see cref="ClientHintsOptions.SaveData"/> is set) are sent with every request to a secure origin. The other hints are
    /// only sent to the origins that opted into them with the <c>Accept-CH</c> header (see <see cref="AcceptCHCache"/>), and to
    /// the origins they delegate them to. Set <see cref="WebPlatformFeatures.ClientHintsEnabled"/> to false to send no client hint.
    /// </remarks>
    public ClientHintsOptions? ClientHints { get; init; }

    private static readonly ClientHintsOptions MobileClientHints = ClientHintsOptions.Default with { Mobile = true };

    /// <summary>Returns the values of the client hints: <see cref="ClientHints"/>, or the default values of the device of the viewport.</summary>
    internal ClientHintsOptions GetClientHints() => ClientHints ?? (Viewport is { IsMobile: true } ? MobileClientHints : ClientHintsOptions.Default);

    /// <summary>
    /// Gets or sets the Accept-CH cache of the browsing contexts (https://wicg.github.io/client-hints-infrastructure/#accept-ch-cache):
    /// the client hints that the origins opted into with the <c>Accept-CH</c> header of the responses of the top-level
    /// navigations. The default is null: each browsing context created by the application has its own cache, shared by its
    /// auxiliary browsing contexts. A cache can be shared by several browsing contexts, and outlive them.
    /// </summary>
    public AcceptCHCache? AcceptCHCache { get; init; }
}
