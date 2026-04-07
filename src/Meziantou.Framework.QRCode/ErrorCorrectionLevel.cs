namespace Meziantou.Framework;

/// <summary>
/// Specifies the error correction level for a QR code.
/// </summary>
public enum ErrorCorrectionLevel
{
    /// <summary>Low error correction (~7% recovery).</summary>
    L = 0,

    /// <summary>Medium error correction (~15% recovery).</summary>
    M = 1,

    /// <summary>Quartile error correction (~25% recovery).</summary>
    Q = 2,

    /// <summary>High error correction (~30% recovery).</summary>
    H = 3,
}
