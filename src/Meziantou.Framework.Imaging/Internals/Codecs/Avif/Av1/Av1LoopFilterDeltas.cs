using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The loop filter deltas of a frame (AV1 specification, section 5.9.11): <c>loop_filter_ref_deltas</c> for each reference frame
/// type and <c>loop_filter_mode_deltas</c>. A value type: loading the deltas of a reference frame is a copy.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct Av1LoopFilterDeltas
{
    public InlineArray8<sbyte> RefDeltas;
    public InlineArray2<sbyte> ModeDeltas;

    /// <summary>Gets the deltas of a frame that does not depend on a previous frame (<c>setup_past_independence</c>).</summary>
    public static Av1LoopFilterDeltas Default
    {
        get
        {
            var deltas = default(Av1LoopFilterDeltas);
            deltas.RefDeltas[Av1Constants.IntraFrame] = 1;
            deltas.RefDeltas[Av1Constants.GoldenFrame] = -1;
            deltas.RefDeltas[Av1Constants.AltrefFrame] = -1;
            deltas.RefDeltas[Av1Constants.Altref2Frame] = -1;
            return deltas;
        }
    }
}
