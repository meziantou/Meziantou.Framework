using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// The appearance characteristics of a widget annotation (<c>/MK</c>, ISO 32000-2, 12.5.6.19, table 192): what the
/// library and the viewers use to draw the appearances of its field. The dictionary is created when a value is set.
/// </summary>
/// <remarks>Changing a value does not change the appearances the widget already has: set the value of its field again, or call <see cref="PdfAnnotation.UpdateAppearance"/>.</remarks>
public sealed class PdfAppearanceCharacteristics
{
    private static readonly PdfName CAName = PdfName.Create("CA");
    private static readonly PdfName RCName = PdfName.Create("RC");
    private static readonly PdfName ACName = PdfName.Create("AC");

    private readonly PdfDictionary _annotation;

    internal PdfAppearanceCharacteristics(PdfDictionary annotation)
    {
        _annotation = annotation;
    }

    /// <summary>Gets or sets the rotation of the content of the widget, counterclockwise (<c>/R</c>): 0, 90, 180 or 270 degrees.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a multiple of 90.</exception>
    public int Rotation
    {
        get
        {
            var rotation = (int)(Dictionary?.GetInteger(AnnotationNames.R, 0) ?? 0);
            return ((rotation % 360) + 360) % 360 / 90 * 90;
        }

        set
        {
            if (value % 90 != 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The rotation must be a multiple of 90");

            var rotation = ((value % 360) + 360) % 360;
            Set(AnnotationNames.R, rotation == 0 ? null : PdfInteger.Get(rotation));
        }
    }

    /// <summary>Gets or sets the color of the border of the widget (<c>/BC</c>), or <see langword="null"/> for no border.</summary>
    public PdfColor? BorderColor
    {
        get => ReadColor(AnnotationNames.BC);
        set => Set(AnnotationNames.BC, value is { } color ? PdfArray.FromNumbers(color.GetComponents()) : null);
    }

    /// <summary>Gets or sets the color of the background of the widget (<c>/BG</c>), or <see langword="null"/> for a transparent background.</summary>
    public PdfColor? BackgroundColor
    {
        get => ReadColor(AnnotationNames.BG);
        set => Set(AnnotationNames.BG, value is { } color ? PdfArray.FromNumbers(color.GetComponents()) : null);
    }

    /// <summary>Gets or sets the caption of a push button (<c>/CA</c>), or the character of the symbol of a check box or a radio button in ZapfDingbats.</summary>
    public string? NormalCaption
    {
        get => Dictionary?.GetText(CAName);
        set => Set(CAName, value is null ? null : PdfString.FromText(value));
    }

    /// <summary>Gets or sets the caption of a push button when the mouse is over it (<c>/RC</c>).</summary>
    public string? RolloverCaption
    {
        get => Dictionary?.GetText(RCName);
        set => Set(RCName, value is null ? null : PdfString.FromText(value));
    }

    /// <summary>Gets or sets the caption of a push button while it is pressed (<c>/AC</c>).</summary>
    public string? DownCaption
    {
        get => Dictionary?.GetText(ACName);
        set => Set(ACName, value is null ? null : PdfString.FromText(value));
    }

    private PdfDictionary? Dictionary => _annotation.Get<PdfDictionary>(AnnotationNames.MK);

    private PdfColor? ReadColor(PdfName key)
    {
        var components = Dictionary?.Get<PdfArray>(key);
        double Get(int index) => Math.Clamp(components.GetNumber(index), 0, 1);
        return components?.Count switch
        {
            1 => PdfColor.FromGray(Get(0)),
            3 => PdfColor.FromRgb(Get(0), Get(1), Get(2)),
            4 => PdfColor.FromCmyk(Get(0), Get(1), Get(2), Get(3)),
            _ => null,
        };
    }

    private void Set(PdfName key, PdfObject? value)
    {
        var dictionary = Dictionary;
        if (dictionary is null)
        {
            if (value is null)
                return;

            dictionary = new PdfDictionary();
            _annotation[AnnotationNames.MK] = dictionary;
        }

        dictionary[key] = value;
    }
}
