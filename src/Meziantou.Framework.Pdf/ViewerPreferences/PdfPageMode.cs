namespace Meziantou.Framework.Pdf;

/// <summary>What viewers show next to the pages when the document is opened (<c>/PageMode</c>, ISO 32000-2, 7.7.2, table 29).</summary>
public enum PdfPageMode
{
    /// <summary>Nothing (<c>UseNone</c>), the default.</summary>
    UseNone,

    /// <summary>The outline, the bookmarks (<c>UseOutlines</c>).</summary>
    UseOutlines,

    /// <summary>The thumbnails of the pages (<c>UseThumbs</c>).</summary>
    UseThumbs,

    /// <summary>The pages in full-screen mode, without menus nor windows (<c>FullScreen</c>).</summary>
    FullScreen,

    /// <summary>The optional content groups, the layers (<c>UseOC</c>).</summary>
    UseOptionalContent,

    /// <summary>The embedded files (<c>UseAttachments</c>).</summary>
    UseAttachments,
}
