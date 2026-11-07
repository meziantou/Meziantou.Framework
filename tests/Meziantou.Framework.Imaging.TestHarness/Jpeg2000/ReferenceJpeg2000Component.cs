namespace Meziantou.Framework.Imaging.TestHarness.Jpeg2000;

/// <summary>A component of a JPEG 2000 codestream (SIZ marker segment).</summary>
/// <param name="Precision">The number of bits of a sample.</param>
/// <param name="IsSigned">Whether the samples are signed.</param>
/// <param name="XSubsampling">The horizontal separation of the samples on the reference grid.</param>
/// <param name="YSubsampling">The vertical separation of the samples on the reference grid.</param>
public sealed record ReferenceJpeg2000Component(int Precision, bool IsSigned, int XSubsampling, int YSubsampling);
