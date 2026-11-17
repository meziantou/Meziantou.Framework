namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The precision of the RGBA rows a texture decodes to (the source layout of the frame sink).</summary>
internal enum Ktx2OutputPrecision
{
    /// <summary><see cref="PixelFormat.Rgba32"/> rows.</summary>
    Byte,

    /// <summary><see cref="PixelFormat.Rgba64"/> rows.</summary>
    UInt16,

    /// <summary><see cref="PixelFormat.RgbaHalf"/> rows: half-precision values, exact for 16-bit, 11-bit, 10-bit and shared-exponent floats.</summary>
    Half,

    /// <summary><see cref="PixelFormat.RgbaFloat"/> rows.</summary>
    Float,
}
