namespace Meziantou.Framework;

/// <summary>
/// Specifies the authentication type for a WiFi network QR code.
/// </summary>
public enum WifiAuthentication
{
    /// <summary>No authentication (open network).</summary>
    None,

    /// <summary>WEP authentication.</summary>
    WEP,

    /// <summary>WPA/WPA2 authentication.</summary>
    WPA,
}
