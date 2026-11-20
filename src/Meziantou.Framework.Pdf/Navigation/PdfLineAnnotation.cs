using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A line annotation (ISO 32000-2, 12.5.6.7): a straight line, with endings such as arrows, and leader lines for dimensions.</summary>
public sealed class PdfLineAnnotation : PdfMarkupAnnotation
{
    internal PdfLineAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
        : base(page, dictionary, reference)
    {
    }

    /// <summary>Gets or sets the start of the line (the first point of <c>/L</c>), in default user space.</summary>
    public PdfPoint Start
    {
        get => GetPoint(0);
        set => SetLine(value, End);
    }

    /// <summary>Gets or sets the end of the line (the second point of <c>/L</c>), in default user space.</summary>
    public PdfPoint End
    {
        get => GetPoint(1);
        set => SetLine(Start, value);
    }

    /// <summary>Gets or sets the ending of the line at <see cref="Start"/> (<c>/LE</c>).</summary>
    public PdfLineEnding StartEnding
    {
        get => PdfLineEndingNames.Read(Dictionary).Start;
        set => PdfLineEndingNames.Write(Dictionary, value, EndEnding);
    }

    /// <summary>Gets or sets the ending of the line at <see cref="End"/> (<c>/LE</c>).</summary>
    public PdfLineEnding EndEnding
    {
        get => PdfLineEndingNames.Read(Dictionary).End;
        set => PdfLineEndingNames.Write(Dictionary, StartEnding, value);
    }

    /// <summary>Gets or sets the color that fills the closed endings (<c>/IC</c>), or <see langword="null"/>.</summary>
    public PdfColor? InteriorColor
    {
        get => ReadColor(AnnotationNames.IC);
        set => WriteColor(AnnotationNames.IC, value);
    }

    /// <summary>Gets or sets the length of the leader lines (<c>/LL</c>), perpendicular to the line at its ends; 0, the default, for none. A negative length draws them below the line.</summary>
    public double LeaderLineLength
    {
        get => Dictionary.GetNumber(AnnotationNames.LL, 0);
        set => Dictionary[AnnotationNames.LL] = value == 0 ? null : PdfObjectFactory.Number(value);
    }

    /// <summary>Gets or sets the length of the extensions of the leader lines beyond the line (<c>/LLE</c>).</summary>
    public double LeaderLineExtension
    {
        get => Dictionary.GetNumber(AnnotationNames.LLE, 0);
        set => Dictionary[AnnotationNames.LLE] = value == 0 ? null : PdfObjectFactory.Number(Math.Abs(value));
    }

    /// <summary>Gets or sets a value indicating whether the text of the annotation is shown as a caption of the line (<c>/Cap</c>).</summary>
    public bool ShowCaption
    {
        get => Dictionary.GetBoolean(AnnotationNames.Cap);
        set => Dictionary[AnnotationNames.Cap] = value ? PdfBoolean.True : null;
    }

    private PdfPoint GetPoint(int index)
    {
        var line = Dictionary.Get<PdfArray>(PdfNames.L);
        return line is { Count: 4 } ? new PdfPoint(line.GetNumber(index * 2), line.GetNumber((index * 2) + 1)) : default;
    }

    private void SetLine(PdfPoint start, PdfPoint end) => Dictionary[PdfNames.L] = PdfArray.FromNumbers(start.X, start.Y, end.X, end.Y);
}
