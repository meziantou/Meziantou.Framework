namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The coding tools that the blocks decoded by an <see cref="Av1Decoder"/> used: a diagnostic that tells which parts of the
/// decoding process a bitstream exercises. It does not take part in the decoding.
/// </summary>
[Flags]
internal enum Av1Tools : ulong
{
    None = 0,
    IntraBlockCopy = 1UL << 0,
    Palette = 1UL << 1,
    FilterIntra = 1UL << 2,
    ChromaFromLuma = 1UL << 3,
    DirectionalIntra = 1UL << 4,
    InterBlock = 1UL << 5,
    IntraBlockInInterFrame = 1UL << 6,
    CompoundReference = 1UL << 7,
    UnidirectionalCompound = 1UL << 8,
    SkipMode = 1UL << 9,
    NewMotionVector = 1UL << 10,
    GlobalMotionVector = 1UL << 11,
    TemporalMotionVector = 1UL << 12,
    OverlappedMotionCompensation = 1UL << 13,
    LocalWarp = 1UL << 14,
    GlobalWarp = 1UL << 15,
    WedgeCompound = 1UL << 16,
    DifferenceWeightedCompound = 1UL << 17,
    DistanceWeightedCompound = 1UL << 18,
    InterIntra = 1UL << 19,
    WedgeInterIntra = 1UL << 20,
    ScaledReference = 1UL << 21,
    VariableTransformSize = 1UL << 22,
    DualInterpolationFilter = 1UL << 23,
    BilinearInterpolationFilter = 1UL << 24,
    SubBlockChromaPrediction = 1UL << 25,
    PredictedSegmentId = 1UL << 26,
    SegmentReferenceFrame = 1UL << 27,
    LosslessBlock = 1UL << 28,
    IdentityTransform = 1UL << 29,
    Transform64 = 1UL << 30,
}
