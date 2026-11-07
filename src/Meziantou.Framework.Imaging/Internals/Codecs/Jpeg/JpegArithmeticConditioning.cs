namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What an arithmetic-coded scan needs for one component (ITU-T T.81 sections F.1.4.4.1 and F.1.4.4.2): the statistics
/// areas of its DC and AC conditioning tables and their conditioning values, captured when the scan starts.
/// </summary>
/// <remarks>
/// Components that select the same table share its statistics area. An area is all zeros at the start of a scan and after
/// each restart marker.
/// </remarks>
internal sealed class JpegArithmeticConditioning
{
    /// <summary>The number of statistics bins of a DC table: four for each of the five classes of the previous difference, 15 magnitude categories and 14 magnitude bits (T.81 table F.4).</summary>
    public const int DcBinCount = 49;

    /// <summary>The number of statistics bins of an AC table: three for each of the 63 coefficients, then twice 14 magnitude categories and 14 magnitude bits (T.81 table F.5).</summary>
    public const int AcBinCount = 245;

    /// <param name="dcStatistics">The statistics area of the DC table, or an empty array when the scan codes no DC difference.</param>
    /// <param name="acStatistics">The statistics area of the AC table, or an empty array when the scan codes no AC coefficient.</param>
    /// <param name="dcLower">L: the lower bound of the DC conditioning, 0 to 15.</param>
    /// <param name="dcUpper">U: the upper bound of the DC conditioning, L to 15.</param>
    /// <param name="acThreshold">Kx: the last coefficient that uses the first set of magnitude bins, 1 to 63.</param>
    public JpegArithmeticConditioning(byte[] dcStatistics, byte[] acStatistics, int dcLower, int dcUpper, int acThreshold)
    {
        DcStatistics = dcStatistics;
        AcStatistics = acStatistics;
        DcLower = dcLower;
        DcUpper = dcUpper;
        AcThreshold = acThreshold;
    }

    public byte[] DcStatistics { get; }

    public byte[] AcStatistics { get; }

    public int DcLower { get; }

    public int DcUpper { get; }

    public int AcThreshold { get; }

    /// <summary>Gets or sets the first bin of the class of the previous DC difference of the component: 0, 4, 8, 12 or 16.</summary>
    public int DcContext { get; set; }
}
