namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The tone curves <see cref="BuiltInIccProfiles"/> writes: the function that decodes samples to linear light.</summary>
internal enum BuiltInIccCurve
{
    /// <summary>The samples are linear.</summary>
    Linear,

    /// <summary>The decoding function of IEC 61966-2-1 (sRGB).</summary>
    Srgb,

    /// <summary>The decoding function of Rec. ITU-R BT.709.</summary>
    Bt709,

    /// <summary>A power function.</summary>
    Power,
}
