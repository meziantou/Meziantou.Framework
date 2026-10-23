using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A graphics state parameter dictionary (ISO 32000-2, 8.4.5), set with <see cref="PdfContentBuilder.SetGraphicsState"/>:
/// the parameters that have no operator of their own, such as the transparency. The parameters that are
/// <see langword="null"/> are not changed. A graphics state does not belong to a document; the instances are immutable.
/// </summary>
public sealed class PdfExtGState
{
    /// <summary>Gets or initializes the line width.</summary>
    public double? LineWidth { get; init; }

    /// <summary>Gets or initializes the line cap style.</summary>
    public PdfLineCap? LineCap { get; init; }

    /// <summary>Gets or initializes the line join style.</summary>
    public PdfLineJoin? LineJoin { get; init; }

    /// <summary>Gets or initializes the miter limit.</summary>
    public double? MiterLimit { get; init; }

    /// <summary>Gets or initializes the flatness tolerance.</summary>
    public double? Flatness { get; init; }

    /// <summary>Gets or initializes the smoothness tolerance of the shadings, from 0 to 1.</summary>
    public double? Smoothness { get; init; }

    /// <summary>Gets or initializes whether the strokes are adjusted to the pixels.</summary>
    public bool? StrokeAdjustment { get; init; }

    /// <summary>Gets or initializes the constant alpha of the stroking operations (<c>/CA</c>), from 0 (transparent) to 1 (opaque).</summary>
    public double? StrokeAlpha { get; init; }

    /// <summary>Gets or initializes the constant alpha of the other painting operations (<c>/ca</c>), from 0 to 1.</summary>
    public double? FillAlpha { get; init; }

    /// <summary>Gets or initializes the blend mode.</summary>
    public PdfBlendMode? BlendMode { get; init; }

    /// <summary>Gets or initializes the soft mask; <see cref="PdfSoftMask.None"/> removes the current one.</summary>
    public PdfSoftMask? SoftMask { get; init; }

    /// <summary>Gets or initializes whether the soft mask and the alpha are shape values rather than opacity values.</summary>
    public bool? AlphaIsShape { get; init; }

    /// <summary>Gets or initializes whether the glyphs of a text object knock each other out.</summary>
    public bool? TextKnockout { get; init; }

    /// <summary>Gets or initializes whether the stroking operations overprint (<c>/OP</c>).</summary>
    public bool? StrokeOverprint { get; init; }

    /// <summary>Gets or initializes whether the other painting operations overprint (<c>/op</c>).</summary>
    public bool? FillOverprint { get; init; }

    /// <summary>Gets or initializes the overprint mode: 0 or 1 (8.6.7).</summary>
    public int? OverprintMode { get; init; }

    /// <summary>Gets the reference to the dictionary in a document, creating it on first use.</summary>
    internal PdfReference GetReference(PdfDocument document)
    {
        if (document.SharedResources.TryGetValue(this, out var reference))
            return reference;

        var dictionary = new PdfDictionary { [PdfNames.Type] = PdfNames.ExtGState };
        SetNumber(dictionary, "LW", LineWidth, minimum: 0, maximum: double.MaxValue);
        if (LineCap is { } cap)
        {
            dictionary[PdfName.Create("LC")] = PdfInteger.Get((int)cap);
        }

        if (LineJoin is { } join)
        {
            dictionary[PdfName.Create("LJ")] = PdfInteger.Get((int)join);
        }

        SetNumber(dictionary, "ML", MiterLimit, minimum: 1, maximum: double.MaxValue);
        SetNumber(dictionary, "FL", Flatness, minimum: 0, maximum: 100);
        SetNumber(dictionary, "SM", Smoothness, minimum: 0, maximum: 1);
        SetBoolean(dictionary, "SA", StrokeAdjustment);
        SetNumber(dictionary, "CA", StrokeAlpha, minimum: 0, maximum: 1);
        SetNumber(dictionary, "ca", FillAlpha, minimum: 0, maximum: 1);
        if (BlendMode is { } blendMode)
        {
            if (!Enum.IsDefined(blendMode))
                throw new InvalidOperationException($"Unknown blend mode {blendMode}");

            dictionary[PdfName.Create("BM")] = PdfName.Create(blendMode.ToString());
        }

        if (SoftMask is { } softMask)
        {
            dictionary[PdfNames.SMask] = softMask.GetObject(document);
        }

        SetBoolean(dictionary, "AIS", AlphaIsShape);
        SetBoolean(dictionary, "TK", TextKnockout);
        SetBoolean(dictionary, "OP", StrokeOverprint);
        SetBoolean(dictionary, "op", FillOverprint);
        if (OverprintMode is { } mode)
        {
            if (mode is not (0 or 1))
                throw new InvalidOperationException("The overprint mode must be 0 or 1");

            dictionary[PdfName.Create("OPM")] = PdfInteger.Get(mode);
        }

        reference = document.Objects.Add(dictionary);
        document.SharedResources[this] = reference;
        return reference;
    }

    private static void SetNumber(PdfDictionary dictionary, string key, double? value, double minimum, double maximum)
    {
        if (value is not { } number)
            return;

        if (!double.IsFinite(number) || number < minimum || number > maximum)
            throw new InvalidOperationException($"The value of /{key} must be between {minimum} and {maximum}");

        dictionary[PdfName.Create(key)] = PdfObjectFactory.Number(number);
    }

    private static void SetBoolean(PdfDictionary dictionary, string key, bool? value)
    {
        if (value is { } flag)
        {
            dictionary[PdfName.Create(key)] = PdfBoolean.Get(flag);
        }
    }
}
