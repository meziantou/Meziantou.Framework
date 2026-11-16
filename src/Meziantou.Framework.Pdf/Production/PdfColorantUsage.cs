namespace Meziantou.Framework.Pdf;

/// <summary>The colorants and the color spaces a page uses (<see cref="PdfSeparations.GetColorants(PdfPage)"/>).</summary>
public sealed class PdfColorantUsage
{
    internal PdfColorantUsage(IReadOnlyList<string> processColorants, IReadOnlyList<string> spotColorants, IReadOnlyList<string> colorSpaces, bool usesOverprint)
    {
        ProcessColorants = processColorants;
        SpotColorants = spotColorants;
        ColorSpaces = colorSpaces;
        UsesOverprint = usesOverprint;
    }

    /// <summary>Gets the process colorants the page paints, in the order <c>Cyan</c>, <c>Magenta</c>, <c>Yellow</c>, <c>Black</c>.</summary>
    public IReadOnlyList<string> ProcessColorants { get; }

    /// <summary>Gets the spot colorants of the separation and DeviceN color spaces of the page, sorted by name.</summary>
    public IReadOnlyList<string> SpotColorants { get; }

    /// <summary>Gets the families of the color spaces the page uses, such as <c>DeviceCMYK</c> or <c>Separation</c>, sorted by name.</summary>
    public IReadOnlyList<string> ColorSpaces { get; }

    /// <summary>Gets a value indicating whether a graphics state of the page turns overprinting on (<c>OP</c> or <c>op</c>).</summary>
    public bool UsesOverprint { get; }

    /// <summary>Gets the plates of the page: its process colorants, then its spot colorants.</summary>
    public IReadOnlyList<string> Colorants => [.. ProcessColorants, .. SpotColorants];
}
