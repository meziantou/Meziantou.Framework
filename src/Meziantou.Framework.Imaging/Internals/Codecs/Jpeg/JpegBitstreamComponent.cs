namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A component of a JPEG frame.</summary>
/// <param name="Id">The identifier of the component.</param>
/// <param name="QuantizationTable">The index of its table in <see cref="JpegBitstreamModel.QuantizationTables"/>.</param>
internal sealed record JpegBitstreamComponent(int Id, int QuantizationTable)
{
    /// <summary>Gets or sets the horizontal sampling factor.</summary>
    public int HorizontalSampling { get; set; } = 1;

    /// <summary>Gets or sets the vertical sampling factor.</summary>
    public int VerticalSampling { get; set; } = 1;
}
