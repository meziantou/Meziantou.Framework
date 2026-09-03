using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

internal sealed class Mp3TagReader : IMediaTagReader
{
    private const int MaxResyncBytes = 64 * 1024;

    /// <summary>The largest frame this reader inspects for a VBR header.</summary>
    private const int MaxFirstFrameSize = 4096;

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
        catch (Exception ex) when (MediaTagErrors.TryMap(ex, out var error))
        {
            return MediaTagResult<MediaTagInfo>.Failure(error, ex.Message);
        }
    }

    /// <summary>
    /// Computes the duration from the first audio frame.
    /// </summary>
    /// <remarks>
    /// The Xing/Info/VBRI header a VBR encoder writes into the first frame carries the exact frame count, and a
    /// constant bit rate stream can be derived from the size of the audio. Walking every frame instead would
    /// make reading the tags of a file cost time proportional to its length.
    /// </remarks>
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

            if (!TryFindFirstFrame(stream, audioStart, audioEnd, out var frameOffset, out var frame))
                return null;

            if (TryReadVbrFrameCount(stream, frameOffset, frame, audioEnd, out var frameCount))
                return TimeSpan.FromSeconds((double)frameCount * frame.SamplesPerFrame / frame.SampleRate);

            var audioLength = audioEnd - frameOffset;
            if (frame.BitRate > 0 && audioLength > 0)
                return TimeSpan.FromSeconds(audioLength * 8d / frame.BitRate);

            return null;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static bool TryFindFirstFrame(Stream stream, long audioStart, long audioEnd, out long frameOffset, out Mp3FrameHeader frame)
    {
        frameOffset = 0;
        frame = default;

        var currentOffset = audioStart;
        var resyncBytes = 0;
        Span<byte> headerBuffer = stackalloc byte[4];

        while (currentOffset + 4 <= audioEnd && resyncBytes < MaxResyncBytes)
        {
            stream.Position = currentOffset;
            if (stream.ReadAtLeast(headerBuffer, 4, throwOnEndOfStream: false) < 4)
                return false;

            if (TryParseFrameHeader(headerBuffer, out var parsed) && currentOffset + parsed.FrameLength <= audioEnd)
            {
                frameOffset = currentOffset;
                frame = parsed;
                return true;
            }

            currentOffset++;
            resyncBytes++;
        }

        return false;
    }

    /// <summary>
    /// Reads the frame count from the Xing, Info or VBRI header stored inside the first frame.
    /// </summary>
    private static bool TryReadVbrFrameCount(Stream stream, long frameOffset, in Mp3FrameHeader frame, long audioEnd, out uint frameCount)
    {
        frameCount = 0;

        var frameLength = Math.Min(frame.FrameLength, MaxFirstFrameSize);
        if (frameLength < 4 || frameOffset + frameLength > audioEnd)
            return false;

        Span<byte> frameData = stackalloc byte[MaxFirstFrameSize];
        frameData = frameData[..frameLength];

        stream.Position = frameOffset;
        if (stream.ReadAtLeast(frameData, frameLength, throwOnEndOfStream: false) < frameLength)
            return false;

        // Xing (VBR) and Info (CBR) sit after the frame header and the side information.
        var xingOffset = 4 + GetSideInfoSize(frame);
        if (xingOffset + 12 <= frameData.Length)
        {
            var magic = frameData.Slice(xingOffset, 4);
            if (magic.SequenceEqual("Xing"u8) || magic.SequenceEqual("Info"u8))
            {
                var flags = BinaryPrimitives.ReadUInt32BigEndian(frameData.Slice(xingOffset + 4, 4));
                if ((flags & 0x0000_0001) != 0)
                {
                    frameCount = BinaryPrimitives.ReadUInt32BigEndian(frameData.Slice(xingOffset + 8, 4));
                    return frameCount > 0;
                }
            }
        }

        // VBRI is written by the Fraunhofer encoder at a fixed offset instead.
        const int VbriOffset = 4 + 32;
        if (VbriOffset + 18 <= frameData.Length && frameData.Slice(VbriOffset, 4).SequenceEqual("VBRI"u8))
        {
            frameCount = BinaryPrimitives.ReadUInt32BigEndian(frameData.Slice(VbriOffset + 14, 4));
            return frameCount > 0;
        }

        return false;
    }

    private static int GetSideInfoSize(in Mp3FrameHeader frame)
    {
        if (frame.IsMpeg1)
            return frame.IsMono ? 17 : 32;

        return frame.IsMono ? 9 : 17;
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

    private static bool TryParseFrameHeader(ReadOnlySpan<byte> header, out Mp3FrameHeader frame)
    {
        frame = default;
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
        var channelMode = (int)((headerValue >> 6) & 0b11);

        if (versionBits is 0b01 || layerBits is 0b00 || bitrateIndex is 0b0000 or 0b1111 || sampleRateIndex is 0b11)
            return false;

        var sampleRate = GetSampleRate(versionBits, sampleRateIndex);
        if (sampleRate <= 0)
            return false;

        var layer = 4 - layerBits;
        var isMpeg1 = versionBits == 0b11;
        var bitrate = GetBitrate(layer, isMpeg1, bitrateIndex);
        if (bitrate <= 0)
            return false;

        int samplesPerFrame;
        int frameLength;
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

        frame = new Mp3FrameHeader
        {
            FrameLength = frameLength,
            SamplesPerFrame = samplesPerFrame,
            SampleRate = sampleRate,
            BitRate = bitrate,
            IsMpeg1 = isMpeg1,
            IsMono = channelMode == 0b11,
        };
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

    private readonly record struct Mp3FrameHeader
    {
        public int FrameLength { get; init; }
        public int SamplesPerFrame { get; init; }
        public int SampleRate { get; init; }
        public int BitRate { get; init; }
        public bool IsMpeg1 { get; init; }
        public bool IsMono { get; init; }
    }
}
