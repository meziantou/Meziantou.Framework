using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The uncompressed header of an AV1 frame (AV1 specification, section 5.9) and the variables derived from it. One instance is
/// created per frame by <see cref="Av1FrameHeaderParser"/> and is not modified afterwards: a reference frame keeps the header of
/// the frame that produced it, which is the state that later frames load from it (order hints, global motion, loop filter deltas,
/// segmentation features, film grain).
/// </summary>
internal sealed class Av1FrameHeader
{
    public bool ShowExistingFrame { get; set; }

    public int FrameToShowMapIdx { get; set; }

    /// <summary>Gets or sets <c>frame_type</c>: <see cref="Av1Constants.KeyFrame"/>, <see cref="Av1Constants.InterFrame"/>, <see cref="Av1Constants.IntraOnlyFrame"/> or <see cref="Av1Constants.SwitchFrame"/>.</summary>
    public int FrameType { get; set; }

    public bool FrameIsIntra { get; set; }

    public bool ShowFrame { get; set; }

    public bool ShowableFrame { get; set; }

    public bool ErrorResilientMode { get; set; }

    public bool DisableCdfUpdate { get; set; }

    public bool AllowScreenContentTools { get; set; }

    public bool ForceIntegerMv { get; set; }

    public int CurrentFrameId { get; set; }

    public bool FrameSizeOverrideFlag { get; set; }

    public int OrderHint { get; set; }

    /// <summary>Gets or sets <c>primary_ref_frame</c>, <see cref="Av1Constants.PrimaryRefNone"/> when the frame loads no state.</summary>
    public int PrimaryRefFrame { get; set; }

    public int RefreshFrameFlags { get; set; }

    public bool AllowIntrabc { get; set; }

    public bool AllowHighPrecisionMv { get; set; }

    public bool UseRefFrameMvs { get; set; }

    /// <summary><c>ref_frame_idx[ 0..6 ]</c>: the reference slot of LAST_FRAME + i.</summary>
    public InlineArray8<sbyte> RefFrameIdx;

    /// <summary><c>OrderHints[ refFrame ]</c> for LAST_FRAME to ALTREF_FRAME.</summary>
    public InlineArray8<int> OrderHints;

    /// <summary><c>RefFrameSignBias[ refFrame ]</c> for LAST_FRAME to ALTREF_FRAME.</summary>
    public InlineArray8<bool> RefFrameSignBias;

    /// <summary>Gets or sets the width of the coded frame, before super-resolution upscaling.</summary>
    public int FrameWidth { get; set; }

    public int FrameHeight { get; set; }

    /// <summary>Gets or sets the width of the frame after super-resolution upscaling.</summary>
    public int UpscaledWidth { get; set; }

    public int RenderWidth { get; set; }

    public int RenderHeight { get; set; }

    public bool UseSuperres { get; set; }

    public int SuperresDenom { get; set; }

    public int MiCols { get; set; }

    public int MiRows { get; set; }

    /// <summary>Gets or sets <c>interpolation_filter</c>, <see cref="Av1Constants.Switchable"/> when blocks select their filter.</summary>
    public int InterpolationFilter { get; set; }

    public bool IsMotionModeSwitchable { get; set; }

    public bool DisableFrameEndUpdateCdf { get; set; }

    public int TileCols { get; set; }

    public int TileRows { get; set; }

    public int TileColsLog2 { get; set; }

    public int TileRowsLog2 { get; set; }

    /// <summary>Gets or sets <c>MiColStarts</c>: <see cref="TileCols"/> + 1 entries.</summary>
    public int[] MiColStarts { get; set; } = [];

    /// <summary>Gets or sets <c>MiRowStarts</c>: <see cref="TileRows"/> + 1 entries.</summary>
    public int[] MiRowStarts { get; set; } = [];

    public int ContextUpdateTileId { get; set; }

    public int TileSizeBytes { get; set; }

    public int BaseQIdx { get; set; }

    public int DeltaQYDc { get; set; }

    public int DeltaQUDc { get; set; }

    public int DeltaQUAc { get; set; }

    public int DeltaQVDc { get; set; }

    public int DeltaQVAc { get; set; }

    public bool UsingQmatrix { get; set; }

    public int QmY { get; set; }

    public int QmU { get; set; }

