using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Ape;

/// <summary>
/// Reads the signature and the duration of the audio formats that use APE tags.
/// </summary>
internal static class ApeAudioHeaders
{
    /// <summary>The bytes read from the start of the file. The Musepack 8 stream header can follow a few other packets.</summary>
    private const int HeaderBufferSize = 4096;

    private static readonly int[] WavPackSampleRates = [6000, 8000, 9600, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000, 64000, 88200, 96000, 192000];

    private static readonly int[] MusepackSampleRates = [44100, 48000, 37800, 32000];

    /// <summary>The length of the signatures. A tag cannot start before it, so a write always keeps the signature.</summary>
    public const int SignatureLength = 4;

    /// <summary>Gets whether the file starts with the signature of the format, and is detected as that format.</summary>
    /// <remarks>Detection checks other signatures first, such as the "ftyp" of MP4 at offset 4: a write must not produce a file detected as another format.</remarks>
    public static bool HasSignature(ReadOnlySpan<byte> header, MediaFormat format)
        => FormatDetector.DetectFromHeader(header[..Math.Min(header.Length, FormatDetector.MinHeaderSize)]) == format;

    /// <summary>Reads the start of the file, from offset 0.</summary>
    public static byte[] ReadHeader(Stream stream)
    {
        stream.Position = 0;
        var buffer = new byte[(int)Math.Min(HeaderBufferSize, stream.Length)];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return read == buffer.Length ? buffer : buffer[..read];
    }

    /// <summary>Computes the duration from the stream header, or returns <see langword="null"/> when it is unknown.</summary>
    public static TimeSpan? GetDuration(ReadOnlySpan<byte> header, MediaFormat format) => format switch
    {
        MediaFormat.MonkeysAudio => GetMonkeysAudioDuration(header),
        MediaFormat.WavPack => GetWavPackDuration(header),
        MediaFormat.Musepack when header.StartsWith("MPCK"u8) => GetMusepack8Duration(header),
        MediaFormat.Musepack => GetMusepack7Duration(header),
        _ => null,
    };

    private static TimeSpan? GetMonkeysAudioDuration(ReadOnlySpan<byte> header)
    {
        if (header.Length < 32)
            return null;

        var version = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
        uint blocksPerFrame, finalFrameBlocks, totalFrames, sampleRate;
        if (version >= 3980)
        {
            // A descriptor, whose size is stored at offset 8, then the header
            var descriptorSize = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
            if (descriptorSize > (uint)(header.Length - 24))
                return null;

            var apeHeader = header[(int)descriptorSize..];
            blocksPerFrame = BinaryPrimitives.ReadUInt32LittleEndian(apeHeader[4..]);
            finalFrameBlocks = BinaryPrimitives.ReadUInt32LittleEndian(apeHeader[8..]);
            totalFrames = BinaryPrimitives.ReadUInt32LittleEndian(apeHeader[12..]);
            sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(apeHeader[20..]);
        }
        else
        {
            var compressionLevel = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
            sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
            totalFrames = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
            finalFrameBlocks = BinaryPrimitives.ReadUInt32LittleEndian(header[28..]);
            blocksPerFrame = version switch
            {
                >= 3950 => 73728 * 4,
                >= 3900 => 73728,
                >= 3800 when compressionLevel == 4000 => 73728,
                _ => 9216,
            };
        }

        if (sampleRate == 0 || totalFrames == 0)
            return null;

        var totalBlocks = ((totalFrames - 1) * (double)blocksPerFrame) + finalFrameBlocks;
        return DurationHelpers.FromSeconds(totalBlocks / sampleRate);
    }

    private static TimeSpan? GetWavPackDuration(ReadOnlySpan<byte> header)
    {
        // The block header: "wvpk", block size, version, total samples (high byte and low 32 bits), block index,
        // block samples, flags and CRC. See https://www.wavpack.com/WavPack5FileFormat.pdf.
        if (header.Length < 32)
            return null;

        var totalSamplesHigh = header[11];
        var totalSamplesLow = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        if (totalSamplesLow == uint.MaxValue)
            return null;

        var totalSamples = totalSamplesLow + ((long)totalSamplesHigh << 32) - totalSamplesHigh;
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
        var sampleRateIndex = (int)((flags >> 23) & 0x0F);
        var sampleRate = sampleRateIndex < WavPackSampleRates.Length ? WavPackSampleRates[sampleRateIndex] : GetWavPackCustomSampleRate(header);
        if (sampleRate <= 0 || totalSamples <= 0)
            return null;

        return DurationHelpers.FromSeconds(totalSamples / (double)sampleRate);
    }

