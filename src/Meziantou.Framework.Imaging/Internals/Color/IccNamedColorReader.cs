using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the colors of an ICC named color profile: the <c>namedColor2Type</c> of version 2 and 4 profiles (ICC.1:2022
/// section 10.17), and the <c>namedColorTag</c> of version 5 profiles, a <c>tagArrayType</c> of <c>tagStructType</c>
/// structures (ICC.2:2023 sections 9.2.104, 10.2.23, 10.2.24, 12.2.5, 12.2.8 and 13.2.1).
/// </summary>
internal static class IccNamedColorReader
{
    private const uint TagNamedColor2 = 0x6E636C32; // 'ncl2'
    private const uint TagNamedColor = 0x6E6D636C; // 'nmcl'
    private const uint TypeNamedColor2 = 0x6E636C32; // 'ncl2'
    private const uint TypeTagArray = 0x74617279; // 'tary'
    private const uint TypeTagStruct = 0x74737472; // 'tstr'
    private const uint TypeUtf8 = 0x75746638; // 'utf8'
    private const uint TypeUInt8Array = 0x75693038; // 'ui08'
    private const uint TypeUInt16Array = 0x75693136; // 'ui16'
    private const uint TypeFloat16Array = 0x666C3136; // 'fl16'
    private const uint TypeFloat32Array = 0x666C3332; // 'fl32'
    private const uint ArrayNamedColors = 0x6E636F6C; // 'ncol'
    private const uint StructNamedColor = 0x6E6D636C; // 'nmcl'
    private const uint StructTintZero = 0x746E7430; // 'tnt0'
    private const uint MemberName = 0x6E616D65; // 'name'
    private const uint MemberPcs = 0x70637320; // 'pcs '
    private const uint MemberDevice = 0x64657620; // 'dev '
    private const uint MemberTint = 0x74696E74; // 'tint'

    /// <summary>The largest number of device coordinates: the 15 colorants of the 'FCLR' color space.</summary>
    private const int MaxDeviceCoordinates = 15;

    /// <summary>Reads the colors of a named color profile.</summary>
    /// <param name="data">The profile bytes.</param>
    /// <param name="role">The role of the profile, for exception messages.</param>
    /// <param name="deviceChannelCount">The number of device values of each color, 0 when the profile gives none.</param>
    /// <returns>The colors, in the order of the profile.</returns>
    /// <exception cref="InvalidImageContentException">The profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The profile is valid but its colors cannot be read.</exception>
    public static IccNamedColor[] Read(ReadOnlySpan<byte> data, string role, out int deviceChannelCount)
    {
        if (!MetadataValidation.TryValidateIccProfile(data, out var error))
            throw Invalid(role, error);

        _ = IccHeader.TryRead(data, out var header);
        if (header.Version.Major > 5)
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The {role} ICC profile has version {header.Version.Major}.{header.Version.Minor}; only versions 2, 4 and 5 (iccMAX) are supported."), ImageFormat.Unknown, "ICC profile version");

        if (header.ConnectionSpace is not (IccReader.SignatureXyz or IccReader.SignatureLab))
        {
            if (header.Version.Major >= 5 && header.ConnectionSpace == 0)
                throw new UnsupportedImageFeatureException($"The {role} ICC profile describes its colors with spectral data only (no colorimetric profile connection space).", ImageFormat.Unknown, "ICC named colors with a spectral connection space");

            throw Invalid(role, $"the profile connection space {IccReader.FormatSignature(header.ConnectionSpace)} is neither 'XYZ ' nor 'Lab '.");
        }

        var isLab = header.ConnectionSpace == IccReader.SignatureLab;
        if (header.Version.Major >= 5)
            return ReadTagArray(data, header, isLab, role, out deviceChannelCount);

        if (!IccReader.TryGetTag(data, TagNamedColor2, out var tag))
            throw Invalid(role, "the named color profile has no 'ncl2' tag.");

        return ReadNamedColor2(tag, header, isLab, role, out deviceChannelCount);
    }

    // ICC.1:2022 section 10.17, Table 66
    private static IccNamedColor[] ReadNamedColor2(ReadOnlySpan<byte> tag, IccHeader header, bool isLab, string role, out int deviceChannelCount)
    {
        if (tag.Length < 84 || IccReader.ReadUInt32(tag) != TypeNamedColor2)
            throw Invalid(role, "the 'ncl2' tag is not a namedColor2Type.");

        var count = IccReader.ReadUInt32(tag[12..]);
        var coordinates = IccReader.ReadUInt32(tag[16..]);
        if (coordinates > MaxDeviceCoordinates)
            throw Invalid(role, string.Create(CultureInfo.InvariantCulture, $"the 'ncl2' tag declares {coordinates} device coordinates, more than the {MaxDeviceCoordinates} colorants of the largest color space."));

        if (GetDeviceChannelCount(header.DataColorSpace) is { } expected && coordinates != 0 && coordinates != expected)
            throw Invalid(role, string.Create(CultureInfo.InvariantCulture, $"the 'ncl2' tag declares {coordinates} device coordinates for the data color space {IccReader.FormatSignature(header.DataColorSpace)}, which has {expected}."));

        var colorSize = 38 + (2 * (int)coordinates);
        if (84 + ((long)count * colorSize) > tag.Length)
            throw Invalid(role, string.Create(CultureInfo.InvariantCulture, $"the 'ncl2' tag declares {count} colors, which do not fit in its {tag.Length} bytes."));

        var prefix = ReadAsciiName(tag.Slice(20, 32), role, "prefix");
        var suffix = ReadAsciiName(tag.Slice(52, 32), role, "suffix");
        var colors = new IccNamedColor[count];
        for (var i = 0; i < colors.Length; i++)
        {
            var entry = tag.Slice(84 + (i * colorSize), colorSize);
            var root = ReadAsciiName(entry[..32], role, "color name");

            // The PCS coordinates use the legacy 16-bit PCSLAB encoding of the lut16Type (L* 100 is FF00h, a* 0 is 8000h),
            // or the u1Fixed15Number encoding of PCSXYZ (1.0 is 8000h)
            var first = IccReader.ReadUInt16(entry[32..]);
            var second = IccReader.ReadUInt16(entry[34..]);
            var third = IccReader.ReadUInt16(entry[36..]);
            var (xyz, lab) = isLab
                ? FromLab(first * 100.0 / 65280, (second / 256.0) - 128, (third / 256.0) - 128)
                : FromXyz(first / 32768.0, second / 32768.0, third / 32768.0);

            var device = new double[coordinates];
            for (var j = 0; j < device.Length; j++)
            {
                device[j] = IccReader.ReadUInt16(entry[(38 + (2 * j))..]) / 65535.0;
            }

            colors[i] = new IccNamedColor(prefix + root + suffix, xyz, lab, device);
        }

        deviceChannelCount = (int)coordinates;
        return colors;
    }

    private static string ReadAsciiName(ReadOnlySpan<byte> field, string role, string what)
    {
        var length = field.IndexOf((byte)0);
        if (length < 0)
            throw Invalid(role, $"a {what} of the 'ncl2' tag is not terminated by a null character within its 32 bytes.");

        var text = field[..length];
        if (!Ascii.IsValid(text))
            throw Invalid(role, $"a {what} of the 'ncl2' tag is not 7-bit ASCII.");

        return Encoding.ASCII.GetString(text);
    }

    // ICC.2:2023 section 9.2.104: a namedColorArray (13.2.1), a tintZeroStructure (12.2.8) then namedColorStructures (12.2.5)
    private static IccNamedColor[] ReadTagArray(ReadOnlySpan<byte> data, IccHeader header, bool isLab, string role, out int deviceChannelCount)
    {
        if (!IccReader.TryGetTag(data, TagNamedColor, out var tag))
            throw Invalid(role, "the named color profile has no 'nmcl' tag.");

        if (tag.Length < 16 || IccReader.ReadUInt32(tag) != TypeTagArray)
            throw Invalid(role, "the 'nmcl' tag is not a tagArrayType.");

        if (IccReader.ReadUInt32(tag[8..]) != ArrayNamedColors)
            throw Invalid(role, $"the 'nmcl' tag is an array of {IccReader.FormatSignature(IccReader.ReadUInt32(tag[8..]))} instead of a namedColorArray ('ncol').");

        var count = IccReader.ReadUInt32(tag[12..]);
        if (count == 0 || 16 + ((long)count * 8) > tag.Length)
            throw Invalid(role, string.Create(CultureInfo.InvariantCulture, $"the 'nmcl' tag declares {count} elements, which do not fit in its {tag.Length} bytes or omit the tint zero structure."));

        var channels = GetDeviceChannelCount(header.DataColorSpace);
        var colors = new IccNamedColor[count - 1];
        deviceChannelCount = 0;
        for (var i = 0; i < count; i++)
        {
            var element = GetPosition(tag, 16 + (8 * i), role, "'nmcl' tag");
            if (element.Length < 16 || IccReader.ReadUInt32(element) != TypeTagStruct)
                throw Invalid(role, "an element of the 'nmcl' tag is not a tagStructType.");

            var structType = IccReader.ReadUInt32(element[8..]);
            if (i == 0)
            {
                if (structType != StructTintZero)
                    throw Invalid(role, "the first element of the 'nmcl' tag is not a tintZeroStructure ('tnt0').");

                continue;
            }

            if (structType != StructNamedColor)
                throw Invalid(role, string.Create(CultureInfo.InvariantCulture, $"the element {i} of the 'nmcl' tag is not a namedColorStructure ('nmcl')."));

            var name = TryGetMember(element, MemberName, role, out var nameData) ? ReadUtf8(nameData, role) : throw Invalid(role, string.Create(CultureInfo.InvariantCulture, $"the named color {i} has no name."));
            var tints = TryGetMember(element, MemberTint, role, out var tintData) ? ReadNumbers(tintData, role, "tint", allowIntegers: true) : null;
            if (!TryGetMember(element, MemberPcs, role, out var pcsData))
                throw Invalid(role, $"the named color '{name}' has no PCS values.");

            var pcs = ReadNumbers(pcsData, role, "PCS", allowIntegers: false);
            var full = GetFullTint(pcs, 3, tints, name, role);
            var (xyz, lab) = isLab ? FromLab(full[0], full[1], full[2]) : FromXyz(full[0], full[1], full[2]);

            var device = Array.Empty<double>();
            if (TryGetMember(element, MemberDevice, role, out var deviceData))
            {
                if (channels is not { } deviceChannels)
                    throw new UnsupportedImageFeatureException($"The {role} ICC profile gives device values for the data color space {IccReader.FormatSignature(header.DataColorSpace)}; only gray, RGB and CMYK are supported.", ImageFormat.Unknown, "ICC profile data color space");

                device = GetFullTint(ReadNumbers(deviceData, role, "device", allowIntegers: true), deviceChannels, tints, name, role);
                deviceChannelCount = deviceChannels;
            }

            colors[i - 1] = new IccNamedColor(name, xyz, lab, device);
        }

        // Colors without device values next to colors with them: the collection reports the channels of the device
        if (deviceChannelCount != 0 && Array.Exists(colors, color => color.DeviceValues.Count == 0))
            throw new UnsupportedImageFeatureException($"The {role} ICC profile gives device values for some of its named colors only.", ImageFormat.Unknown, "ICC named colors with partial device values");

        return colors;
    }

    // The values of a tint array (ICC.2:2023 section 4.2.9) at full tint: the vector whose tint is 1.0. Without tint
    // values, the vectors are equally spaced tints and the last one is the full tint (12.2.5.2.9)
    private static double[] GetFullTint(double[] values, int channels, double[]? tints, string name, string role)
    {
        if (values.Length == 0 || values.Length % channels != 0)
            throw Invalid(role, string.Create(CultureInfo.InvariantCulture, $"the named color '{name}' has {values.Length} values, which are not vectors of {channels} values."));

        var vectors = values.Length / channels;
        var index = vectors - 1;
        if (tints is not null)
        {
            if (tints.Length != vectors)
                throw Invalid(role, string.Create(CultureInfo.InvariantCulture, $"the named color '{name}' has {tints.Length} tints for {vectors} vectors of values."));

            index = Array.IndexOf(tints, 1.0);
            if (index < 0)
                throw new UnsupportedImageFeatureException($"The {role} ICC profile has no value at full tint (1.0) for the named color '{name}'.", ImageFormat.Unknown, "ICC named color without full tint");
        }

        return values.AsSpan(index * channels, channels).ToArray();
    }

    private static bool TryGetMember(ReadOnlySpan<byte> element, uint signature, string role, out ReadOnlySpan<byte> member)
    {
        var count = IccReader.ReadUInt32(element[12..]);
        if (16 + ((long)count * 12) > element.Length)
            throw Invalid(role, string.Create(CultureInfo.InvariantCulture, $"a structure of the 'nmcl' tag declares {count} members, which do not fit in its {element.Length} bytes."));

        for (var i = 0; i < count; i++)
        {
            var entry = 16 + (12 * i);
            if (IccReader.ReadUInt32(element[entry..]) == signature)
            {
                member = GetPosition(element, entry + 4, role, "structure of the 'nmcl' tag");
                return true;
            }
        }

        member = default;
        return false;
    }

    // A positionNumber (offset and size relative to the start of the containing structure) inside it
    private static ReadOnlySpan<byte> GetPosition(ReadOnlySpan<byte> container, int position, string role, string what)
    {
        var offset = IccReader.ReadUInt32(container[position..]);
        var size = IccReader.ReadUInt32(container[(position + 4)..]);
        if (offset > (uint)container.Length || size > (uint)container.Length - offset)
            throw Invalid(role, string.Create(CultureInfo.InvariantCulture, $"an element of the {what} ({size} bytes at offset {offset}) is outside it."));

        return container.Slice((int)offset, (int)size);
    }

    private static string ReadUtf8(ReadOnlySpan<byte> member, string role)
    {
        if (member.Length < 8 || IccReader.ReadUInt32(member) != TypeUtf8)
            throw Invalid(role, "the name of a named color is not a utf8Type.");

        var text = member[8..];
        var end = text.IndexOf((byte)0);
        if (end >= 0)
        {
            text = text[..end];
        }

        if (!System.Text.Unicode.Utf8.IsValid(text))
            throw Invalid(role, "the name of a named color is not valid UTF-8.");

        return Encoding.UTF8.GetString(text);
    }

    // The numeric array types (ICC.2:2023 sections 10.2.9, 10.2.10, 10.2.27 and 10.2.30). Integers encode [0, 1] (4.2.10)
    private static double[] ReadNumbers(ReadOnlySpan<byte> member, string role, string what, bool allowIntegers)
    {
        if (member.Length < 8)
            throw Invalid(role, $"the {what} values of a named color are not a numeric array.");

        var type = IccReader.ReadUInt32(member);
        var values = member[8..];
        if (!allowIntegers && type is TypeUInt8Array or TypeUInt16Array)
            throw new UnsupportedImageFeatureException($"The {role} ICC profile stores the {what} values of a named color as integers; only floating-point values are supported.", ImageFormat.Unknown, "ICC named color with integer PCS values");

        var size = type switch
        {
            TypeUInt8Array => 1,
            TypeUInt16Array or TypeFloat16Array => 2,
            TypeFloat32Array => 4,
            _ => throw Invalid(role, $"the {what} values of a named color are a {IccReader.FormatSignature(type)} instead of a numeric array."),
        };

        var result = new double[values.Length / size];
        for (var i = 0; i < result.Length; i++)
        {
            var value = values[(i * size)..];
            result[i] = type switch
            {
                TypeUInt8Array => value[0] / 255.0,
                TypeUInt16Array => BinaryPrimitives.ReadUInt16BigEndian(value) / 65535.0,
                TypeFloat16Array => (double)BinaryPrimitives.ReadHalfBigEndian(value),
                _ => BinaryPrimitives.ReadSingleBigEndian(value),
            };

            if (!double.IsFinite(result[i]))
                throw Invalid(role, $"the {what} values of a named color are not finite numbers.");
        }

        return result;
    }

    private static (CieXyz Xyz, CieLab Lab) FromLab(double l, double a, double b)
    {
        var fy = (l + 16) / 116;
        var xyz = new CieXyz(IccColorimetry.D50X * IccColorimetry.LabInverse(fy + (a / 500)), IccColorimetry.D50Y * IccColorimetry.LabInverse(fy), IccColorimetry.D50Z * IccColorimetry.LabInverse(fy - (b / 200)));
        return (xyz, new CieLab(l, a, b));
    }

    private static (CieXyz Xyz, CieLab Lab) FromXyz(double x, double y, double z)
    {
        var fy = IccColorimetry.LabFunction(y / IccColorimetry.D50Y);
        var lab = new CieLab((116 * fy) - 16, 500 * (IccColorimetry.LabFunction(x / IccColorimetry.D50X) - fy), 200 * (fy - IccColorimetry.LabFunction(z / IccColorimetry.D50Z)));
        return (new CieXyz(x, y, z), lab);
    }

    private static int? GetDeviceChannelCount(uint colorSpace) => colorSpace switch
    {
        IccReader.SignatureGray => 1,
        IccReader.SignatureRgb => 3,
        IccReader.SignatureCmyk => 4,
        _ => null,
    };

    private static InvalidImageContentException Invalid(string role, string message)
        => new($"The {role} ICC profile is invalid: {message}", ImageFormat.Unknown);
}
