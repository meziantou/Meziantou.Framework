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

    /// <summary>Converts normalized device values to CIEXYZ relative to D50.</summary>
    public decimal[] ToConnectionSpace(decimal[] device)
    {
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
    public decimal[] FromConnectionSpace(decimal[] xyz)
    {
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
    {
        var count = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(128));
        for (var i = 0; i < count; i++)
        {
            var entry = _data.AsSpan(132 + (12 * i), 12);
            if (Encoding.ASCII.GetString(entry[..4]) == signature)
                return _data.AsSpan(BinaryPrimitives.ReadInt32BigEndian(entry[4..]), BinaryPrimitives.ReadInt32BigEndian(entry[8..]));
        }

        throw new NotSupportedException($"The profile has no '{signature}' tag.");
    }
}
