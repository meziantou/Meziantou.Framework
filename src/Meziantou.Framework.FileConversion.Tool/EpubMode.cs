namespace Meziantou.Framework.FileConversionTool;

internal enum EpubMode
{
    /// <summary>The images of the book when every page is an image (comics, fixed layout), the rendered pages otherwise.</summary>
    Auto,

    /// <summary>The pages laid out from the XHTML documents and their style sheets.</summary>
    Render,

    /// <summary>The images of the pages: every document of the reading order must show one image.</summary>
    Images,
}
