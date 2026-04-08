namespace Meziantou.Framework;

/// <summary>
/// Specifies the type of one-time password for OTP Auth QR codes.
/// </summary>
public enum OneTimePasswordType
{
    /// <summary>Time-based one-time password (RFC 6238).</summary>
    Totp,

    /// <summary>HMAC-based one-time password (RFC 4226).</summary>
    Hotp,
}
