namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>Mutable per-frame settings of a displayed frame.</summary>
public sealed class FrameMetadata
{
    private Point? _hotspot;

    /// <summary>Gets or sets how long the frame is displayed. Defaults to <see cref="FrameDuration.Zero"/>.</summary>
    /// <remarks>
    /// The exact source timing is preserved on import. Encoders convert it to their own representation according to
    /// their <see cref="Formats.FrameDurationRounding"/> setting.
    /// </remarks>
    public FrameDuration Duration { get; set; }

    /// <summary>
    /// Gets or sets the cursor hotspot of the frame: the pixel that designates the pointer position, in stored-pixel
    /// coordinates from the top-left corner. Defaults to <see langword="null"/> (the frame is not a cursor image).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hotspot is metadata, never a pixel offset. It is read from cursor (<c>.cur</c>) and animated cursor
    /// (<c>.ani</c>) files and written back to them (the top-left corner when the frame has none). Every other output
    /// cannot store it: a frame that has one is rejected unless the encoder's
    /// <see cref="Formats.ImageEncoder.MetadataHandling"/> allows discarding it.
    /// </para>
    /// <para>
    /// The hotspot of an attached frame is always inside that frame. Geometry operations (crop, resize, rotations, mirrors,
    /// auto-orient) move it with the pixel it designates, and fail instead of dropping it when that pixel is removed.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is negative, or the point is outside the frame these settings belong to.</exception>
    public Point? Hotspot
    {
        get => _hotspot;
        set
        {
            if (value is { } point)
            {
                if (point.X < 0 || point.Y < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), point, "The hotspot coordinates must not be negative.");

                if (Owner is { IsValid: true } frame && (point.X >= frame.Storage.Width || point.Y >= frame.Storage.Height))
                    throw new ArgumentOutOfRangeException(nameof(value), point, string.Create(CultureInfo.InvariantCulture, $"The hotspot must be inside the {frame.Storage.Width}x{frame.Storage.Height} frame."));
            }

            _hotspot = value;
        }
    }

    /// <summary>Gets or sets the frame these settings are attached to, whose size bounds <see cref="Hotspot"/>; <see langword="null"/> for a detached copy.</summary>
    internal ImageFrame? Owner { get; set; }

    /// <summary>Creates an independent copy of these settings.</summary>
    /// <returns>A new instance with the same values.</returns>
    public FrameMetadata Clone() => new() { Duration = Duration, _hotspot = _hotspot };

    /// <summary>
    /// Sets the hotspot without validating it against the frame: the caller guarantees that it is inside the storage the frame
    /// has, or is about to have (decoders, committed geometry operations).
    /// </summary>
    internal void SetHotspotUnchecked(Point? hotspot) => _hotspot = hotspot;
}
