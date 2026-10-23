namespace Meziantou.Framework.Pdf.Objects;

/// <summary>A number: a <see cref="PdfInteger"/> or a <see cref="PdfReal"/> (ISO 32000-2, 7.3.3).</summary>
/// <remarks>The class cannot be derived outside the library.</remarks>
public abstract class PdfNumber : PdfObject
{
    private protected PdfNumber()
    {
    }

    /// <summary>Gets the value as a double-precision number.</summary>
    public abstract double DoubleValue { get; }
}
