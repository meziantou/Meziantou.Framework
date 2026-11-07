namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The layout of the 8-bit color pixels that the JPEG encoder converts to YCbCr (<see cref="JpegEncoderCodec.ConvertRgbToYCbCr{TLayout}"/>).</summary>
internal interface IJpegSourceLayout
{
    /// <summary>Gets the number of bytes of a pixel: 3, or 4 with an alpha sample last, which is ignored.</summary>
    static abstract int PixelSize { get; }

    /// <summary>Gets a value indicating whether blue is the first sample and red the third.</summary>
    static abstract bool IsBgr { get; }
}
