namespace Meziantou.Framework.FileConversionTool;

internal enum PdfImageEncoding
{
    /// <summary>The JPEG images are embedded as they are, the other images are compressed without loss.</summary>
    Auto,

    /// <summary>The images are compressed as JPEG (with <c>--jpeg-quality</c>).</summary>
    Jpeg,

    /// <summary>The images are compressed without loss.</summary>
    Lossless,
}
