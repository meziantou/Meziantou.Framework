namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental accounting of the per-input limits of <see cref="ImageResourceLimits"/>: frames
/// processed (displayed frames and the poster), cumulative displayed pixels, encoded bytes consumed, and decompressed or
/// retained metadata bytes. Codecs charge <em>before</em> allocating or consuming, so a limit is reported with
/// <see cref="ImageResourceLimitException"/> and never turned into a truncated result. One tracker serves one input (an
/// eager load, an identify call or a reader).
/// </summary>
[SuppressMessage("Design", "MA0182:Unused internal type", Justification = "Shared codec infrastructure; consumed by the codecs and covered by unit tests.")]
internal sealed class InputResourceTracker
{
    public InputResourceTracker(ImageResourceLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        Limits = limits;
    }

    public ImageResourceLimits Limits { get; }

    /// <summary>Gets the number of frames (displayed frames and poster) charged so far.</summary>
    public int Frames { get; private set; }

    /// <summary>Gets the cumulative number of displayed pixels charged so far.</summary>
    public long TotalPixels { get; private set; }

    /// <summary>Gets the number of encoded bytes charged so far.</summary>
    public long EncodedBytes { get; private set; }

    /// <summary>Gets the number of metadata bytes charged so far.</summary>
    public long MetadataBytes { get; private set; }

    /// <summary>Charges one frame of the canvas size before it is produced.</summary>
    /// <param name="canvas">The canvas size (positive, already validated against the canvas limits).</param>
    /// <param name="isPoster"><see langword="true"/> for a separate poster: it counts toward <see cref="ImageResourceLimits.MaxFrames"/> but is not a displayed frame.</param>
    /// <exception cref="ImageResourceLimitException">A limit is exceeded; the counters are unchanged.</exception>
    public void ChargeFrame(Size canvas, bool isPoster = false)
    {
        var frames = Frames + 1L;
        if (frames > Limits.MaxFrames)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Frames, Limits.MaxFrames, frames);

        var totalPixels = TotalPixels;
        if (!isPoster)
        {
            totalPixels = Charge(totalPixels, canvas.Area, Limits.MaxTotalPixels, ImageResourceLimitKind.TotalPixels);
        }

        Frames = (int)frames;
        TotalPixels = totalPixels;
    }

    /// <summary>
    /// Charges one frame traversed by a full-scan identification: it counts toward
    /// <see cref="ImageResourceLimits.MaxFrames"/>, but no pixel is produced, so <see cref="TotalPixels"/> is unchanged.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The frame limit is exceeded; the counters are unchanged.</exception>
    public void ChargeScannedFrame()
    {
        var frames = Frames + 1L;
        if (frames > Limits.MaxFrames)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Frames, Limits.MaxFrames, frames);

        Frames = (int)frames;
    }

    /// <summary>Charges encoded bytes before they are consumed.</summary>
    /// <param name="count">The number of bytes (non-negative).</param>
    /// <exception cref="ImageResourceLimitException">The limit is exceeded; the counter is unchanged.</exception>
    public void ChargeEncodedBytes(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        EncodedBytes = Charge(EncodedBytes, count, Limits.MaxEncodedBytes, ImageResourceLimitKind.EncodedBytes);
    }

    /// <summary>Charges decompressed or retained metadata bytes before they are produced.</summary>
    /// <param name="count">The number of bytes (non-negative).</param>
    /// <exception cref="ImageResourceLimitException">The limit is exceeded; the counter is unchanged.</exception>
    public void ChargeMetadataBytes(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        MetadataBytes = Charge(MetadataBytes, count, Limits.MaxMetadataBytes, ImageResourceLimitKind.MetadataBytes);
    }

    private static long Charge(long current, long count, long limit, ImageResourceLimitKind kind)
    {
        // Saturating addition: the requested value is reported even when it would overflow
        var requested = count > long.MaxValue - current ? long.MaxValue : current + count;
        if (requested > limit)
            throw new ImageResourceLimitException(kind, limit, requested);

        return requested;
    }
}
