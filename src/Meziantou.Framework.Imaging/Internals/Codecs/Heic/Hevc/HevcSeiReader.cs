namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the supplemental enhancement information of a NAL unit (Rec. ITU-T H.265, clauses 7.3.2.4 "Supplemental
/// enhancement information RBSP syntax", 7.3.5 "Supplemental enhancement information message syntax" and D.2.1 "General
/// SEI message syntax").
/// </summary>
internal static class HevcSeiReader
{
    /// <summary>Reads the messages of the payload of a SEI NAL unit into <paramref name="messages"/>.</summary>
    /// <param name="rbsp">The payload of the NAL unit, without emulation prevention bytes.</param>
    /// <param name="isPrefix">Whether the NAL unit is a prefix SEI NAL unit; otherwise it is a suffix SEI NAL unit.</param>
    /// <param name="componentCount">The number of colour components of the pictures of the active sequence, or 0 when no sequence is active.</param>
    /// <param name="messages">The messages read so far.</param>
    /// <exception cref="InvalidImageContentException">A message goes beyond the end of the payload, or the payload does not end with its trailing bits.</exception>
    public static void Read(ReadOnlySpan<byte> rbsp, bool isPrefix, int componentCount, HevcSeiMessages messages)
    {
        var position = 0;
        do
        {
            var payloadType = ReadFfCodedNumber(rbsp, ref position);
            var payloadSize = ReadFfCodedNumber(rbsp, ref position);
            if (payloadSize > rbsp.Length - position)
                throw HevcErrors.Truncated();

            ReadPayload(rbsp.Slice(position, payloadSize), payloadType, isPrefix, componentCount, messages);
            position += payloadSize;
        }
        while (MoreRbspData(rbsp, position));

        // rbsp_trailing_bits( ): the messages end on a byte
        if (position != rbsp.Length - 1 || rbsp[position] is not 0x80)
            throw HevcErrors.Invalid("a supplemental enhancement information payload does not end with its trailing bits.");
    }

    private static int ReadFfCodedNumber(ReadOnlySpan<byte> rbsp, ref int position)
    {
        var value = 0;
        while (true)
        {
            if (position >= rbsp.Length)
                throw HevcErrors.Truncated();

            var next = rbsp[position++];
            value += next;
            if (next is not 0xFF)
                return value;

            // A value larger than the payload cannot be the type of a known message or a size
            if (value > rbsp.Length + 255)
                throw HevcErrors.Truncated();
        }
    }

    private static bool MoreRbspData(ReadOnlySpan<byte> rbsp, int position)
    {
        var last = rbsp.LastIndexOfAnyExcept((byte)0);
        return position < last || (position == last && rbsp[position] is not 0x80);
    }

    private static void ReadPayload(ReadOnlySpan<byte> payload, int payloadType, bool isPrefix, int componentCount, HevcSeiMessages messages)
    {
        var reader = new HevcBitReader(payload);
        switch (payloadType)
        {
            case HevcConstants.SeiDecodedPictureHash when !isPrefix && componentCount > 0:
                var hashType = (int)reader.ReadBits(8);
                if (hashType > HevcDecodedPictureHash.Checksum)
                {
                    // Clause D.3.20: a decoder ignores the messages that have a reserved value of hash_type
                    break;
                }

                var md5 = hashType is HevcDecodedPictureHash.Md5 ? new byte[componentCount * 16] : [];
                var values = new uint[componentCount];
                for (var cIdx = 0; cIdx < componentCount; cIdx++)
                {
                    if (hashType is HevcDecodedPictureHash.Md5)
                    {
                        for (var i = 0; i < 16; i++)
                        {
                            md5[(cIdx * 16) + i] = (byte)reader.ReadBits(8);
                        }
                    }
                    else
                    {
                        values[cIdx] = reader.ReadBits(hashType is HevcDecodedPictureHash.Crc ? 16 : 32);
                    }
                }

                messages.DecodedPictureHash = new HevcDecodedPictureHash(hashType, componentCount, md5, values);
                break;

            case HevcConstants.SeiMasteringDisplayColourVolume when isPrefix:
                Span<ushort> primaries = stackalloc ushort[6];
                for (var i = 0; i < primaries.Length; i++)
                {
                    primaries[i] = (ushort)reader.ReadBits(16);
                }

                var whitePointX = (ushort)reader.ReadBits(16);
                var whitePointY = (ushort)reader.ReadBits(16);
                var maxDisplayMasteringLuminance = reader.ReadBits(32);
                var minDisplayMasteringLuminance = reader.ReadBits(32);
                messages.MasteringDisplayColourVolume = new HevcMasteringDisplayColourVolume(primaries[0], primaries[1], primaries[2], primaries[3], primaries[4], primaries[5], whitePointX, whitePointY, maxDisplayMasteringLuminance, minDisplayMasteringLuminance);
                break;

            case HevcConstants.SeiContentLightLevelInfo when isPrefix:
                var maxContentLightLevel = (ushort)reader.ReadBits(16);
                var maxPicAverageLightLevel = (ushort)reader.ReadBits(16);
                messages.ContentLightLevel = new HevcContentLightLevel(maxContentLightLevel, maxPicAverageLightLevel);
                break;

            case HevcConstants.SeiAlphaChannelInfo when isPrefix:
                if (reader.ReadFlag())
                {
                    messages.AlphaChannelInfo = new HevcAlphaChannelInfo(CancelFlag: true, 0, 8, 0, 0, IncrFlag: false, ClipFlag: false, ClipTypeFlag: false);
                    break;
                }

                var useIdc = (int)reader.ReadBits(3);
                var bitDepth = (int)reader.ReadBits(3) + 8;
                var transparentValue = (int)reader.ReadBits(bitDepth + 1);
                var opaqueValue = (int)reader.ReadBits(bitDepth + 1);
                var incrFlag = reader.ReadFlag();
                var clipFlag = reader.ReadFlag();
                var clipTypeFlag = clipFlag && reader.ReadFlag();
                messages.AlphaChannelInfo = new HevcAlphaChannelInfo(CancelFlag: false, useIdc, bitDepth, transparentValue, opaqueValue, incrFlag, clipFlag, clipTypeFlag);
                break;

            default:
                // The other messages do not take part in the decoding: they are skipped by their size
                break;
        }
    }
}
