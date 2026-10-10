namespace Meziantou.Framework.Imaging.Internals;

/// <summary>8-bit samples: 0 to 255 is 0 to 1.</summary>
internal readonly struct IccByteSample : IIccSample<byte>
{
    public static double Load(byte value) => value / 255.0;

    public static byte Store(double value) => (byte)((IccCurve.Clip(value) * 255) + 0.5);
}
