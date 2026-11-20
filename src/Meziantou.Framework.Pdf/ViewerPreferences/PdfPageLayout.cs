namespace Meziantou.Framework.Pdf;

/// <summary>How viewers lay out the pages when the document is opened (<c>/PageLayout</c>, ISO 32000-2, 7.7.2, table 29).</summary>
public enum PdfPageLayout
{
    /// <summary>One page at a time (<c>SinglePage</c>), the default.</summary>
    SinglePage,

    /// <summary>The pages in one column (<c>OneColumn</c>).</summary>
    OneColumn,

    /// <summary>The pages in two columns, the odd pages on the left (<c>TwoColumnLeft</c>).</summary>
    TwoColumnLeft,

    /// <summary>The pages in two columns, the odd pages on the right (<c>TwoColumnRight</c>).</summary>
    TwoColumnRight,

    /// <summary>Two pages at a time, the odd pages on the left (<c>TwoPageLeft</c>).</summary>
    TwoPageLeft,

    /// <summary>Two pages at a time, the odd pages on the right (<c>TwoPageRight</c>).</summary>
    TwoPageRight,
}
