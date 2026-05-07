using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

internal static class Id3v2TagLocator
{
    public static bool TryGetAudioDataOffsets(Stream stream, long tagStartOffset, out long primaryOffset, out long secondaryOffset)
    {
        primaryOffset = -1;
        secondaryOffset = -1;

        if (!stream.CanSeek || tagStartOffset < 0 || stream.Length < tagStartOffset + 10)
            return false;

        var originalPosition = stream.Position;
        try
        {
            stream.Position = tagStartOffset;
            Span<byte> headerBytes = stackalloc byte[10];
            if (stream.ReadAtLeast(headerBytes, headerBytes.Length, throwOnEndOfStream: false) < headerBytes.Length)
                return false;

            if (!Id3v2Header.TryParse(headerBytes, out var header))
                return false;

            var contentEndOffset = tagStartOffset + 10L + header.TagSize;
            if (contentEndOffset > stream.Length)
                return false;

            if (header.FooterPresent)
            {
                primaryOffset = contentEndOffset + 10;
                secondaryOffset = contentEndOffset;
            }
            else
            {
                primaryOffset = contentEndOffset;
                if (HasMatchingFooterAt(stream, contentEndOffset, header))
                {
                    secondaryOffset = contentEndOffset + 10;
                }
            }

            return true;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static bool HasMatchingFooterAt(Stream stream, long offset, Id3v2Header header)
    {
        if (stream.Length < offset + 10)
            return false;

        stream.Position = offset;
        Span<byte> footerBytes = stackalloc byte[10];
        if (stream.ReadAtLeast(footerBytes, footerBytes.Length, throwOnEndOfStream: false) < footerBytes.Length)
            return false;

        if (footerBytes is not [(byte)'3', (byte)'D', (byte)'I', ..])
            return false;

        if (footerBytes[3] != header.MajorVersion || footerBytes[4] != header.MinorVersion)
            return false;

        if (footerBytes[5] != GetFlags(header))
            return false;

        var footerTagSize = SynchsafeInteger.Decode(footerBytes[6..]);
        return footerTagSize == header.TagSize;
    }

    private static byte GetFlags(Id3v2Header header)
    {
        byte flags = 0;
        if (header.Unsynchronisation)
            flags |= 0x80;

        if (header.ExtendedHeader)
            flags |= 0x40;

        if (header.ExperimentalIndicator)
            flags |= 0x20;

        if (header.FooterPresent)
            flags |= 0x10;

        return flags;
    }
}
