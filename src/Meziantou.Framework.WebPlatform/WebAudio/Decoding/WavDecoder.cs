using System.Buffers.Binary;

namespace Meziantou.Framework.WebPlatform.WebAudio.Decoding;

/// <summary>
/// Decodes the WAVE files (RIFF): integer PCM of 8 to 32 bits, IEEE floats of 32 and 64 bits, A-law and µ-law, and the
/// same formats in a <c>WAVE_FORMAT_EXTENSIBLE</c> header.
/// </summary>
internal static class WavDecoder
{
    private const ushort FormatPcm = 1;
    private const ushort FormatIeeeFloat = 3;
    private const ushort FormatALaw = 6;
    private const ushort FormatMuLaw = 7;
    private const ushort FormatExtensible = 0xFFFE;

    public static bool IsWav(ReadOnlySpan<byte> data) => data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WAVE"u8);

    /// <summary>Decodes a WAVE file; <paramref name="checkLength"/> is called with the sample rate and the number of frames before the samples are decoded.</summary>
    public static DecodedAudio Decode(ReadOnlySpan<byte> data, Action<double, long> checkLength)
    {
        var offset = 12;
        ushort format = 0;
        var channelCount = 0;
        uint sampleRate = 0;
        var blockAlign = 0;
        var bitsPerSample = 0;
        var hasFormat = false;
        while (offset + 8 <= data.Length)
        {
            var id = data.Slice(offset, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
            var bodyOffset = offset + 8;

            // A chunk can claim more bytes than the file has (a file written while streaming): it ends with the file
            var bodyLength = (int)Math.Min(size, (uint)(data.Length - bodyOffset));
            var body = data.Slice(bodyOffset, bodyLength);
            if (id.SequenceEqual("fmt "u8))
            {
                if (body.Length < 16)
                    throw new InvalidDataException("The 'fmt ' chunk of the WAVE file is too short");

                format = BinaryPrimitives.ReadUInt16LittleEndian(body);
                channelCount = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(body[12..]);
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                if (format == FormatExtensible)
                {
                    // The format is the first 2 bytes of the GUID of the sub-format (KSDATAFORMAT_SUBTYPE_PCM, ..._IEEE_FLOAT...)
                    if (body.Length < 40)
                        throw new InvalidDataException("The 'fmt ' chunk of the WAVE_FORMAT_EXTENSIBLE file is too short");

                    format = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]);
                }

                hasFormat = true;
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (!hasFormat)
                    throw new InvalidDataException("The 'data' chunk of the WAVE file is before its 'fmt ' chunk");

                return DecodeSamples(body, format, channelCount, sampleRate, blockAlign, bitsPerSample, checkLength);
            }

            // The chunks are aligned on 2 bytes
            offset = (int)Math.Min(data.Length, bodyOffset + (long)size + (size & 1));
        }

        throw new InvalidDataException("The WAVE file has no 'data' chunk");
    }

    private static DecodedAudio DecodeSamples(ReadOnlySpan<byte> data, ushort format, int channelCount, uint sampleRate, int blockAlign, int bitsPerSample, Action<double, long> checkLength)
    {
        if (channelCount is 0 or > AudioNode.MaxChannels)
            throw new InvalidDataException($"The WAVE file has {channelCount} channels: an AudioBuffer has from 1 to {AudioNode.MaxChannels} channels");

        // The size of the container of a sample (e.g. 24 valid bits in 32-bit containers)
        var bytesPerSample = (bitsPerSample + 7) / 8;
        if (blockAlign < channelCount * bytesPerSample)
        {
            blockAlign = channelCount * bytesPerSample;
        }

        bytesPerSample = blockAlign / channelCount;
        var supported = format switch
        {
            FormatPcm => bytesPerSample is >= 1 and <= 4,
            FormatIeeeFloat => bytesPerSample is 4 or 8,
            FormatALaw or FormatMuLaw => bytesPerSample == 1,
            _ => false,
        };
        if (!supported)
            throw new InvalidDataException($"The WAVE format {format} with {bitsPerSample} bits per sample is not supported");

        var frames = data.Length / blockAlign;
        checkLength(sampleRate, frames);
        var channels = new float[channelCount][];
        for (var c = 0; c < channelCount; c++)
        {
            channels[c] = new float[frames];
        }

        for (var i = 0; i < frames; i++)
        {
            var frame = data.Slice(i * blockAlign, blockAlign);
            for (var c = 0; c < channelCount; c++)
            {
                var sample = frame.Slice(c * bytesPerSample, bytesPerSample);
                channels[c][i] = format switch
                {
                    FormatIeeeFloat when bytesPerSample == 4 => BinaryPrimitives.ReadSingleLittleEndian(sample),
                    FormatIeeeFloat => (float)BinaryPrimitives.ReadDoubleLittleEndian(sample),
                    FormatALaw => DecodeALaw(sample[0]),
                    FormatMuLaw => DecodeMuLaw(sample[0]),
                    _ => DecodePcm(sample),
                };
            }
        }

        return new DecodedAudio(channels, sampleRate);
    }

    /// <summary>A little-endian integer sample, signed except for 8 bits (unsigned, centered on 128).</summary>
    private static float DecodePcm(ReadOnlySpan<byte> sample)
    {
        switch (sample.Length)
        {
            case 1:
                return (sample[0] - 128) / 128f;
            case 2:
                return BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f;
            case 3:
                var value = sample[0] | (sample[1] << 8) | ((sbyte)sample[2] << 16);
                return value / 8388608f;
            default:
                return (float)(BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648.0);
        }
    }

    /// <summary>G.711 A-law.</summary>
    private static float DecodeALaw(byte value)
    {
        value ^= 0x55;
        var exponent = (value >> 4) & 0x07;
        var mantissa = value & 0x0F;
        var magnitude = exponent == 0 ? (mantissa << 4) + 8 : ((mantissa << 4) + 0x108) << (exponent - 1);
        return ((value & 0x80) != 0 ? magnitude : -magnitude) / 32768f;
    }

    /// <summary>G.711 µ-law.</summary>
    private static float DecodeMuLaw(byte value)
    {
        value = (byte)~value;
        var exponent = (value >> 4) & 0x07;
        var mantissa = value & 0x0F;
        var magnitude = (((mantissa << 3) + 0x84) << exponent) - 0x84;
        return ((value & 0x80) != 0 ? -magnitude : magnitude) / 32768f;
    }
}
