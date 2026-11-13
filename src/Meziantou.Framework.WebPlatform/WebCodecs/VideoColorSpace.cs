namespace Meziantou.Framework.WebPlatform.WebCodecs;

/// <summary>The color primaries of a video color space (https://w3c.github.io/webcodecs/#enumdef-videocolorprimaries).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#enumdef-videocolorprimaries"/>
[IdlInterface("VideoColorPrimaries", "https://w3c.github.io/webcodecs/#enumdef-videocolorprimaries")]
public enum VideoColorPrimaries
{
    /// <summary>BT.709 (the primaries of sRGB).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videocolorprimaries-bt709"/>
    [IdlMember("bt709", "https://w3c.github.io/webcodecs/#dom-videocolorprimaries-bt709")]
    Bt709,

    /// <summary>BT.470 System B, G (PAL).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videocolorprimaries-bt470bg"/>
    [IdlMember("bt470bg", "https://w3c.github.io/webcodecs/#dom-videocolorprimaries-bt470bg")]
    Bt470bg,

    /// <summary>SMPTE 170M (NTSC).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videocolorprimaries-smpte170m"/>
    [IdlMember("smpte170m", "https://w3c.github.io/webcodecs/#dom-videocolorprimaries-smpte170m")]
    Smpte170m,

    /// <summary>BT.2020.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videocolorprimaries-bt2020"/>
    [IdlMember("bt2020", "https://w3c.github.io/webcodecs/#dom-videocolorprimaries-bt2020")]
    Bt2020,

    /// <summary>SMPTE EG 432-1 (the primaries of Display P3).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videocolorprimaries-smpte432"/>
    [IdlMember("smpte432", "https://w3c.github.io/webcodecs/#dom-videocolorprimaries-smpte432")]
    Smpte432,
}

/// <summary>The transfer characteristics of a video color space (https://w3c.github.io/webcodecs/#enumdef-videotransfercharacteristics).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#enumdef-videotransfercharacteristics"/>
[IdlInterface("VideoTransferCharacteristics", "https://w3c.github.io/webcodecs/#enumdef-videotransfercharacteristics")]
public enum VideoTransferCharacteristics
{
    /// <summary>BT.709.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videotransfercharacteristics-bt709"/>
    [IdlMember("bt709", "https://w3c.github.io/webcodecs/#dom-videotransfercharacteristics-bt709")]
    Bt709,

    /// <summary>SMPTE 170M.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videotransfercharacteristics-smpte170m"/>
    [IdlMember("smpte170m", "https://w3c.github.io/webcodecs/#dom-videotransfercharacteristics-smpte170m")]
    Smpte170m,

    /// <summary>IEC 61966-2-1 (the transfer function of sRGB).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videotransfercharacteristics-iec61966-2-1"/>
    [IdlMember("iec61966-2-1", "https://w3c.github.io/webcodecs/#dom-videotransfercharacteristics-iec61966-2-1")]
    Iec6196621,

    /// <summary>Linear RGB.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videotransfercharacteristics-linear"/>
    [IdlMember("linear", "https://w3c.github.io/webcodecs/#dom-videotransfercharacteristics-linear")]
    Linear,

    /// <summary>The perceptual quantizer of BT.2100 (SMPTE ST 2084).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videotransfercharacteristics-pq"/>
    [IdlMember("pq", "https://w3c.github.io/webcodecs/#dom-videotransfercharacteristics-pq")]
    Pq,

    /// <summary>The hybrid log-gamma of BT.2100.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videotransfercharacteristics-hlg"/>
    [IdlMember("hlg", "https://w3c.github.io/webcodecs/#dom-videotransfercharacteristics-hlg")]
    Hlg,
}

/// <summary>The matrix coefficients of a video color space (https://w3c.github.io/webcodecs/#enumdef-videomatrixcoefficients).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#enumdef-videomatrixcoefficients"/>
[IdlInterface("VideoMatrixCoefficients", "https://w3c.github.io/webcodecs/#enumdef-videomatrixcoefficients")]
public enum VideoMatrixCoefficients
{
    /// <summary>The samples are RGB (no conversion from YUV).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videomatrixcoefficients-rgb"/>
    [IdlMember("rgb", "https://w3c.github.io/webcodecs/#dom-videomatrixcoefficients-rgb")]
    Rgb,

    /// <summary>BT.709.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videomatrixcoefficients-bt709"/>
    [IdlMember("bt709", "https://w3c.github.io/webcodecs/#dom-videomatrixcoefficients-bt709")]
    Bt709,

    /// <summary>BT.470 System B, G.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videomatrixcoefficients-bt470bg"/>
    [IdlMember("bt470bg", "https://w3c.github.io/webcodecs/#dom-videomatrixcoefficients-bt470bg")]
    Bt470bg,

    /// <summary>SMPTE 170M.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videomatrixcoefficients-smpte170m"/>
    [IdlMember("smpte170m", "https://w3c.github.io/webcodecs/#dom-videomatrixcoefficients-smpte170m")]
    Smpte170m,

