using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A form XObject of a document (ISO 32000-2, 8.10): a content stream that can be painted several times, with
/// <see cref="PdfContentBuilder.DrawForm"/>, or used as a transparency group or the group of a soft mask.
/// </summary>
public sealed class PdfForm
{
    private readonly PdfStream _stream;

    internal PdfForm(PdfDocument document, PdfRectangle boundingBox, PdfMatrix? matrix, PdfTransparencyGroup? group)
    {
        Document = document;
        BoundingBox = boundingBox;
        Group = group;
        _stream = new PdfStream();
        var dictionary = _stream.Dictionary;
        dictionary[PdfNames.Type] = PdfNames.XObject;
        dictionary[PdfNames.Subtype] = PdfNames.Form;
        dictionary[PdfNames.BBox] = boundingBox.ToArray();
        if (matrix is { } value)
        {
            dictionary[PdfNames.Matrix] = PdfArray.FromNumbers(value.A, value.B, value.C, value.D, value.E, value.F);
        }

        if (group is not null)
        {
            var groupDictionary = new PdfDictionary
            {
                [PdfNames.Type] = PdfNames.Group,
                [PdfNames.S] = PdfName.Create("Transparency"),
            };
            if (group.ColorSpace is { } colorSpace)
            {
                groupDictionary[PdfNames.CS] = colorSpace switch
                {
                    PdfDeviceColorSpace.Gray => PdfNames.DeviceGray,
                    PdfDeviceColorSpace.Rgb => PdfNames.DeviceRGB,
                    _ => PdfNames.DeviceCMYK,
                };
            }

            if (group.Isolated)
            {
                groupDictionary[PdfNames.I] = PdfBoolean.True;
            }

            if (group.Knockout)
            {
                groupDictionary[PdfNames.K] = PdfBoolean.True;
            }

            dictionary[PdfNames.Group] = groupDictionary;
        }

        Resources = new PdfDictionary();
        dictionary[PdfNames.Resources] = Resources;
        Reference = document.Objects.Add(_stream);
    }

    /// <summary>Gets the document of the form.</summary>
    public PdfDocument Document { get; }

    /// <summary>Gets the reference to the form object.</summary>
    public PdfReference Reference { get; }

    /// <summary>Gets the bounding box of the form, in its own coordinate space.</summary>
    public PdfRectangle BoundingBox { get; }

    /// <summary>Gets the attributes of the transparency group of the form, or <see langword="null"/> when it is not a group.</summary>
    public PdfTransparencyGroup? Group { get; }

    /// <summary>Gets the resource dictionary of the form.</summary>
    public PdfDictionary Resources { get; }

    /// <summary>Creates a builder that appends to the content of the form.</summary>
    /// <returns>The builder.</returns>
    public PdfContentBuilder CreateContentBuilder() => new(Document, Resources, _stream);
}
