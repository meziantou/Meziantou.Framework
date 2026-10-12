namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A quantization table of a JPEG file.</summary>
/// <param name="Precision">0 for values of 8 bits, 1 for values of 16 bits.</param>
/// <param name="Slot">The destination of the table (0 to 3).</param>
/// <param name="IsLast">Whether the table ends its segment.</param>
internal sealed record JpegBitstreamQuantizationTable(int Precision, int Slot, bool IsLast)
{
    /// <summary>Gets the 64 values, in the order of the rows of the block (not in zig-zag order).</summary>
    public ushort[] Values { get; } = new ushort[64];

    /// <summary>Gets or sets a value indicating whether the values are known.</summary>
    public bool HasValues { get; set; }
}
