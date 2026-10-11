using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The global motion parameters of a frame (AV1 specification, section 5.9.24): <c>gm_params[ ref ][ 0..5 ]</c> and
/// <c>GmType[ ref ]</c> for each reference frame type. A value type: the parameters of the primary reference frame are a copy.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct Av1GlobalMotion
{
    public InlineArray8<InlineArray6<int>> Parameters;
    public InlineArray8<byte> Types;

    /// <summary>Gets the identity model for every reference frame.</summary>
    public static Av1GlobalMotion Identity
    {
        get
        {
            var result = default(Av1GlobalMotion);
            for (var reference = Av1Constants.LastFrame; reference <= Av1Constants.AltrefFrame; reference++)
            {
                result.Types[reference] = Av1Constants.Identity;
                result.Parameters[reference][2] = 1 << Av1Constants.WarpedmodelPrecBits;
                result.Parameters[reference][5] = 1 << Av1Constants.WarpedmodelPrecBits;
            }

            return result;
        }
    }
}
