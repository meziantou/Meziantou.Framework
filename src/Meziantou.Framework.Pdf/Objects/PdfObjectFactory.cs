namespace Meziantou.Framework.Pdf.Objects;

internal static class PdfObjectFactory
{
    /// <summary>Creates an integer for a whole number, a real number otherwise.</summary>
    public static PdfNumber Number(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "A PDF number must be finite");

        if (Math.Abs(value) < 1e15 && Math.Floor(value) == value)
            return PdfInteger.Get((long)value);

        return PdfReal.Get(value);
    }
}
