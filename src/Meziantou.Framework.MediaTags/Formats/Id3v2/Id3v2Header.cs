using System.Runtime.InteropServices;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

[StructLayout(LayoutKind.Auto)]
internal readonly struct Id3v2Header
{
    public byte MajorVersion { get; init; }
    public byte MinorVersion { get; init; }
    public bool Unsynchronisation { get; init; }
    public bool ExtendedHeader { get; init; }
    public bool ExperimentalIndicator { get; init; }
    public bool FooterPresent { get; init; }
    public int TagSize { get; init; }

    public static bool TryParse(ReadOnlySpan<byte> data, out Id3v2Header header)
    {
        header = default;

        if (data.Length < 10)
            return false;

        // "ID3"
        if (data[0] != 'I' || data[1] != 'D' || data[2] != '3')
            return false;

        var majorVersion = data[3];
        var minorVersion = data[4];

        // Only support v2.2, v2.3, v2.4
        if (majorVersion is not (2 or 3 or 4))
            return false;

        var flags = data[5];
        var sizeBytes = data.Slice(6, 4);

        // Validate synchsafe: each byte must have bit 7 clear
        if ((sizeBytes[0] | sizeBytes[1] | sizeBytes[2] | sizeBytes[3]) > 0x7F)
            return false;

        var size = SynchsafeInteger.Decode(sizeBytes);

        header = new Id3v2Header
        {
            MajorVersion = majorVersion,
            MinorVersion = minorVersion,
            Unsynchronisation = (flags & 0x80) != 0,
            ExtendedHeader = (flags & 0x40) != 0,
            ExperimentalIndicator = (flags & 0x20) != 0,
            FooterPresent = majorVersion == 4 && (flags & 0x10) != 0,
            TagSize = size,
        };
        return true;
    }
}
