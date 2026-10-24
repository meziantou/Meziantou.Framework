using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A soft mask (ISO 32000-2, 11.6.5.2): the alpha of the objects painted while it is set comes from a transparency group,
/// its luminosity or its alpha. It is set with <see cref="PdfExtGState.SoftMask"/>.
/// </summary>
public sealed class PdfSoftMask
{
    private readonly PdfForm? _group;
    private readonly bool _luminosity;
    private readonly PdfColor? _backdrop;
    private readonly PdfFunction? _transfer;

    private PdfSoftMask(PdfForm? group, bool luminosity, PdfColor? backdrop, PdfFunction? transfer)
    {
        _group = group;
        _luminosity = luminosity;
        _backdrop = backdrop;
        _transfer = transfer;
    }

    /// <summary>Gets the absence of soft mask: setting it removes the current soft mask.</summary>
    public static PdfSoftMask None { get; } = new(group: null, luminosity: false, backdrop: null, transfer: null);

    /// <summary>Creates a soft mask from the luminosity of a transparency group: white is opaque, black transparent.</summary>
    /// <param name="group">The group: a form with a <see cref="PdfTransparencyGroup"/> whose color space is set.</param>
    /// <param name="backdrop">The color the group is composited on, in the color space of the group; <see langword="null"/> for black.</param>
    /// <param name="transfer">A function from the luminosity to the alpha (one input, one output); <see langword="null"/> for the identity.</param>
    /// <returns>The soft mask.</returns>
    public static PdfSoftMask FromLuminosity(PdfForm group, PdfColor? backdrop = null, PdfFunction? transfer = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Group?.ColorSpace is not { } colorSpace)
            throw new ArgumentException("The group of a luminosity soft mask must have a color space", nameof(group));

        if (backdrop is { } color && color.ColorSpace != colorSpace)
            throw new ArgumentException("The backdrop must be in the color space of the group", nameof(backdrop));

        CheckTransfer(transfer);
        return new PdfSoftMask(group, luminosity: true, backdrop, transfer);
    }

    /// <summary>Creates a soft mask from the alpha of a transparency group.</summary>
    /// <param name="group">The group: a form with a <see cref="PdfTransparencyGroup"/>.</param>
    /// <param name="transfer">A function from the alpha of the group to the alpha of the mask (one input, one output); <see langword="null"/> for the identity.</param>
    /// <returns>The soft mask.</returns>
    public static PdfSoftMask FromAlpha(PdfForm group, PdfFunction? transfer = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Group is null)
            throw new ArgumentException("The form of a soft mask must be a transparency group", nameof(group));

        CheckTransfer(transfer);
        return new PdfSoftMask(group, luminosity: false, backdrop: null, transfer);
    }

    internal PdfObject GetObject(PdfDocument document)
    {
        if (_group is null)
            return PdfName.Create("None");

        if (_group.Document != document)
            throw new InvalidOperationException("The group of the soft mask belongs to another document");

        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Mask,
            [PdfNames.S] = PdfName.Create(_luminosity ? "Luminosity" : "Alpha"),
            [PdfNames.G] = _group.Reference,
        };
        if (_backdrop is { } backdrop)
        {
            dictionary[PdfName.Create("BC")] = PdfArray.FromNumbers(backdrop.GetComponents());
        }

        if (_transfer is not null)
        {
            dictionary[PdfName.Create("TR")] = _transfer.GetObject(document);
        }

        return dictionary;
    }

    private static void CheckTransfer(PdfFunction? transfer)
    {
        if (transfer is not null && (transfer.InputCount != 1 || transfer.OutputCount != 1))
            throw new ArgumentException("The transfer function must have one input and one output", nameof(transfer));
    }
}
