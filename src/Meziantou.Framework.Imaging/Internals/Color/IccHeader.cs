using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The fields of an ICC profile header (ICC.1:2022 section 7.2) used by the library.</summary>
internal readonly struct IccHeader
{
    private IccHeader(ReadOnlySpan<byte> data)
    {
        // The version is a major byte, then the minor and bug fix numbers in the two halves of the next byte
        Version = new Version(data[8], data[9] >> 4, data[9] & 0x0F);
        ProfileClass = IccReader.ReadUInt32(data[12..]);
        DataColorSpace = IccReader.ReadUInt32(data[16..]);
        ConnectionSpace = IccReader.ReadUInt32(data[20..]);
        RenderingIntent = IccReader.ReadUInt32(data[64..]);
    }

    /// <summary>Gets the profile version (major, minor, bug fix).</summary>
    public Version Version { get; }

    /// <summary>Gets the profile/device class signature.</summary>
    public uint ProfileClass { get; }

    /// <summary>Gets the signature of the data color space.</summary>
    public uint DataColorSpace { get; }

    /// <summary>Gets the signature of the profile connection space.</summary>
    public uint ConnectionSpace { get; }

    /// <summary>Gets the raw rendering intent field.</summary>
    public uint RenderingIntent { get; }

    /// <summary>Reads the header of a profile.</summary>
    /// <param name="data">The profile bytes.</param>
    /// <param name="header">The header.</param>
    /// <returns><see langword="false"/> if the data is shorter than a header.</returns>
    public static bool TryRead(ReadOnlySpan<byte> data, out IccHeader header)
    {
        if (data.Length < IccReader.HeaderSize)
        {
            header = default;
            return false;
        }

        header = new IccHeader(data);
        return true;
    }

    /// <summary>Maps the data color space signature to the public enumeration.</summary>
    public IccProfileColorSpace GetColorSpace() => DataColorSpace switch
    {
        IccReader.SignatureGray => IccProfileColorSpace.Gray,
        IccReader.SignatureRgb => IccProfileColorSpace.Rgb,
        IccReader.SignatureCmyk => IccProfileColorSpace.Cmyk,
        _ => IccProfileColorSpace.Other,
    };

    /// <summary>Maps the profile class signature to the public enumeration.</summary>
    public IccProfileClass GetProfileClass() => ProfileClass switch
    {
        IccReader.ClassInput => IccProfileClass.Input,
        IccReader.ClassDisplay => IccProfileClass.Display,
        IccReader.ClassOutput => IccProfileClass.Output,
        IccReader.ClassDeviceLink => IccProfileClass.DeviceLink,
        IccReader.ClassColorSpace => IccProfileClass.ColorSpace,
        IccReader.ClassAbstract => IccProfileClass.Abstract,
        IccReader.ClassNamedColor => IccProfileClass.NamedColor,
        _ => IccProfileClass.Other,
    };

    /// <summary>Maps the rendering intent field to the public enumeration, or <see langword="null"/> for an undefined value.</summary>
    public IccRenderingIntent? GetRenderingIntent() => RenderingIntent <= (uint)IccRenderingIntent.AbsoluteColorimetric ? (IccRenderingIntent)RenderingIntent : null;
}
