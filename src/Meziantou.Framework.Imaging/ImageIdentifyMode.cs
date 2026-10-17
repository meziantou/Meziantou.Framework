namespace Meziantou.Framework.Imaging;

/// <summary>Controls how much of an encoded image is examined by <see cref="Image.Identify(Stream, ImageIdentifyOptions?)"/>.</summary>
public enum ImageIdentifyMode
{
    /// <summary>
    /// Reads only the header and the structures that precede the first pixel payload. Values that cannot be known
    /// from the header (for example the frame count of a GIF) are reported as <see langword="null"/>; they are never guessed.
    /// </summary>
    Header = 0,

    /// <summary>
    /// Traverses the whole file structure and its bounded metadata without allocating or decoding pixels.
    /// A full scan validates the container structure but does not validate compressed pixel payloads.
    /// </summary>
    FullScan = 1,
}
