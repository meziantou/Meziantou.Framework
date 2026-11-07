using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The ICC profiles a JP2 file may hold (T.800, I.5.3.3: the restricted ICC method): a monochrome profile, or a
/// three-component matrix-based profile, of the input or display class, whose colors are computed with curves and a
/// matrix and no lookup table.
/// </summary>
internal static class Jpeg2000RestrictedIcc
{
    private const uint ColorSpaceGray = 0x47524159;
    private const uint ColorSpaceRgb = 0x52474220;
    private const uint ConnectionSpaceXyz = 0x58595A20;
    private const uint ClassInput = 0x73636E72;
    private const uint ClassDisplay = 0x6D6E7472;
    private const uint TagGrayCurve = 0x6B545243;
    private const uint TagRedColorant = 0x7258595A;
    private const uint TagGreenColorant = 0x6758595A;
    private const uint TagBlueColorant = 0x6258595A;
    private const uint TagRedCurve = 0x72545243;
    private const uint TagGreenCurve = 0x67545243;
    private const uint TagBlueCurve = 0x62545243;

    /// <summary>Determines whether a profile, already validated, is one a JP2 file may hold.</summary>
    public static bool IsRestricted(ReadOnlySpan<byte> profile)
    {
        // The header (ICC.1:2022, 7.2): the class at the offset 12, the color space at 16, the connection space at 20;
        // then the number of tags and, for each tag, its signature, its offset and its size
        if (profile.Length < 132)
            return false;

        var deviceClass = BinaryPrimitives.ReadUInt32BigEndian(profile[12..]);
        if (deviceClass is not (ClassInput or ClassDisplay) || BinaryPrimitives.ReadUInt32BigEndian(profile[20..]) != ConnectionSpaceXyz)
            return false;

        var tagCount = BinaryPrimitives.ReadUInt32BigEndian(profile[128..]);
        if (tagCount > (uint)((profile.Length - 132) / 12))
            return false;

        var tags = profile.Slice(132, (int)tagCount * 12);
        return BinaryPrimitives.ReadUInt32BigEndian(profile[16..]) switch
        {
            ColorSpaceGray => HasTag(tags, TagGrayCurve),
            ColorSpaceRgb => HasTag(tags, TagRedColorant) && HasTag(tags, TagGreenColorant) && HasTag(tags, TagBlueColorant) && HasTag(tags, TagRedCurve) && HasTag(tags, TagGreenCurve) && HasTag(tags, TagBlueCurve),
            _ => false,
        };
    }

    private static bool HasTag(ReadOnlySpan<byte> tags, uint signature)
    {
        for (var offset = 0; offset < tags.Length; offset += 12)
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(tags[offset..]) == signature)
                return true;
        }

        return false;
    }
}
