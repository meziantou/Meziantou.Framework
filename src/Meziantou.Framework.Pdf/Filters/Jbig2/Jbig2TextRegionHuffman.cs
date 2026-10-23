namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>The tables and the data of the Huffman coding of a text region (6.4.4).</summary>
internal sealed class Jbig2TextRegionHuffman
{
    public required Jbig2BitReader Reader { get; init; }

    public required byte[] Data { get; init; }

    public required int End { get; init; }

    public required Jbig2HuffmanTable FirstS { get; init; }

    public required Jbig2HuffmanTable DeltaS { get; init; }

    public required Jbig2HuffmanTable DeltaT { get; init; }

    public required Jbig2HuffmanTable RefinementWidth { get; init; }

    public required Jbig2HuffmanTable RefinementHeight { get; init; }

    public required Jbig2HuffmanTable RefinementX { get; init; }

    public required Jbig2HuffmanTable RefinementY { get; init; }

    public required Jbig2HuffmanTable RefinementSize { get; init; }

    public required Jbig2HuffmanTable SymbolIds { get; init; }
}
