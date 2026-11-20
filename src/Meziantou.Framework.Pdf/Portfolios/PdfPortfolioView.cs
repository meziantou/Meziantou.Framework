namespace Meziantou.Framework.Pdf;

/// <summary>How viewers show the files of a portfolio (<c>/View</c>, ISO 32000-2, 12.3.5, table 153).</summary>
public enum PdfPortfolioView
{
    /// <summary>A list of the files, with the fields of the schema in columns (<c>D</c>), the default.</summary>
    Details,

    /// <summary>The files as tiles, with a small icon and a few fields (<c>T</c>).</summary>
    Tile,

    /// <summary>The list is hidden: the initial file is shown (<c>H</c>).</summary>
    Hidden,

    /// <summary>A navigator of the portfolio (<c>C</c>).</summary>
    Custom,
}
