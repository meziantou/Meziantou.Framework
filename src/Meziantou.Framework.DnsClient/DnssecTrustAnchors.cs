namespace Meziantou.Framework.DnsClient;

/// <summary>Provides built-in DNSSEC trust anchors.</summary>
public static class DnssecTrustAnchors
{
    /// <summary>Gets the IANA root DNSSEC trust anchors.</summary>
    public static IReadOnlyList<DnssecTrustAnchor> Root { get; } =
    [
        new(".", 20326, 8, 2, Convert.FromHexString("E06D44B80B8F1D39A95C0B0D7C65D08458E880409BBC683457104237C7F8EC8D")),
        new(".", 38696, 8, 2, Convert.FromHexString("683D2D0ACB8C9B712A1948B27F741219298D0A450D612C483AF444A4C0FB2B16")),
    ];
}
