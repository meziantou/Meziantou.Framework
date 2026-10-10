namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The reference frames of the AV1 frame being decoded, indexed by reference frame type (<c>LAST_FRAME</c> to
/// <c>ALTREF_FRAME</c>): the frame buffers that <c>ref_frame_idx</c> selects among the 8 slots, and their size relative to
/// the frame being decoded (AV1 specification, section 7.11.3.3 "Motion vector scaling process"). The frame buffers belong to
/// the slots of the decoder.
/// </summary>
internal sealed class Av1ReferenceFrames
{
    private const int NoScale = 1 << Av1Constants.RefScaleShift;

    private readonly Av1FrameBuffer?[] _frames = new Av1FrameBuffer?[Av1Constants.TotalRefsPerFrame];
    private readonly int[] _xScale = new int[Av1Constants.TotalRefsPerFrame];
    private readonly int[] _yScale = new int[Av1Constants.TotalRefsPerFrame];

    /// <summary>Removes the reference frames: the frame being decoded is an intra frame.</summary>
    public void Clear()
    {
        Array.Clear(_frames);
        Array.Fill(_xScale, NoScale);
        Array.Fill(_yScale, NoScale);
    }

    /// <summary>Sets the frame buffer of a reference frame type.</summary>
    /// <exception cref="InvalidImageContentException">The reference frame cannot be used to predict the frame.</exception>
    public void Set(int refFrame, Av1FrameBuffer reference, Av1SequenceHeader sequence, Av1FrameHeader header)
    {
        var referenceSequence = reference.Sequence;
        if (referenceSequence.BitDepth != sequence.BitDepth
            || referenceSequence.SubsamplingX != sequence.SubsamplingX
            || referenceSequence.SubsamplingY != sequence.SubsamplingY
            || referenceSequence.PlaneCount != sequence.PlaneCount)
        {
            throw Av1Errors.Invalid("a reference frame does not have the format of the frame.");
        }

        // The requirements of the motion vector scaling process
        var referenceWidth = reference.Header.UpscaledWidth;
        var referenceHeight = reference.Header.FrameHeight;
        if (2 * header.FrameWidth < referenceWidth
            || 2 * header.FrameHeight < referenceHeight
            || header.FrameWidth > 16 * referenceWidth
            || header.FrameHeight > 16 * referenceHeight)
        {
            throw Av1Errors.Invalid("a reference frame does not have a size that the frame can be predicted from.");
        }

        _frames[refFrame] = reference;
        _xScale[refFrame] = (int)((((long)referenceWidth << Av1Constants.RefScaleShift) + (header.FrameWidth / 2)) / header.FrameWidth);
        _yScale[refFrame] = (int)((((long)referenceHeight << Av1Constants.RefScaleShift) + (header.FrameHeight / 2)) / header.FrameHeight);
    }

    /// <summary>Gets the frame buffer of a reference frame type.</summary>
    /// <exception cref="InvalidImageContentException">The frame has no such reference frame.</exception>
    public Av1FrameBuffer Get(int refFrame) => _frames[refFrame] ?? throw Av1Errors.Invalid("a block refers to a frame that is not available.");

    /// <summary>Gets <c>xScale</c>: the width of the reference frame relative to the frame, with 14 fractional bits.</summary>
    public int GetXScale(int refFrame) => _xScale[refFrame];

    /// <summary>Gets <c>yScale</c>: the height of the reference frame relative to the frame, with 14 fractional bits.</summary>
    public int GetYScale(int refFrame) => _yScale[refFrame];

    /// <summary>Returns <c>is_scaled( refFrame )</c>: whether the reference frame does not have the size of the frame.</summary>
    public bool IsScaled(int refFrame) => _xScale[refFrame] is not NoScale || _yScale[refFrame] is not NoScale;
}
