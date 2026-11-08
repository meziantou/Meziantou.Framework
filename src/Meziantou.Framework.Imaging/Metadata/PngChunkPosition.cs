namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>Where a <see cref="PngChunk"/> lies relative to the critical chunks of a PNG file.</summary>
public enum PngChunkPosition
{
    /// <summary>
    /// Before the <c>PLTE</c> chunk, or before the image data when the file has no palette (the decoder reads such chunks
    /// of a file without palette as <see cref="BeforeImageData"/>).
    /// </summary>
    BeforePalette = 0,

    /// <summary>After the <c>PLTE</c> chunk, if any, and before the first <c>IDAT</c> chunk.</summary>
    BeforeImageData = 1,

    /// <summary>After the image data, before the <c>IEND</c> chunk.</summary>
    AfterImageData = 2,
}
