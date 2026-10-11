namespace Meziantou.Framework.Imaging.TestHarness.JpegXl;

/// <summary>An extra channel of a JPEG XL image.</summary>
/// <param name="Type">The type: 0 alpha, 1 depth, 2 spot color, 3 selection mask, 4 black, 5 CFA, 6 thermal, 15 non-optional, 16 optional.</param>
/// <param name="BitsPerSample">The depth of the samples.</param>
/// <param name="DimShift">The base-2 logarithm of the factor that the channel is downsampled by.</param>
/// <param name="IsAlphaAssociated">Whether the color channels are premultiplied by this alpha channel.</param>
public sealed record ReferenceJpegXlExtraChannel(int Type, int BitsPerSample, int DimShift, bool IsAlphaAssociated);