    /// <summary>BT.2020 non-constant luminance.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videomatrixcoefficients-bt2020-ncl"/>
    [IdlMember("bt2020-ncl", "https://w3c.github.io/webcodecs/#dom-videomatrixcoefficients-bt2020-ncl")]
    Bt2020Ncl,
}

/// <summary>The members of a <see cref="VideoColorSpace"/> (https://w3c.github.io/webcodecs/#dictdef-videocolorspaceinit).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-videocolorspaceinit"/>
[IdlInterface("VideoColorSpaceInit", "https://w3c.github.io/webcodecs/#dictdef-videocolorspaceinit")]
public sealed class VideoColorSpaceInit
{
    /// <summary>Gets the color primaries, or null when they are unknown.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videocolorspaceinit-primaries"/>
    [IdlMember("primaries", "https://w3c.github.io/webcodecs/#dom-videocolorspaceinit-primaries")]
    public VideoColorPrimaries? Primaries { get; init; }

    /// <summary>Gets the transfer characteristics, or null when they are unknown.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videocolorspaceinit-transfer"/>
    [IdlMember("transfer", "https://w3c.github.io/webcodecs/#dom-videocolorspaceinit-transfer")]
    public VideoTransferCharacteristics? Transfer { get; init; }

    /// <summary>Gets the matrix coefficients, or null when they are unknown.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videocolorspaceinit-matrix"/>
    [IdlMember("matrix", "https://w3c.github.io/webcodecs/#dom-videocolorspaceinit-matrix")]
    public VideoMatrixCoefficients? Matrix { get; init; }

    /// <summary>Gets whether the samples use the full range of their values, or null when it is unknown.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videocolorspaceinit-fullrange"/>
    [IdlMember("fullRange", "https://w3c.github.io/webcodecs/#dom-videocolorspaceinit-fullrange")]
    public bool? FullRange { get; init; }
}

/// <summary>
/// The color space of a <see cref="VideoFrame"/> (https://w3c.github.io/webcodecs/#videocolorspace): its color primaries,
/// transfer characteristics, matrix coefficients and range, as the codecs describe them.
/// </summary>
/// <seealso href="https://w3c.github.io/webcodecs/#videocolorspace"/>
[IdlInterface("VideoColorSpace", "https://w3c.github.io/webcodecs/#videocolorspace")]
public sealed class VideoColorSpace
{
    /// <summary>Creates a color space (https://w3c.github.io/webcodecs/#dom-videocolorspace-videocolorspace).</summary>
    /// <param name="init">The members of the color space (none: unknown).</param>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videocolorspace-videocolorspace"/>
    [IdlMember("constructor", "https://w3c.github.io/webcodecs/#dom-videocolorspace-videocolorspace")]
    public VideoColorSpace(VideoColorSpaceInit? init = null)
    {
        Primaries = init?.Primaries;
        Transfer = init?.Transfer;
        Matrix = init?.Matrix;
        FullRange = init?.FullRange;
    }

    /// <summary>The sRGB color space (https://w3c.github.io/webcodecs/#srgb-color-space): the color space of the RGB frames.</summary>
    internal static VideoColorSpace CreateSrgb() => new(new VideoColorSpaceInit { Primaries = VideoColorPrimaries.Bt709, Transfer = VideoTransferCharacteristics.Iec6196621, Matrix = VideoMatrixCoefficients.Rgb, FullRange = true });

    /// <summary>The Display P3 color space (https://w3c.github.io/webcodecs/#display-p3-color-space).</summary>
    internal static VideoColorSpace CreateDisplayP3() => new(new VideoColorSpaceInit { Primaries = VideoColorPrimaries.Smpte432, Transfer = VideoTransferCharacteristics.Iec6196621, Matrix = VideoMatrixCoefficients.Rgb, FullRange = true });

    /// <summary>Gets the color primaries, or null when they are unknown.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videocolorspace-primaries"/>
    [IdlMember("primaries", "https://w3c.github.io/webcodecs/#dom-videocolorspace-primaries")]
    public VideoColorPrimaries? Primaries { get; }

    /// <summary>Gets the transfer characteristics, or null when they are unknown.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videocolorspace-transfer"/>
    [IdlMember("transfer", "https://w3c.github.io/webcodecs/#dom-videocolorspace-transfer")]
    public VideoTransferCharacteristics? Transfer { get; }

    /// <summary>Gets the matrix coefficients, or null when they are unknown.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videocolorspace-matrix"/>
    [IdlMember("matrix", "https://w3c.github.io/webcodecs/#dom-videocolorspace-matrix")]
    public VideoMatrixCoefficients? Matrix { get; }

    /// <summary>Gets whether the samples use the full range of their values, or null when it is unknown.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videocolorspace-fullrange"/>
    [IdlMember("fullRange", "https://w3c.github.io/webcodecs/#dom-videocolorspace-fullrange")]
    public bool? FullRange { get; }

    /// <summary>Returns the members of the color space (the default <c>toJSON</c> steps).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videocolorspace-tojson"/>
    [IdlMember("toJSON", "https://w3c.github.io/webcodecs/#dom-videocolorspace-tojson")]
    public VideoColorSpaceInit ToJson() => new() { Primaries = Primaries, Transfer = Transfer, Matrix = Matrix, FullRange = FullRange };
}
