namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>The parameters of the text region decoding procedure (table 9).</summary>
internal sealed class Jbig2TextRegionParameters
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int InstanceCount { get; init; }

    public required int StripSize { get; init; }

    public int LogStripSize => System.Numerics.BitOperations.Log2((uint)StripSize);

    public required IReadOnlyList<Jbig2Bitmap> Symbols { get; init; }

    public required int SymbolCodeLength { get; init; }

    public required int DefaultPixel { get; init; }

    public required int CombinationOperator { get; init; }

    public required bool Transposed { get; init; }

    public required Jbig2ReferenceCorner ReferenceCorner { get; init; }

    public required int DsOffset { get; init; }

    public required bool Refine { get; init; }

    public required int RefinementTemplate { get; init; }

    public required (int X, int Y)[] RefinementAdaptivePixels { get; init; }

    public required byte[] RefinementContexts { get; init; }

    public Jbig2TextRegionContexts? Arithmetic { get; init; }

    public Jbig2TextRegionHuffman? Huffman { get; init; }
}
