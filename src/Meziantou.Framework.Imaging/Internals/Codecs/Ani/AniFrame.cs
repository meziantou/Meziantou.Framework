namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// One stored frame of an animated cursor: the range of its <c>icon</c> chunk and, once a step references it, the icon or
/// cursor file it holds and the representation that is displayed.
/// </summary>
/// <param name="index">The zero-based index of the frame in the frame list.</param>
/// <param name="offset">The offset of the embedded icon or cursor file from the start of the input.</param>
/// <param name="length">The length of the embedded file.</param>
internal sealed class AniFrame(int index, long offset, long length)
{
    /// <summary>Gets the zero-based index of the frame in the frame list.</summary>
    public int Index => index;

    /// <summary>Gets the offset of the embedded icon or cursor file from the start of the input.</summary>
    public long Offset => offset;

    /// <summary>Gets the length of the embedded file.</summary>
    public long Length => length;

    /// <summary>Gets or sets the view of the embedded file, whose offsets start at zero; <see langword="null"/> until a step references the frame.</summary>
    public RandomAccessSource? Source { get; set; }

    /// <summary>Gets or sets the type of the embedded file: <see cref="ImageFormat.Ico"/> or <see cref="ImageFormat.Cur"/>.</summary>
    public ImageFormat ContainerFormat { get; set; }

    /// <summary>Gets or sets the displayed representation; <see langword="null"/> until a step references the frame.</summary>
    public IcoRepresentation? Selected { get; set; }

    /// <summary>
    /// Gets or sets the index of the first displayed frame decoded from this stored frame, or -1 while it was not decoded:
    /// a later step showing it copies that frame instead of decoding the payload again.
    /// </summary>
    public int FirstOutputIndex { get; set; } = -1;
}
