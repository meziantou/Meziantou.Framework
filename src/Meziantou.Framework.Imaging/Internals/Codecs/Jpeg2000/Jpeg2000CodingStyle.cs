namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The coding style of one component: the SPcod parameters of a COD marker segment or the SPcoc parameters of a COC marker
/// segment (T.800, Tables A.15 and A.23).
/// </summary>
internal sealed class Jpeg2000CodingStyle
{
    /// <summary>Selective arithmetic coding bypass.</summary>
    public const int StyleBypass = 0x01;

    /// <summary>The context probabilities are reset at the start of each coding pass.</summary>
    public const int StyleReset = 0x02;

    /// <summary>The coder is terminated at the end of each coding pass.</summary>
    public const int StyleTerminateAll = 0x04;

    /// <summary>The contexts are vertically causal.</summary>
    public const int StyleVerticallyCausal = 0x08;

    /// <summary>The termination is predictable.</summary>
    public const int StylePredictableTermination = 0x10;

    /// <summary>Segmentation symbols end each cleanup pass.</summary>
    public const int StyleSegmentationSymbols = 0x20;

    /// <summary>The code-blocks use the high-throughput block coder (T.814, Table A.3).</summary>
    public const int StyleHighThroughput = 0x40;

    /// <summary>With <see cref="StyleHighThroughput"/>: some code-blocks of the tile-component use the block coder of T.800 (T.814, Table A.3).</summary>
    public const int StyleMixed = 0x80;

    private readonly byte[]? _precinctSizes;

    public Jpeg2000CodingStyle(int decompositionLevels, int codeBlockWidthExponent, int codeBlockHeightExponent, int codeBlockStyle, bool isReversible, byte[]? precinctSizes)
    {
        DecompositionLevels = decompositionLevels;
        CodeBlockWidthExponent = codeBlockWidthExponent;
        CodeBlockHeightExponent = codeBlockHeightExponent;
        CodeBlockStyle = codeBlockStyle;
        IsReversible = isReversible;
        _precinctSizes = precinctSizes;
    }

    /// <summary>Gets the number of decomposition levels (NL), 0 to 32.</summary>
    public int DecompositionLevels { get; }

    /// <summary>Gets the exponent of the code-block width (xcb), 2 to 10.</summary>
    public int CodeBlockWidthExponent { get; }

    /// <summary>Gets the exponent of the code-block height (ycb), 2 to 10.</summary>
    public int CodeBlockHeightExponent { get; }

    /// <summary>Gets the style of the code-block coding passes: a combination of the <c>Style</c> constants.</summary>
    public int CodeBlockStyle { get; }

    /// <summary>Gets a value indicating whether the wavelet transformation is the reversible 5-3 filter; otherwise it is the irreversible 9-7 filter.</summary>
    public bool IsReversible { get; }

    /// <summary>Gets a value indicating whether the marker segment defines the precinct sizes.</summary>
    public bool HasPrecincts => _precinctSizes is not null;

    /// <summary>Gets the exponent of the precinct width of a resolution level (PPx); 15 when the precincts are not defined.</summary>
    public int GetPrecinctWidthExponent(int resolution) => _precinctSizes is null ? 15 : _precinctSizes[resolution] & 0x0F;

    /// <summary>Gets the exponent of the precinct height of a resolution level (PPy); 15 when the precincts are not defined.</summary>
    public int GetPrecinctHeightExponent(int resolution) => _precinctSizes is null ? 15 : _precinctSizes[resolution] >> 4;
}
