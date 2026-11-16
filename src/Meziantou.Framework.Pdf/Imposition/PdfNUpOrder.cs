namespace Meziantou.Framework.Pdf;

/// <summary>The order of the pages on a sheet of <see cref="PdfImposition.CreateNUp(PdfDocument, PdfNUpOptions)"/>.</summary>
public enum PdfNUpOrder
{
    /// <summary>Left to right, then top to bottom, as text in a western language is read.</summary>
    Horizontal,

    /// <summary>Top to bottom, then left to right, as the columns of a newspaper are read.</summary>
    Vertical,

    /// <summary>Right to left, then top to bottom, for the languages written from right to left.</summary>
    HorizontalRightToLeft,
}
