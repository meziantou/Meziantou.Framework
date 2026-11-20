namespace Meziantou.Framework.Pdf;

/// <summary>The type of the values of a field of the schema of a portfolio (<c>/Subtype</c>, ISO 32000-2, 12.3.5, table 155).</summary>
public enum PdfPortfolioFieldType
{
    /// <summary>A text, set per file (<c>S</c>).</summary>
    Text,

    /// <summary>A date, set per file (<c>D</c>).</summary>
    Date,

    /// <summary>A number, set per file (<c>N</c>).</summary>
    Number,

    /// <summary>The name of the file (<c>F</c>).</summary>
    FileName,

    /// <summary>The description of the file (<c>Desc</c>).</summary>
    Description,

    /// <summary>The date the file was last modified (<c>ModDate</c>).</summary>
    ModificationDate,

    /// <summary>The date the file was created (<c>CreationDate</c>).</summary>
    CreationDate,

    /// <summary>The size of the file (<c>Size</c>).</summary>
    Size,

    /// <summary>The size of the compressed file (<c>CompressedSize</c>).</summary>
    CompressedSize,
}