    /// <summary>Reads the sample rate of the metadata sub-block that stores a rate the header has no index for.</summary>
    private static int GetWavPackCustomSampleRate(ReadOnlySpan<byte> header)
    {
        const int SampleRateId = 0x27;
        const int LargeFlag = 0x80;
        const int OddSizeFlag = 0x40;

        var blockSize = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) + 8L;
        var block = header[..(int)Math.Min(blockSize, header.Length)];
        var offset = 32;
        while (offset + 2 <= block.Length)
        {
            var id = block[offset];
            int size;
            if ((id & LargeFlag) != 0)
            {
                if (offset + 4 > block.Length)
                    return 0;

                size = (block[offset + 1] | (block[offset + 2] << 8) | (block[offset + 3] << 16)) * 2;
                offset += 4;
            }
            else
            {
                size = block[offset + 1] * 2;
                offset += 2;
            }

            var dataLength = size - ((id & OddSizeFlag) != 0 ? 1 : 0);
            if ((id & 0x3F) == SampleRateId && dataLength >= 3 && offset + 3 <= block.Length)
                return block[offset] | (block[offset + 1] << 8) | (block[offset + 2] << 16);

            offset += size;
        }

        return 0;
    }

    private static TimeSpan? GetMusepack7Duration(ReadOnlySpan<byte> header)
    {
        // "MP+", the stream version, the frame count, then the stream header whose third byte holds the sample rate index
        if (header.Length < 12)
            return null;

        var frameCount = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var sampleRate = MusepackSampleRates[header[10] & 0x03];
        if (frameCount == 0)
            return null;

        return DurationHelpers.FromSeconds(frameCount * 1152d / sampleRate);
    }

    private static TimeSpan? GetMusepack8Duration(ReadOnlySpan<byte> header)
    {
        // "MPCK", then packets: a two-letter key, the size of the packet (key and size included) as a variable-length
        // integer, and the content. The stream header ("SH") holds a CRC, the stream version, the sample count, the
        // number of silent samples at the start, and the sample rate index.
        var offset = 4;
        while (offset + 3 <= header.Length)
        {
            var key = header.Slice(offset, 2);
            if (!IsUpperCaseLetter(key[0]) || !IsUpperCaseLetter(key[1]))
                return null;

            var sizeStart = offset + 2;
            if (!TryReadVarInt(header, ref sizeStart, out var packetSize) || packetSize < (ulong)(sizeStart - offset) || packetSize > int.MaxValue)
                return null;

            if (key.SequenceEqual("SH"u8))
            {
                var position = sizeStart + 5; // CRC and stream version
                if (!TryReadVarInt(header, ref position, out var sampleCount) || !TryReadVarInt(header, ref position, out var silence) || position >= header.Length)
                    return null;

                var sampleRateIndex = header[position] >> 5;
                if (sampleRateIndex >= MusepackSampleRates.Length || sampleCount == 0 || silence >= sampleCount)
                    return null;

                return DurationHelpers.FromSeconds((sampleCount - silence) / (double)MusepackSampleRates[sampleRateIndex]);
            }

            if ((ulong)offset + packetSize > (ulong)header.Length)
                return null;

            offset += (int)packetSize;
        }

        return null;

        static bool IsUpperCaseLetter(byte value) => value is >= (byte)'A' and <= (byte)'Z';
    }

    private static bool TryReadVarInt(ReadOnlySpan<byte> data, ref int offset, out ulong value)
    {
        value = 0;
        for (var i = 0; i < 9; i++)
        {
            if (offset >= data.Length)
                return false;

            var b = data[offset++];
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
                return true;
        }

        return false;
    }
}
