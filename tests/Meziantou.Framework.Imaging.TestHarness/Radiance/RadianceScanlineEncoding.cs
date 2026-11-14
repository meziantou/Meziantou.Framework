namespace Meziantou.Framework.Imaging.TestHarness.Radiance;

/// <summary>The encodings of a scanline of a Radiance HDR file.</summary>
public enum RadianceScanlineEncoding
{
    /// <summary>Four bytes per pixel.</summary>
    Flat,

    /// <summary>The "old" encoding: repetitions of the previous pixel among flat pixels.</summary>
    OldRunLength,

    /// <summary>The "new" encoding: the four components apart, in runs and literals.</summary>
    NewRunLength,
}
