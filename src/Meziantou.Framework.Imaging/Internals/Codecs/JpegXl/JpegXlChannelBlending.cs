using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How a channel of a layer is combined with the same channel of another one: by a frame over a reference frame, or by a patch over a frame.</summary>
/// <param name="Mode">The mode, one of the constants of this type.</param>
/// <param name="AlphaChannel">The index of the extra channel that the modes that weight by alpha use.</param>
/// <param name="Clamp">Whether the alpha values, or the factors of a multiplication, are clamped to the range 0 to 1.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct JpegXlChannelBlending(int Mode, int AlphaChannel, bool Clamp)
{
    /// <summary>The channel keeps the samples it has.</summary>
    public const int ModeNone = 0;

    /// <summary>The channel takes the samples of the new layer.</summary>
    public const int ModeReplace = 1;

    /// <summary>The samples of the new layer are added.</summary>
    public const int ModeAdd = 2;

    /// <summary>The samples are multiplied by those of the new layer.</summary>
    public const int ModeMultiply = 3;

    /// <summary>The new layer is blended over the samples, weighted by its alpha.</summary>
    public const int ModeBlendAbove = 4;

    /// <summary>The samples are blended over the new layer, weighted by their alpha.</summary>
    public const int ModeBlendBelow = 5;

    /// <summary>The samples of the new layer, multiplied by its alpha, are added.</summary>
    public const int ModeAddWeightedAbove = 6;

    /// <summary>The samples, multiplied by their alpha, are added to the new layer.</summary>
    public const int ModeAddWeightedBelow = 7;

    /// <summary>The number of modes.</summary>
    public const int ModeCount = 8;

    /// <summary>Gets a value indicating whether the mode weights the samples by an alpha channel.</summary>
    public bool UsesAlpha => Mode is ModeBlendAbove or ModeBlendBelow or ModeAddWeightedAbove or ModeAddWeightedBelow;

    /// <summary>Converts the blending of a frame header.</summary>
    public static JpegXlChannelBlending FromFrame(JpegXlBlendingInfo info)
    {
        var mode = info.Mode switch
        {
            JpegXlBlendingInfo.ModeAdd => ModeAdd,
            JpegXlBlendingInfo.ModeBlend => ModeBlendAbove,
            JpegXlBlendingInfo.ModeMulAdd => ModeAddWeightedAbove,
            JpegXlBlendingInfo.ModeMul => ModeMultiply,
            _ => ModeReplace,
        };

        return new JpegXlChannelBlending(mode, info.AlphaChannel, info.Clamp);
    }
}
