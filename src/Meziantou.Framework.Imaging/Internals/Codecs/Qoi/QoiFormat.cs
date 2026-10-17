using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constants of the QOI specification (version 1.0, qoiformat.org) shared by the decoder and the encoder: header layout,
/// chunk tags, the running index hash and the end marker.
/// </summary>
/// <remarks>
/// Pixels are handled as packed <see cref="uint"/> values with the bytes R, G, B, A in memory order (little-endian
/// <c>R | G &lt;&lt; 8 | B &lt;&lt; 16 | A &lt;&lt; 24</c>), so that an <see cref="PixelFormat.Rgba32"/> row can be read and
/// written as 32-bit values.
/// </remarks>
internal static class QoiFormat
{
    /// <summary>The length of the header: magic, big-endian width and height, channels and colorspace.</summary>
    public const int HeaderLength = 14;

    /// <summary>The length of the end marker: seven <c>0x00</c> bytes followed by <c>0x01</c>.</summary>
    public const int EndMarkerLength = 8;

    /// <summary>The longest chunk (<c>QOI_OP_RGBA</c>: tag and four samples).</summary>
    public const int MaxChunkLength = 5;

    /// <summary>The number of entries of the running index.</summary>
    public const int IndexLength = 64;

    /// <summary>The longest run of one <c>QOI_OP_RUN</c> chunk (62: the run lengths 63 and 64 would collide with the RGB and RGBA tags).</summary>
    public const int MaxRun = 62;

    /// <summary><c>QOI_OP_RGB</c>: tag, then R, G, B; alpha is unchanged.</summary>
    public const byte OpRgb = 0xFE;

    /// <summary><c>QOI_OP_RGBA</c>: tag, then R, G, B, A.</summary>
    public const byte OpRgba = 0xFF;

    /// <summary>The 2-bit tag of <c>QOI_OP_INDEX</c> (<c>0b00</c>).</summary>
    public const byte OpIndex = 0x00;

    /// <summary>The 2-bit tag of <c>QOI_OP_DIFF</c> (<c>0b01</c>).</summary>
    public const byte OpDiff = 0x40;

    /// <summary>The 2-bit tag of <c>QOI_OP_LUMA</c> (<c>0b10</c>).</summary>
    public const byte OpLuma = 0x80;

    /// <summary>The 2-bit tag of <c>QOI_OP_RUN</c> (<c>0b11</c>).</summary>
    public const byte OpRun = 0xC0;

    /// <summary>The mask of the 2-bit tags.</summary>
    public const byte TagMask = 0xC0;

    /// <summary>The colorspace field of sRGB samples with linear alpha.</summary>
    public const byte ColorSpaceSrgb = 0;

    /// <summary>The colorspace field of linear samples (all channels).</summary>
    public const byte ColorSpaceLinear = 1;

    /// <summary>The pixel every stream starts from: opaque black.</summary>
    public const uint InitialPixel = 0xFF000000;

    public static ReadOnlySpan<byte> Magic => "qoif"u8;

    public static ReadOnlySpan<byte> EndMarker => [0, 0, 0, 0, 0, 0, 0, 1];

    /// <summary>The running index position of a pixel: <c>(r * 3 + g * 5 + b * 7 + a * 11) % 64</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Hash(uint pixel)
        => (int)(((pixel & 0xFF) * 3 + ((pixel >> 8) & 0xFF) * 5 + ((pixel >> 16) & 0xFF) * 7 + (pixel >> 24) * 11) & (IndexLength - 1));

    /// <summary>Gets the number of bytes of the chunk starting with <paramref name="tag"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetChunkLength(byte tag) => tag switch
    {
        OpRgba => 5,
        OpRgb => 4,
        _ => (tag & TagMask) == OpLuma ? 2 : 1,
    };
}
