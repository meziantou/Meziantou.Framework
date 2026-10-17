namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Floating-point samples, clipped to [0, 1] in both directions; a value that is not a number is read as 0.</summary>
internal readonly struct IccSingleSample : IIccSample<float>
{
    public static double Load(float value) => IccCurve.Clip(value);

    public static float Store(double value) => (float)IccCurve.Clip(value);
}
