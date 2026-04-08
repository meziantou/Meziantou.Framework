namespace Meziantou.Framework;

/// <summary>
/// Specifies the hash algorithm for OTP Auth QR codes.
/// </summary>
public enum OneTimePasswordAlgorithm
{
    /// <summary>SHA-1 (default).</summary>
    SHA1,

    /// <summary>SHA-256.</summary>
    SHA256,

    /// <summary>SHA-512.</summary>
    SHA512,
}
