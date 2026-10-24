namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>The integer contexts of the arithmetic coding of a text region (6.4.4, the IAx procedures).</summary>
internal sealed class Jbig2TextRegionContexts(Jbig2ArithmeticDecoder decoder, int symbolCodeLength)
{
    public Jbig2ArithmeticDecoder Decoder { get; } = decoder;

    public byte[] DeltaT { get; } = new byte[512];

    public byte[] FirstS { get; } = new byte[512];

    public byte[] DeltaS { get; } = new byte[512];

    public byte[] T { get; } = new byte[512];

    public byte[] Id { get; } = new byte[1 << (symbolCodeLength + 1)];

    public byte[] Refinement { get; } = new byte[512];

    public byte[] RefinementWidth { get; } = new byte[512];

    public byte[] RefinementHeight { get; } = new byte[512];

    public byte[] RefinementX { get; } = new byte[512];

    public byte[] RefinementY { get; } = new byte[512];
}
