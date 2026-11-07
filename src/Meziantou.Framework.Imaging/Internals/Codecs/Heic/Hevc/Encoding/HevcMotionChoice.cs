using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The syntax elements of a prediction unit that the HEVC encoder chose (Rec. ITU-T H.265, clause 7.3.8.6 "Prediction unit
/// syntax"): a merge candidate, or a motion vector difference with its predictor, for the only reference picture of the
/// list 0.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal record struct HevcMotionChoice(bool MergeFlag, int MergeIdx, int MvdX, int MvdY, int MvpFlag);
