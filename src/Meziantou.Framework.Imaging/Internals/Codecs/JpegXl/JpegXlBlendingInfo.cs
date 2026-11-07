using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The <c>BlendingInfo</c> bundle (ISO/IEC 18181-1, section C.2.5): how a channel of a frame is combined with a reference frame.</summary>
/// <param name="Mode">The blend mode.</param>
/// <param name="AlphaChannel">The index of the extra channel used as alpha by the modes that need one.</param>
/// <param name="Clamp">Whether the alpha values are clamped to the range 0 to 1 before blending.</param>
/// <param name="Source">The reference slot (0 to 3) the frame is blended with.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct JpegXlBlendingInfo(int Mode, int AlphaChannel, bool Clamp, int Source)
{
    // Values of the mode field
    public const int ModeReplace = 0;
    public const int ModeAdd = 1;
    public const int ModeBlend = 2;
    public const int ModeMulAdd = 3;
    public const int ModeMul = 4;

    public static JpegXlBlendingInfo Read(JpegXlBitReader reader, int extraChannelCount, bool isPartialFrame)
    {
        var mode = (int)reader.ReadU32(0, 0, 1, 0, 2, 0, 3, 2);
        if (mode > ModeMul)
            throw JpegXlFormat.Invalid("a blend mode is not a defined value.");

        var alphaChannel = 0;
        var clamp = false;
        var usesAlpha = extraChannelCount > 0 && mode is ModeBlend or ModeMulAdd;
        if (usesAlpha)
        {
            alphaChannel = (int)reader.ReadU32(0, 0, 1, 0, 2, 0, 3, 3);
            if (alphaChannel >= extraChannelCount)
                throw JpegXlFormat.Invalid("a frame is blended with an alpha channel that the image does not have.");
        }

        if (usesAlpha || mode == ModeMul)
        {
            clamp = reader.ReadBool();
        }

        var source = mode != ModeReplace || isPartialFrame ? (int)reader.ReadBits(2) : 0;
        return new JpegXlBlendingInfo(mode, alphaChannel, clamp, source);
    }
}
