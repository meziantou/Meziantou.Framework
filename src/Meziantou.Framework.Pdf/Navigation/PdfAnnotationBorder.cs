using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// The border of an annotation (ISO 32000-2, 12.5.4): its border style dictionary (<c>/BS</c>), the older border array
/// (<c>/Border</c>) when the annotation has no style dictionary, and its border effect (<c>/BE</c>).
/// </summary>
/// <remarks>
/// The setters write the border style dictionary; the viewers prefer it to the border array. Changing the border does
/// not change an appearance stream the annotation already has: call <see cref="PdfAnnotation.UpdateAppearance"/>.
/// </remarks>
public sealed class PdfAnnotationBorder
{
    // The dashes of a dashed border: a bounded number of numbers
    private const int MaxDashes = 16;

    private readonly PdfDictionary _annotation;

    internal PdfAnnotationBorder(PdfDictionary annotation)
    {
        _annotation = annotation;
    }

    /// <summary>
    /// Gets or sets the width of the border, in points (<c>/BS /W</c>, or the third number of <c>/Border</c>); 0 for no
    /// border. The default is 1.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double Width
    {
        get
        {
            var width = Style?.GetResolved(AnnotationNames.W) is PdfNumber number
                ? number.DoubleValue
                : _annotation.Get<PdfArray>(PdfNames.Border) is { Count: >= 3 } border ? border.GetNumber(2, 1) : 1;
            return double.IsFinite(width) ? Math.Max(width, 0) : 1;
        }

        set
        {
            if (!(value >= 0 && double.IsFinite(value)))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The width must be a positive number");

            GetOrCreateStyle()[AnnotationNames.W] = PdfObjectFactory.Number(value);
        }
    }

    /// <summary>Gets or sets the style of the border (<c>/BS /S</c>). The default is <see cref="PdfAnnotationBorderStyle.Solid"/>; a border array with dashes is <see cref="PdfAnnotationBorderStyle.Dashed"/>.</summary>
    public PdfAnnotationBorderStyle BorderStyle
    {
        get
        {
            if (Style is { } style)
            {
                return style.GetName(PdfNames.S)?.Value switch
                {
                    "D" => PdfAnnotationBorderStyle.Dashed,
                    "B" => PdfAnnotationBorderStyle.Beveled,
                    "I" => PdfAnnotationBorderStyle.Inset,
                    "U" => PdfAnnotationBorderStyle.Underline,
                    _ => PdfAnnotationBorderStyle.Solid,
                };
            }

            return _annotation.Get<PdfArray>(PdfNames.Border) is { Count: >= 4 } border && border.GetResolved(3) is PdfArray ? PdfAnnotationBorderStyle.Dashed : PdfAnnotationBorderStyle.Solid;
        }

        set
        {
            var name = value switch
            {
                PdfAnnotationBorderStyle.Solid => "S",
                PdfAnnotationBorderStyle.Dashed => "D",
                PdfAnnotationBorderStyle.Beveled => "B",
                PdfAnnotationBorderStyle.Inset => "I",
                PdfAnnotationBorderStyle.Underline => "U",
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The border style is not valid"),
            };
            var style = GetOrCreateStyle();
            if (value == PdfAnnotationBorderStyle.Solid)
            {
                style.Remove(PdfNames.S);
            }
            else
            {
                style.SetName(PdfNames.S, name);
            }
        }
    }

    /// <summary>
    /// Gets or sets the lengths of the dashes and of the gaps of a dashed border (<c>/BS /D</c>, or the fourth element of
    /// <c>/Border</c>), in points. The default is a dash of 3 points and a gap of 3 points; an empty list removes the entry.
    /// </summary>
    /// <exception cref="ArgumentException">A length is negative or not finite, or all the lengths are 0.</exception>
    public IReadOnlyList<double> DashPattern
    {
        get
        {
            var array = Style is { } style ? style.Get<PdfArray>(PdfNames.D) : _annotation.Get<PdfArray>(PdfNames.Border) is { Count: >= 4 } border ? border.GetResolved(3) as PdfArray : null;
            if (array is null || array.Count == 0)
                return [3];

            var result = new List<double>();
            for (var i = 0; i < array.Count && i < MaxDashes; i++)
            {
                var value = array.GetNumber(i);
                result.Add(double.IsFinite(value) ? Math.Abs(value) : 0);
            }

            return result;
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Any(length => !(length >= 0 && double.IsFinite(length))) || (value.Count > 0 && value.All(length => length == 0)))
                throw new ArgumentException("The lengths must be positive numbers, and not all 0", nameof(value));

            GetOrCreateStyle()[PdfNames.D] = value.Count == 0 ? null : PdfArray.FromNumbers([.. value]);
        }
    }

    /// <summary>
    /// Gets or sets the intensity of the cloudy effect of the border (<c>/BE</c>, 12.5.4, table 169), from 0 to 2: the
    /// border of a square, a circle, a polygon or a free text is drawn as a series of arcs. 0, the default, is a plain
    /// border.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 2.</exception>
    public double CloudyIntensity
    {
        get
        {
            if (_annotation.Get<PdfDictionary>(AnnotationNames.BE) is not { } effect || effect.GetName(PdfNames.S)?.Value != "C")
                return 0;

            var intensity = effect.GetNumber(AnnotationNames.I, 0);
            return double.IsFinite(intensity) ? Math.Clamp(intensity, 0, 2) : 0;
        }

        set
        {
            if (!(value is >= 0 and <= 2))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The intensity must be between 0 and 2");

            _annotation[AnnotationNames.BE] = value == 0 ? null : new PdfDictionary
            {
                [PdfNames.S] = PdfName.Create("C"),
                [AnnotationNames.I] = PdfObjectFactory.Number(value),
            };
        }
    }

    private PdfDictionary? Style => _annotation.Get<PdfDictionary>(AnnotationNames.BS);

    private PdfDictionary GetOrCreateStyle()
    {
        if (Style is { } style)
            return style;

        // The border style dictionary replaces the border array (12.5.4): its width and dashes are kept
        style = new PdfDictionary();
        if (_annotation.Get<PdfArray>(PdfNames.Border) is { Count: >= 3 } border)
        {
            style[AnnotationNames.W] = PdfObjectFactory.Number(Math.Max(border.GetNumber(2, 1), 0));
            if (border.Count >= 4 && border.GetResolved(3) is PdfArray { Count: > 0 } dashes)
            {
                style[PdfNames.S] = PdfName.Create("D");
                style[PdfNames.D] = dashes;
            }
        }

        _annotation[AnnotationNames.BS] = style;
        return style;
    }
}
