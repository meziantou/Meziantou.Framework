namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What <see cref="HeifFileWriter"/> needs to describe a coded stream (the color images of a file, or its alpha planes)
/// without knowing its codec: the sample format of the <c>pixi</c> property and the configuration box of the codec. A
/// binding reads the rest (the profile that selects its brands) from its own description.
/// </summary>
internal abstract class HeifStreamDescription
{
    /// <summary>Gets the number of planes: 1 for a monochrome stream, otherwise 3.</summary>
    public abstract int PlaneCount { get; }

    /// <summary>Gets the number of bits of a sample.</summary>
    public abstract int BitDepth { get; }

    /// <summary>
    /// Gets the size of the pictures that a decoder outputs for the stream when it is not the size of the pictures that
    /// the encoder was given, or <see langword="null"/>: a codec that cannot code every size (HEVC cannot crop an odd
    /// number of columns or rows of a picture with subsampled chroma) codes a larger picture, and the container then
    /// describes the part of it that is the image (a clean aperture).
    /// </summary>
    public virtual Size? OutputSize => null;

    /// <summary>
    /// Records a coded picture of the stream (an image, a tile of a grid or a sample of a track) before its configuration
    /// box is written: the configuration of a codec can depend on the size of the coded pictures (the level of an HEVC
    /// stream). A configuration box describes the pictures recorded when it is written.
    /// </summary>
    /// <param name="length">The number of bytes of the coded data of the picture, as the container stores them.</param>
    public virtual void RecordCodedPicture(int length)
    {
    }

    /// <summary>
    /// Writes the box that holds the configuration record of the codec (<see cref="HeifCodecBinding.ConfigurationBoxType"/>):
    /// a property of an image item, and a box of the sample entry of a track.
    /// </summary>
    public abstract void WriteConfigurationBox(HeifBoxWriter writer);
}
