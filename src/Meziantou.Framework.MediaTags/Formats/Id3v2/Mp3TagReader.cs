namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

internal sealed class Mp3TagReader : IMediaTagReader
{
    private const int MaxResyncBytes = 64 * 1024;

    private static readonly int[] Mpeg1Layer1BitRates =
    [
        32_000, 64_000, 96_000, 128_000, 160_000, 192_000, 224_000, 256_000, 288_000, 320_000, 352_000, 384_000, 416_000, 448_000,
    ];

    private static readonly int[] Mpeg1Layer2BitRates =
    [
        32_000, 48_000, 56_000, 64_000, 80_000, 96_000, 112_000, 128_000, 160_000, 192_000, 224_000, 256_000, 320_000, 384_000,
    ];

    private static readonly int[] Mpeg1Layer3BitRates =
    [
        32_000, 40_000, 48_000, 56_000, 64_000, 80_000, 96_000, 112_000, 128_000, 160_000, 192_000, 224_000, 256_000, 320_000,
    ];

    private static readonly int[] Mpeg2Layer1BitRates =
    [
        32_000, 48_000, 56_000, 64_000, 80_000, 96_000, 112_000, 128_000, 144_000, 160_000, 176_000, 192_000, 224_000, 256_000,
    ];

    private static readonly int[] Mpeg2Layer2Or3BitRates =
    [
        8_000, 16_000, 24_000, 32_000, 40_000, 48_000, 56_000, 64_000, 80_000, 96_000, 112_000, 128_000, 144_000, 160_000,
    ];

    public MediaTagResult<MediaTagInfo> ReadTags(Stream stream)
    {
        try
        {
            var tags = new MediaTagInfo();

            // Try ID3v2 first (at start of file)
            stream.Position = 0;
            Id3v2Reader.TryReadTag(stream, tags);

            // Then try ID3v1 (at end of file) — ID3v2 values take priority (already set via ??=)
            Id3v1.Id3v1Reader.TryReadTag(stream, tags);
            tags.Duration ??= TryReadDuration(stream);

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex)
        {
            return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, ex.Message);
        }
    }

    private static TimeSpan? TryReadDuration(Stream stream)
    {
        if (!stream.CanSeek || !stream.CanRead)
            return null;

        var originalPosition = stream.Position;
        try
        {
            var audioStart = GetAudioStartOffset(stream);
            var audioEnd = GetAudioEndOffset(stream);
            if (audioEnd - audioStart < 4)
                return null;

            var currentOffset = audioStart;
            var totalSeconds = 0d;
            var hasFrame = false;
            var resyncBytes = 0;

            Span<byte> headerBuffer = stackalloc byte[4];
            while (currentOffset + 4 <= audioEnd)
            {
                stream.Position = currentOffset;
                if (stream.ReadAtLeast(headerBuffer, 4, throwOnEndOfStream: false) < 4)
                    break;

                if (TryParseFrameHeader(headerBuffer, out var frameLength, out var samplesPerFrame, out var sampleRate) && currentOffset + frameLength <= audioEnd)
                {
                    totalSeconds += (double)samplesPerFrame / sampleRate;
                    currentOffset += frameLength;
                    hasFrame = true;
                    resyncBytes = 0;
                    continue;
                }

                currentOffset++;
                resyncBytes++;

                if (hasFrame && resyncBytes >= MaxResyncBytes)
                    break;
            }

            return hasFrame ? TimeSpan.FromSeconds(totalSeconds) : null;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static long GetAudioStartOffset(Stream stream)
    {
        stream.Position = 0;
        return Id3v2Reader.GetTagSize(stream);
    }

    private static long GetAudioEndOffset(Stream stream)
    {
        var audioEnd = stream.Length;
        if (audioEnd < 128)
            return audioEnd;

        stream.Position = audioEnd - 128;
        Span<byte> id3v1Header = stackalloc byte[3];
        if (stream.ReadAtLeast(id3v1Header, id3v1Header.Length, throwOnEndOfStream: false) < id3v1Header.Length)
            return audioEnd;

        return id3v1Header is [(byte)'T', (byte)'A', (byte)'G'] ? audioEnd - 128 : audioEnd;
    }

    private static bool TryParseFrameHeader(ReadOnlySpan<byte> header, out int frameLength, out int samplesPerFrame, out int sampleRate)
    {
        frameLength = 0;
        samplesPerFrame = 0;
        sampleRate = 0;
        if (header.Length < 4)
            return false;

        var headerValue = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];

        // Sync word: first 11 bits must be set.
        if ((headerValue & 0xFFE00000) != 0xFFE00000)
            return false;

        var versionBits = (int)((headerValue >> 19) & 0b11);
        var layerBits = (int)((headerValue >> 17) & 0b11);
        var bitrateIndex = (int)((headerValue >> 12) & 0b1111);
        var sampleRateIndex = (int)((headerValue >> 10) & 0b11);
        var padding = (int)((headerValue >> 9) & 0b1);

        if (versionBits is 0b01 || layerBits is 0b00 || bitrateIndex is 0b0000 or 0b1111 || sampleRateIndex is 0b11)
            return false;

        sampleRate = GetSampleRate(versionBits, sampleRateIndex);
        if (sampleRate <= 0)
            return false;

        var layer = 4 - layerBits;
        var isMpeg1 = versionBits == 0b11;
        var bitrate = GetBitrate(layer, isMpeg1, bitrateIndex);
        if (bitrate <= 0)
            return false;

        switch (layer)
        {
            case 1:
                samplesPerFrame = 384;
                frameLength = (((12 * bitrate) / sampleRate) + padding) * 4;
                break;

            case 2:
                samplesPerFrame = 1152;
                frameLength = ((144 * bitrate) / sampleRate) + padding;
                break;

            case 3:
                samplesPerFrame = isMpeg1 ? 1152 : 576;
                frameLength = (((isMpeg1 ? 144 : 72) * bitrate) / sampleRate) + padding;
                break;

            default:
                return false;
        }

        if (frameLength <= 4)
            return false;

        return true;
    }

    private static int GetSampleRate(int versionBits, int sampleRateIndex)
    {
        var sampleRate = sampleRateIndex switch
        {
            0 => 44_100,
            1 => 48_000,
            2 => 32_000,
            _ => 0,
        };

        return versionBits switch
        {
            0b11 => sampleRate,      // MPEG 1
            0b10 => sampleRate / 2,  // MPEG 2
            0b00 => sampleRate / 4,  // MPEG 2.5
            _ => 0,
        };
    }

    private static int GetBitrate(int layer, bool isMpeg1, int bitrateIndex)
    {
        if (bitrateIndex < 1 || bitrateIndex > 14)
            return 0;

        var tableIndex = bitrateIndex - 1;
        return (isMpeg1, layer) switch
        {
            (true, 1) => Mpeg1Layer1BitRates[tableIndex],
            (true, 2) => Mpeg1Layer2BitRates[tableIndex],
            (true, 3) => Mpeg1Layer3BitRates[tableIndex],
            (false, 1) => Mpeg2Layer1BitRates[tableIndex],
            (false, 2) or (false, 3) => Mpeg2Layer2Or3BitRates[tableIndex],
            _ => 0,
        };
    }

}
