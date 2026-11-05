namespace Meziantou.Framework.WebPlatform.Dom;

public sealed partial class BrowsingContextOptions
{
    /// <summary>
    /// Gets or sets the host hooks of the Network Information API (https://wicg.github.io/netinfo/, <c>navigator.connection</c>):
    /// <see cref="NetworkInformationOptions.Provider"/> is the source of the information about the network connection. The
    /// default is null: an unknown connection with the performance of a 4G network (or the network values of
    /// <see cref="ClientHints"/>), which never changes.
    /// </summary>
    public NetworkInformationOptions? NetworkInformation { get; init; }

    /// <summary>
    /// Gets or sets the preference of the user expressed by Global Privacy Control (https://w3c.github.io/gpc/):
    /// <c>navigator.globalPrivacyControl</c>, and the <c>Sec-GPC: 1</c> header of the requests when it is enabled. The
    /// default is null: the preference is not set (<c>navigator.globalPrivacyControl</c> is false, and no header is sent).
    /// </summary>
    public GlobalPrivacyControlOptions? GlobalPrivacyControl { get; init; }
}
