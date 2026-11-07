namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Half-precision floating-point samples (<see cref="RgbaHalf"/>), clipped to [0, 1] in both directions and stored as the
/// nearest half; a value that is not a number is read as 0.
/// </summary>
internal readonly struct IccHalfSample : IIccSample<Half>
{
    public static double Load(Half value) => IccCurve.Clip((double)value);

    public static Half Store(double value) => (Half)IccCurve.Clip(value);
}
