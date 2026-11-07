using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The fields of a Graphic Control Extension.</summary>
/// <param name="DisposalMethod">The raw disposal method (0-7; 4-7 are undefined).</param>
/// <param name="HasTransparency">Whether <paramref name="TransparentIndex"/> is transparent.</param>
/// <param name="DelayHundredths">The raw delay field, in hundredths of a second.</param>
/// <param name="TransparentIndex">The transparent color index.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GifGraphicControl(int DisposalMethod, bool HasTransparency, ushort DelayHundredths, byte TransparentIndex)
{
    public const int DisposeNotSpecified = 0;
    public const int DisposeDoNotDispose = 1;
    public const int DisposeRestoreBackground = 2;
    public const int DisposeRestorePrevious = 3;

    /// <summary>Gets the exact frame duration.</summary>
    public FrameDuration Duration => AnimationTiming.FromGifDelay(DelayHundredths);

    /// <summary>
    /// Gets the disposal method as interpreted by the decoder: 0-3 as defined; the undefined value 4
    /// is restore-to-previous (Chromium, Firefox and Apple ImageIO all treat it so); 5-7 are "not specified" (no disposal).
    /// </summary>
    public int EffectiveDisposalMethod => GetEffectiveDisposalMethod(DisposalMethod);

    /// <summary>Normalizes a raw disposal method (see <see cref="EffectiveDisposalMethod"/>).</summary>
    public static int GetEffectiveDisposalMethod(int disposalMethod) => disposalMethod switch
    {
        DisposeDoNotDispose or DisposeRestoreBackground or DisposeRestorePrevious => disposalMethod,
        4 => DisposeRestorePrevious,
        _ => DisposeNotSpecified,
    };
}
