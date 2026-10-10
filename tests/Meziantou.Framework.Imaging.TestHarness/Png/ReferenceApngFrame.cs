namespace Meziantou.Framework.Imaging.TestHarness.Png;

/// <summary>An APNG frame read by <see cref="ReferencePng"/>: its raw <c>fcTL</c> fields and its image data.</summary>
/// <param name="SequenceNumber">The <c>fcTL</c> sequence number.</param>
/// <param name="Width">The region width.</param>
/// <param name="Height">The region height.</param>
/// <param name="XOffset">The region x offset.</param>
/// <param name="YOffset">The region y offset.</param>
/// <param name="DelayNumerator">The raw <c>delay_num</c> field.</param>
/// <param name="DelayDenominator">The raw <c>delay_den</c> field (0 means 100).</param>
/// <param name="DisposeOp">The raw <c>dispose_op</c> field (0 NONE, 1 BACKGROUND, 2 PREVIOUS).</param>
/// <param name="BlendOp">The raw <c>blend_op</c> field (0 SOURCE, 1 OVER).</param>
/// <param name="UsesIdat">Whether the frame data is the <c>IDAT</c> image (the <c>fcTL</c> precedes <c>IDAT</c>).</param>
/// <param name="DataSequenceNumbers">The sequence numbers of the frame's <c>fdAT</c> chunks (empty for an <c>IDAT</c> frame).</param>
/// <param name="Datastream">The concatenated zlib datastream of the frame.</param>
public sealed record ReferenceApngFrame(
    uint SequenceNumber,
    int Width,
    int Height,
    int XOffset,
    int YOffset,
    ushort DelayNumerator,
    ushort DelayDenominator,
    byte DisposeOp,
    byte BlendOp,
    bool UsesIdat,
    IReadOnlyList<uint> DataSequenceNumbers,
    ReadOnlyMemory<byte> Datastream)
{
    /// <summary>Gets a value indicating whether the frame replaces the whole canvas (offset 0, canvas size, <c>SOURCE</c>).</summary>
    /// <param name="canvasWidth">The canvas width.</param>
    /// <param name="canvasHeight">The canvas height.</param>
    /// <returns><see langword="true"/> when the displayed frame is the frame data.</returns>
    public bool IsFullCanvasSource(int canvasWidth, int canvasHeight) => XOffset == 0 && YOffset == 0 && Width == canvasWidth && Height == canvasHeight && BlendOp == 0;
}
