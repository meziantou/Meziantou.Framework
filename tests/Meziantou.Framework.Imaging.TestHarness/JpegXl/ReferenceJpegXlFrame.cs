namespace Meziantou.Framework.Imaging.TestHarness.JpegXl;

/// <summary>A frame of a JPEG XL codestream.</summary>
/// <param name="Type">The type: 0 regular, 1 LF, 2 reference only, 3 skip progressive.</param>
/// <param name="IsModular">Whether the frame is coded with Modular (otherwise VarDCT).</param>
/// <param name="Flags">The flags: 1 noise, 2 patches, 16 splines, 32 uses LF frames, 128 no adaptive LF smoothing.</param>
/// <param name="IsYCbCr">Whether the samples are YCbCr.</param>
/// <param name="Upsampling">The factor the frame is upsampled by.</param>
/// <param name="PassCount">The number of passes.</param>
/// <param name="X">The position of the frame in the image.</param>
/// <param name="Y">The position of the frame in the image.</param>
/// <param name="Width">The width of the frame.</param>
/// <param name="Height">The height of the frame.</param>
/// <param name="BlendMode">The blend mode of the color channels: 0 replace, 1 add, 2 blend, 3 alpha-weighted add, 4 multiply.</param>
/// <param name="Duration">The duration in ticks.</param>
/// <param name="IsLast">Whether the frame ends the image.</param>
/// <param name="SaveAsReference">The reference slot the frame is saved to.</param>
/// <param name="SectionCount">The number of sections.</param>
/// <param name="ByteCount">The size of the sections, or -1 when the table of contents is permuted (it is then entropy coded and not read).</param>
public sealed record ReferenceJpegXlFrame(int Type, bool IsModular, ulong Flags, bool IsYCbCr, int Upsampling, int PassCount, int X, int Y, int Width, int Height, int BlendMode, long Duration, bool IsLast, int SaveAsReference, int SectionCount, long ByteCount)
{
    /// <summary>Gets a value indicating whether the decoder applies the Gabor-like filter to the frame; a frame header with every default value applies it.</summary>
    public bool IsGaborishEnabled { get; init; } = true;

    /// <summary>Gets the number of iterations of the edge-preserving filter (0 to 3); a frame header with every default value has 2.</summary>
    public int EdgePreservingIterations { get; init; } = 2;
}
