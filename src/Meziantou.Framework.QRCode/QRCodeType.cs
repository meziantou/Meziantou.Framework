namespace Meziantou.Framework;

/// <summary>
/// Specifies the type of QR code.
/// </summary>
public enum QRCodeType
{
    /// <summary>Standard QR code (versions 1-40).</summary>
    Standard,

    /// <summary>Micro QR code (versions M1-M4).</summary>
    MicroQR,

    /// <summary>Rectangular Micro QR code (rMQR).</summary>
    RMQR,
}
