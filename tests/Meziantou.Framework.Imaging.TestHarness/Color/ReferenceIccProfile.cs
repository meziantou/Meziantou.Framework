using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Color;

/// <summary>
/// The color model of an ICC profile for <see cref="ReferenceIccTransform"/>, read with its own tag reader from the
/// profile bytes: device values to CIEXYZ of the profile connection space (relative to D50) and back.
/// </summary>
internal sealed class ReferenceIccProfile
{
    private readonly byte[] _data;

    public ReferenceIccProfile(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
        ColorSpace = Encoding.ASCII.GetString(_data, 16, 4);
        ConnectionSpace = Encoding.ASCII.GetString(_data, 20, 4);
        ChannelCount = ColorSpace switch
        {
            "GRAY" => 1,
            "RGB " => 3,
            "CMYK" => 4,
            _ => throw new NotSupportedException($"Data color space '{ColorSpace}' is not supported by the reference."),
        };
    }

    public string ColorSpace { get; }

    public string ConnectionSpace { get; }

    public int ChannelCount { get; }

    /// <summary>Gets a value indicating whether the profile is a CMYK output (printer) profile.</summary>
    public bool IsCmykOutputProfile => ChannelCount == 4 && Encoding.ASCII.GetString(_data, 12, 4) == "prtr";

    /// <summary>Whether the conversion of an intent goes through a lookup table of the profile.</summary>
    public bool UsesLookupTable(bool deviceToConnection, int intent) => FindLookupTable(deviceToConnection, intent) is not null;

    /// <summary>Whether the profile has the tags of a conversion: a lookup table, or tone curves (with colorants for RGB).</summary>
    public bool HasConversion(bool deviceToConnection, int intent)
        => UsesLookupTable(deviceToConnection, intent) || (ChannelCount == 1 && TryFindTag("kTRC", out _, out _)) || (ChannelCount == 3 && TryFindTag("rTRC", out _, out _) && TryFindTag("rXYZ", out _, out _));

