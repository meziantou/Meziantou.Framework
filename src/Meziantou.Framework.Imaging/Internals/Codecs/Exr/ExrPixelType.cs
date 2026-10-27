namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The pixel type of an OpenEXR channel (the <c>pixel type</c> field of a <c>chlist</c> entry).</summary>
internal enum ExrPixelType
{
    /// <summary>32-bit unsigned integers.</summary>
    UInt = 0,

    /// <summary>IEEE 754 binary16 floating-point numbers.</summary>
    Half = 1,

    /// <summary>IEEE 754 binary32 floating-point numbers.</summary>
    Float = 2,
}