    public int QmV { get; set; }

    public bool SegmentationEnabled { get; set; }

    public bool SegmentationUpdateMap { get; set; }

    public bool SegmentationTemporalUpdate { get; set; }

    public bool SegmentationUpdateData { get; set; }

    public Av1SegmentationFeatures Segmentation;

    public bool SegIdPreSkip { get; set; }

    public int LastActiveSegId { get; set; }

    public bool DeltaQPresent { get; set; }

    public int DeltaQRes { get; set; }

    public bool DeltaLfPresent { get; set; }

    public int DeltaLfRes { get; set; }

    public bool DeltaLfMulti { get; set; }

    public bool CodedLossless { get; set; }

    public bool AllLossless { get; set; }

    /// <summary><c>LosslessArray[ segmentId ]</c>.</summary>
    public InlineArray8<bool> LosslessArray;

    /// <summary><c>loop_filter_level[ 0..3 ]</c>: luma vertical, luma horizontal, U, V.</summary>
    public InlineArray4<byte> LoopFilterLevel;

    public int LoopFilterSharpness { get; set; }

    public bool LoopFilterDeltaEnabled { get; set; }

    public bool LoopFilterDeltaUpdate { get; set; }

    public Av1LoopFilterDeltas LoopFilterDeltas;

    public int CdefDamping { get; set; }

    public int CdefBits { get; set; }

    public InlineArray8<byte> CdefYPriStrength;

    public InlineArray8<byte> CdefYSecStrength;

    public InlineArray8<byte> CdefUvPriStrength;

    public InlineArray8<byte> CdefUvSecStrength;

    /// <summary><c>FrameRestorationType[ plane ]</c>.</summary>
    public InlineArray3<byte> FrameRestorationType;

    public bool UsesLr { get; set; }

    /// <summary><c>LoopRestorationSize[ plane ]</c>.</summary>
    public InlineArray3<int> LoopRestorationSize;

    /// <summary>Gets or sets <c>TxMode</c>: <see cref="Av1Constants.Only4x4"/>, <see cref="Av1Constants.TxModeLargest"/> or <see cref="Av1Constants.TxModeSelect"/>.</summary>
    public int TxMode { get; set; }

    public bool ReferenceSelect { get; set; }

    public bool SkipModePresent { get; set; }

    /// <summary><c>SkipModeFrame[ 0..1 ]</c>.</summary>
    public InlineArray2<byte> SkipModeFrame;

    public bool AllowWarpedMotion { get; set; }

    public bool ReducedTxSet { get; set; }

    public Av1GlobalMotion GlobalMotion;

    /// <summary>Gets or sets the film grain parameters, <see langword="null"/> when no grain is applied to the frame.</summary>
    public Av1FilmGrainParameters? FilmGrain { get; set; }

    /// <summary>Gets or sets the temporal layer of the frame (0 without OBU extension).</summary>
    public int TemporalId { get; set; }

    /// <summary>Gets or sets the spatial layer of the frame (0 without OBU extension).</summary>
    public int SpatialId { get; set; }

    /// <summary>Gets the number of tiles of the frame.</summary>
    public int TileCount => TileCols * TileRows;

    /// <summary>Returns <c>seg_feature_active_idx( segmentId, feature )</c> (section 5.11.14).</summary>
    public bool IsSegmentationFeatureActive(int segmentId, int feature) => SegmentationEnabled && Segmentation.IsEnabled(segmentId, feature);

    /// <summary>
    /// Returns the quantizer index of a segment (<c>get_qindex</c>, section 7.12.2). <paramref name="currentQIndex"/> is the
    /// <c>CurrentQIndex</c> of the block, or -1 to ignore the block-level delta (<c>ignoreDeltaQ</c> equal to 1).
    /// </summary>
    public int GetQIndex(int segmentId, int currentQIndex)
    {
        var useDelta = currentQIndex >= 0 && DeltaQPresent;
        if (IsSegmentationFeatureActive(segmentId, Av1Constants.SegLvlAltQ))
        {
            var data = Segmentation.GetData(segmentId, Av1Constants.SegLvlAltQ);
            return Math.Clamp((useDelta ? currentQIndex : BaseQIdx) + data, 0, 255);
        }

        return useDelta ? currentQIndex : BaseQIdx;
    }
}