    /// <summary>
    /// The media white point of the ICC-absolute colorimetric intent (ICC.1:2022 section 6.3.2): the <c>wtpt</c> tag, or
    /// D50 without the tag and for display profiles (assumed to be viewed fully adapted).
    /// </summary>
    public decimal[] GetMediaWhitePoint()
    {
        if (Encoding.ASCII.GetString(_data, 12, 4) == "mntr" || !TryFindTag("wtpt", out var offset, out _))
            return ReferenceIccMath.D50;

        return
        [
            BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(offset + 8)) / 65536m,
            BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(offset + 12)) / 65536m,
            BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(offset + 16)) / 65536m,
        ];
    }

    /// <summary>Converts normalized device values to CIELAB.</summary>
    public decimal[] DeviceToLab(decimal[] device, int intent) => ReferenceIccMath.XyzToLab(ToConnectionSpace(device, intent));

    /// <summary>Converts CIELAB to normalized device values.</summary>
    public decimal[] LabToDevice(decimal[] lab, int intent) => FromConnectionSpace(ReferenceIccMath.LabToXyz(lab), intent);

    /// <summary>Converts normalized device values to CIEXYZ relative to D50.</summary>
    public decimal[] ToConnectionSpace(decimal[] device, int intent)
    {
        if (FindLookupTable(deviceToConnection: true, intent) is { } tag)
            return Decode(ReferenceIccLut.Evaluate(_data.AsSpan(tag.Offset, tag.Length), device, deviceToConnection: true, ConnectionSpace == "XYZ "), tag.Type);

        if (ChannelCount == 1)
        {
            // Annex F.2: the curve output scales the media white (CIEXYZ) or is L* / 100 (CIELAB)
            var t = ReadCurve("kTRC").Evaluate(device[0]);
            return ConnectionSpace == "Lab "
                ? ReferenceIccMath.LabToXyz([100 * t, 0, 0])
                : [t * ReferenceIccMath.D50[0], t * ReferenceIccMath.D50[1], t * ReferenceIccMath.D50[2]];
        }

        if (ChannelCount == 3)
        {
            // Annex F.3: linear = curve(device), connection = colorant matrix * linear
            decimal[] linear = [ReadCurve("rTRC").Evaluate(device[0]), ReadCurve("gTRC").Evaluate(device[1]), ReadCurve("bTRC").Evaluate(device[2])];
            return ReferenceIccMath.Multiply(ReadColorants(), linear);
        }

        throw new NotSupportedException("The reference has no model for this profile.");
    }

    /// <summary>Converts CIEXYZ relative to D50 to normalized device values in [0, 1].</summary>
    public decimal[] FromConnectionSpace(decimal[] xyz, int intent)
    {
        if (FindLookupTable(deviceToConnection: false, intent) is { } tag)
            return ReferenceIccLut.Evaluate(_data.AsSpan(tag.Offset, tag.Length), Encode(xyz, tag.Type), deviceToConnection: false, ConnectionSpace == "XYZ ");

        if (ChannelCount == 1)
        {
            var t = ConnectionSpace == "Lab " ? ReferenceIccMath.XyzToLab(xyz)[0] / 100 : xyz[1];
            return [ReadCurve("kTRC").Invert(t)];
        }

        if (ChannelCount == 3)
        {
            var linear = ReferenceIccMath.Multiply(ReferenceIccMath.Invert(ReadColorants()), xyz);
            return [ReadCurve("rTRC").Invert(linear[0]), ReadCurve("gTRC").Invert(linear[1]), ReadCurve("bTRC").Invert(linear[2])];
        }

        throw new NotSupportedException("The reference has no model for this profile.");
    }

    /// <summary>
    /// ICC.1:2022 section 8.10.2: the tag of the intent (0 perceptual, 1 media-relative and 3 ICC-absolute colorimetric,
    /// 2 saturation), else the tag of the perceptual intent, else no lookup table (matrix and tone curves).
    /// </summary>
    private (int Offset, int Length, string Type)? FindLookupTable(bool deviceToConnection, int intent)
    {
        var index = intent switch { 0 => 0, 2 => 2, _ => 1 };
        foreach (var candidate in new[] { index, 0 })
        {
            var signature = string.Create(CultureInfo.InvariantCulture, $"{(deviceToConnection ? "A2B" : "B2A")}{candidate}");
            if (TryFindTag(signature, out var offset, out var length))
                return (offset, length, Encoding.ASCII.GetString(_data, offset, 4));
        }

        return null;
    }

    /// <summary>
    /// Normalized lookup table output to CIEXYZ (ICC.1:2022 section 6.3.4). CIEXYZ: 16-bit u1Fixed15, so 1.0 is
    /// 32768 / 65535. CIELAB: L* = 100 n and a* = 255 n - 128, except in a lut16Type where the 16-bit value v = 65535 n
    /// gives L* = 100 v / 65280 and a* = v / 256 - 128 (ICC.1:2001-04 annex A).
    /// </summary>
    private decimal[] Decode(decimal[] encoded, string type)
    {
        if (ConnectionSpace == "XYZ ")
        {
            if (type == "mft1")
                throw new NotSupportedException("A lut8Type has no CIEXYZ encoding.");

            return [encoded[0] * 65535 / 32768, encoded[1] * 65535 / 32768, encoded[2] * 65535 / 32768];
        }

        return type == "mft2"
            ? ReferenceIccMath.LabToXyz([encoded[0] * 65535 * 100 / 65280, (encoded[1] * 65535 / 256) - 128, (encoded[2] * 65535 / 256) - 128])
            : ReferenceIccMath.LabToXyz([encoded[0] * 100, (encoded[1] * 255) - 128, (encoded[2] * 255) - 128]);
    }

    private decimal[] Encode(decimal[] xyz, string type)
    {
        if (ConnectionSpace == "XYZ ")
        {
            if (type == "mft1")
                throw new NotSupportedException("A lut8Type has no CIEXYZ encoding.");

            return [xyz[0] * 32768 / 65535, xyz[1] * 32768 / 65535, xyz[2] * 32768 / 65535];
        }

        var lab = ReferenceIccMath.XyzToLab(xyz);
        return type == "mft2"
            ? [lab[0] * 65280 / 100 / 65535, (lab[1] + 128) * 256 / 65535, (lab[2] + 128) * 256 / 65535]
            : [lab[0] / 100, (lab[1] + 128) / 255, (lab[2] + 128) / 255];
    }

    private decimal[] ReadColorants()
    {
        var matrix = new decimal[9];
        string[] signatures = ["rXYZ", "gXYZ", "bXYZ"];
        for (var column = 0; column < 3; column++)
        {
            var tag = GetTag(signatures[column]);
            for (var row = 0; row < 3; row++)
            {
                matrix[(3 * row) + column] = BinaryPrimitives.ReadInt32BigEndian(tag[(8 + (4 * row))..]) / 65536m;
            }
        }

        return matrix;
    }

    private ReferenceIccCurve ReadCurve(string signature) => ReferenceIccCurve.Parse(GetTag(signature), out _);

    private ReadOnlySpan<byte> GetTag(string signature)
        => TryFindTag(signature, out var offset, out var length) ? _data.AsSpan(offset, length) : throw new NotSupportedException($"The profile has no '{signature}' tag.");

    private bool TryFindTag(string signature, out int offset, out int length)
    {
        var count = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(128));
        for (var i = 0; i < count; i++)
        {
            var entry = _data.AsSpan(132 + (12 * i), 12);
            if (Encoding.ASCII.GetString(entry[..4]) == signature)
            {
                offset = BinaryPrimitives.ReadInt32BigEndian(entry[4..]);
                length = BinaryPrimitives.ReadInt32BigEndian(entry[8..]);
                return true;
            }
        }

        offset = 0;
        length = 0;
        return false;
    }
}
