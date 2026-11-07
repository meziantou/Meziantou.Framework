namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A <c>colr</c> box (T.800, I.5.3.3): how the samples are to be interpreted as colors.</summary>
internal sealed class Jpeg2000ColorSpecification
{
    /// <summary>METH 1: an enumerated color space.</summary>
    public const int MethodEnumerated = 1;

    /// <summary>METH 2: a restricted ICC profile (a monochrome or three-component matrix-based input profile).</summary>
    public const int MethodRestrictedIcc = 2;

    /// <summary>METH 3: any ICC profile (T.801, M.11.7.2).</summary>
    public const int MethodAnyIcc = 3;

    /// <summary>EnumCS 12: CMYK (T.801, Table M.25).</summary>
    public const uint EnumeratedCmyk = 12;

    /// <summary>EnumCS 16: sRGB.</summary>
    public const uint EnumeratedSrgb = 16;

    /// <summary>EnumCS 17: grayscale, with the non-linearity of sRGB.</summary>
    public const uint EnumeratedGray = 17;

    /// <summary>EnumCS 18: sYCC.</summary>
    public const uint EnumeratedSycc = 18;

    public Jpeg2000ColorSpecification(int method, uint enumeratedColorSpace, byte[]? profile)
    {
        Method = method;
        EnumeratedColorSpace = enumeratedColorSpace;
        Profile = profile;
    }

    /// <summary>Gets the METH field.</summary>
    public int Method { get; }

    /// <summary>Gets the EnumCS field when <see cref="Method"/> is <see cref="MethodEnumerated"/>.</summary>
    public uint EnumeratedColorSpace { get; }

    /// <summary>Gets the ICC profile when <see cref="Method"/> is <see cref="MethodRestrictedIcc"/> or <see cref="MethodAnyIcc"/>. Its bytes are charged to the metadata limit.</summary>
    public byte[]? Profile { get; }
}
