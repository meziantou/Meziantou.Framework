namespace Meziantou.Framework.Imaging.Internals;

/// <summary>16-bit samples: 0 to 65,535 is 0 to 1.</summary>
internal readonly struct IccUInt16Sample : IIccSample<ushort>
{
    public static double Load(ushort value) => value / 65535.0;

    public static ushort Store(double value) => (ushort)((IccCurve.Clip(value) * 65535) + 0.5);
}
