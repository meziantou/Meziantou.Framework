namespace Meziantou.Framework.MediaTags.Formats.Flac;

internal sealed class FlacReader : IMediaTagReader
{
    public MediaTagResult<MediaTagInfo> ReadTags(Stream stream)
    {
        try
        {
            if (!TrySeekAfterFlacMagic(stream))
            {
                stream.Position = 0;
                Span<byte> magic = stackalloc byte[4];
                if (stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) < magic.Length)
                    return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, "File too small for FLAC.");

                return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.UnsupportedFormat, "Not a FLAC file.");
            }

            var tags = new MediaTagInfo();
            Span<byte> blockHeader = stackalloc byte[4];

            // Parse metadata blocks
            while (true)
            {
                if (stream.ReadAtLeast(blockHeader, 4, throwOnEndOfStream: false) < 4)
                    break;

                var isLast = (blockHeader[0] & 0x80) != 0;
                var blockType = (byte)(blockHeader[0] & 0x7F);
                var blockSize = (blockHeader[1] << 16) | (blockHeader[2] << 8) | blockHeader[3];

                if (blockType == FlacMetadataBlock.StreamInfo)
                {
                    // STREAMINFO is always 34 bytes
                    var blockData = new byte[blockSize];
                    if (stream.ReadAtLeast(blockData, blockSize, throwOnEndOfStream: false) < blockSize)
                        break;

                    if (blockSize >= 18)
                    {
                        // Sample rate: bits 80-99 (20 bits starting at byte 10)
                        var sampleRate = (blockData[10] << 12) | (blockData[11] << 4) | ((blockData[12] & 0xF0) >> 4);
                        // Total samples: bits 108-143 (36 bits starting at nibble in byte 13)
                        var totalSamples = ((long)(blockData[13] & 0x0F) << 32)
                            | ((long)blockData[14] << 24)
                            | ((long)blockData[15] << 16)
                            | ((long)blockData[16] << 8)
                            | blockData[17];

                        if (sampleRate > 0 && totalSamples > 0)
                        {
                            tags.Duration = TimeSpan.FromSeconds((double)totalSamples / sampleRate);
                        }
                    }
                }
                else if (blockType == FlacMetadataBlock.VorbisCommentType)
                {
                    var blockData = new byte[blockSize];
                    if (stream.ReadAtLeast(blockData, blockSize, throwOnEndOfStream: false) < blockSize)
                        break;

                    VorbisComment.VorbisCommentReader.TryParse(blockData, tags);
                }
                else if (blockType == FlacMetadataBlock.Picture)
                {
                    var blockData = new byte[blockSize];
                    if (stream.ReadAtLeast(blockData, blockSize, throwOnEndOfStream: false) < blockSize)
                        break;

                    FlacPictureBlock.TryParse(blockData, tags);
                }
                else
                {
                    // Skip this block
                    stream.Seek(blockSize, SeekOrigin.Current);
                }

                if (isLast)
                    break;
            }

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex)
        {
            return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, ex.Message);
        }
    }

    private static bool TrySeekAfterFlacMagic(Stream stream)
    {
        if (!stream.CanSeek)
            return false;

        stream.Position = 0;
        if (TryReadFlacMagic(stream))
            return true;

        stream.Position = 0;
        Span<byte> id3Header = stackalloc byte[3];
        if (stream.ReadAtLeast(id3Header, id3Header.Length, throwOnEndOfStream: false) < id3Header.Length)
            return false;

        if (id3Header is not [(byte)'I', (byte)'D', (byte)'3'])
            return false;

        stream.Position = 0;
        var id3v2TagSize = Id3v2.Id3v2Reader.GetTagSize(stream);
        if (id3v2TagSize <= 0 || stream.Length < id3v2TagSize + 4)
            return false;

        stream.Position = id3v2TagSize;
        return TryReadFlacMagic(stream);
    }

    private static bool TryReadFlacMagic(Stream stream)
    {
        Span<byte> magic = stackalloc byte[4];
        return stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) == magic.Length
            && magic is [(byte)'f', (byte)'L', (byte)'a', (byte)'C'];
    }
}
