using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.WebAudio.Decoding;

/// <summary>
/// Decodes the native FLAC streams (RFC 9639, https://www.rfc-editor.org/rfc/rfc9639): the CONSTANT, VERBATIM, FIXED and LPC
/// subframes, the Rice-coded residuals and the stereo decorrelation. The checksums are not verified.
/// </summary>
internal static class FlacDecoder
{
    private const int StreamInfoBlockType = 0;

    /// <summary>Returns the offset of the <c>fLaC</c> marker: at the start of the data, or after an ID3v2 tag.</summary>
    public static bool TryGetStreamOffset(ReadOnlySpan<byte> data, out int offset)
    {
        offset = 0;
        if (data.Length >= 10 && data[..3].SequenceEqual("ID3"u8))
        {
            // The size of an ID3v2 tag is a 28-bit "syncsafe" integer, without its header (and its footer)
            var size = (data[6] << 21) | (data[7] << 14) | (data[8] << 7) | data[9];
            offset = 10 + size + ((data[5] & 0x10) != 0 ? 10 : 0);
        }

        return offset + 4 <= data.Length && data.Slice(offset, 4).SequenceEqual("fLaC"u8);
    }

    /// <summary>Decodes a FLAC stream starting with its <c>fLaC</c> marker; <paramref name="checkLength"/> is called with the sample rate and the number of frames of the STREAMINFO block, when known, before the samples are decoded.</summary>
    public static DecodedAudio Decode(ReadOnlySpan<byte> data, Action<double, long> checkLength)
    {
        var offset = 4;
        StreamInfo? info = null;
        var last = false;
        while (!last)
        {
            if (offset + 4 > data.Length)
                throw new InvalidDataException("The metadata blocks of the FLAC stream are truncated");

            last = (data[offset] & 0x80) != 0;
            var type = data[offset] & 0x7F;
            var length = (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
            offset += 4;
            if (offset + length > data.Length)
                throw new InvalidDataException("A metadata block of the FLAC stream is truncated");

            if (type == StreamInfoBlockType)
            {
                info = ReadStreamInfo(data.Slice(offset, length));
            }

            offset += length;
        }

        if (info is not { } streamInfo)
            throw new InvalidDataException("The FLAC stream has no STREAMINFO block");

        if (streamInfo.TotalSamples > 0)
        {
            checkLength(streamInfo.SampleRate, streamInfo.TotalSamples);
        }

        var output = new SampleWriter(streamInfo.Channels, streamInfo.TotalSamples);
        var reader = new BitReader(data, offset);
        var samples = new long[streamInfo.Channels][];
        var sampleRate = streamInfo.SampleRate;
        while (reader.BytePosition + 2 <= data.Length)
        {
            if (!IsFrameSync(data, reader.BytePosition))
            {
                // Skips the bytes that are not a frame (e.g. an ID3v1 tag after the last frame)
                reader = new BitReader(data, reader.BytePosition + 1);
                continue;
            }

            var start = reader.BytePosition;
            int frameRate;
            int blockSize;
            int bitsPerSample;
            try
            {
                (blockSize, frameRate, bitsPerSample) = DecodeFrame(ref reader, streamInfo, samples);
            }
            catch (InvalidDataException) when (output.Frames > 0)
            {
                // A corrupted frame after valid ones: resynchronizes on the next frame header
                reader = new BitReader(data, start + 1);
                continue;
            }

            if (frameRate != 0)
            {
                sampleRate = frameRate;
            }

            output.Write(samples, blockSize, bitsPerSample);
            if (streamInfo.TotalSamples > 0 && output.Frames >= streamInfo.TotalSamples)
                break;
        }

        if (output.Frames == 0)
            throw new InvalidDataException("The FLAC stream has no audio frame");

        if (streamInfo.TotalSamples == 0)
        {
            checkLength(sampleRate, output.Frames);
        }

        return new DecodedAudio(output.ToArrays(), sampleRate);
    }

    private static bool IsFrameSync(ReadOnlySpan<byte> data, int offset) => data[offset] == 0xFF && (data[offset + 1] & 0xFE) == 0xF8;

    private static StreamInfo ReadStreamInfo(ReadOnlySpan<byte> block)
    {
        if (block.Length < 34)
            throw new InvalidDataException("The STREAMINFO block of the FLAC stream is too short");

        // 20 bits of sample rate, 3 bits of (channels - 1), 5 bits of (bits per sample - 1), 36 bits of total samples
        var packed = BinaryPrimitives.ReadUInt64BigEndian(block[10..]);
        var sampleRate = (int)(packed >> 44);
        var channels = (int)((packed >> 41) & 0x07) + 1;
        var bitsPerSample = (int)((packed >> 36) & 0x1F) + 1;
        var totalSamples = (long)(packed & 0xF_FFFF_FFFF);
        if (sampleRate == 0)
            throw new InvalidDataException("The sample rate of the FLAC stream is 0");

        if (bitsPerSample < 4)
            throw new InvalidDataException($"The FLAC stream has {bitsPerSample} bits per sample");

        return new StreamInfo(sampleRate, channels, bitsPerSample, totalSamples);
    }

    /// <summary>Decodes a frame (https://www.rfc-editor.org/rfc/rfc9639#section-9): returns its block size, its sample rate (0 for the one of the STREAMINFO block) and its bits per sample.</summary>
    private static (int BlockSize, int SampleRate, int BitsPerSample) DecodeFrame(ref BitReader reader, StreamInfo info, long[][] samples)
    {
        _ = reader.ReadBits(16); // The sync code, a reserved bit and the blocking strategy
        var blockSizeCode = (int)reader.ReadBits(4);
        var sampleRateCode = (int)reader.ReadBits(4);
        var channelCode = (int)reader.ReadBits(4);
        var sampleSizeCode = (int)reader.ReadBits(3);
        if (reader.ReadBits(1) != 0)
            throw new InvalidDataException("The reserved bit of a FLAC frame header is set");

        // The coded frame or sample number: an UTF-8-like integer of 1 to 7 bytes
        var first = reader.ReadBits(8);
        var extraBytes = first switch
        {
            < 0x80 => 0,
            >= 0xC0 and < 0xE0 => 1,
            >= 0xE0 and < 0xF0 => 2,
            >= 0xF0 and < 0xF8 => 3,
            >= 0xF8 and < 0xFC => 4,
            >= 0xFC and < 0xFE => 5,
            0xFE => 6,
            _ => throw new InvalidDataException("The coded number of a FLAC frame header is not valid"),
        };
        for (var i = 0; i < extraBytes; i++)
        {
            if ((reader.ReadBits(8) & 0xC0) != 0x80)
                throw new InvalidDataException("The coded number of a FLAC frame header is not valid");
        }

        var blockSize = blockSizeCode switch
        {
            0 => throw new InvalidDataException("The block size of a FLAC frame uses a reserved code"),
            1 => 192,
            >= 2 and <= 5 => 576 << (blockSizeCode - 2),
            6 => (int)reader.ReadBits(8) + 1,
            7 => (int)reader.ReadBits(16) + 1,
            _ => 256 << (blockSizeCode - 8),
        };
        var sampleRate = sampleRateCode switch
        {
            0 => 0,
            1 => 88200,
            2 => 176400,
            3 => 192000,
            4 => 8000,
            5 => 16000,
            6 => 22050,
            7 => 24000,
            8 => 32000,
            9 => 44100,
            10 => 48000,
            11 => 96000,
            12 => (int)reader.ReadBits(8) * 1000,
            13 => (int)reader.ReadBits(16),
            14 => (int)reader.ReadBits(16) * 10,
            _ => throw new InvalidDataException("The sample rate of a FLAC frame uses a reserved code"),
        };
        var bitsPerSample = sampleSizeCode switch
        {
            0 => info.BitsPerSample,
            1 => 8,
            2 => 12,
            4 => 16,
            5 => 20,
            6 => 24,
            7 => 32,
            _ => throw new InvalidDataException("The sample size of a FLAC frame uses a reserved code"),
        };
        _ = reader.ReadBits(8); // CRC-8 of the header

        var channels = channelCode switch
        {
            < 8 => channelCode + 1,
            8 or 9 or 10 => 2,
            _ => throw new InvalidDataException("The channel assignment of a FLAC frame uses a reserved code"),
        };
        if (channels != info.Channels)
            throw new InvalidDataException($"A FLAC frame has {channels} channels, the stream has {info.Channels}");

        if (samples[0] is null || samples[0].Length < blockSize)
        {
            for (var c = 0; c < samples.Length; c++)
            {
                samples[c] = new long[blockSize];
            }
        }

        for (var c = 0; c < channels; c++)
        {
            // The side channel has one more bit
            var isSide = (channelCode == 8 && c == 1) || (channelCode == 9 && c == 0) || (channelCode == 10 && c == 1);
            DecodeSubframe(ref reader, samples[c].AsSpan(0, blockSize), bitsPerSample + (isSide ? 1 : 0));
        }

        Decorrelate(channelCode, samples, blockSize);
        reader.AlignToByte();
        _ = reader.ReadBits(16); // CRC-16 of the frame
        return (blockSize, sampleRate, bitsPerSample);
    }

    /// <summary>Restores the left and right channels of the stereo decorrelation (https://www.rfc-editor.org/rfc/rfc9639#section-4.2).</summary>
    private static void Decorrelate(int channelCode, long[][] samples, int blockSize)
    {
        var left = samples[0];
        var right = channelCode is 8 or 9 or 10 ? samples[1] : null;
        for (var i = 0; i < blockSize && right is not null; i++)
        {
            switch (channelCode)
            {
                case 8: // left, side
                    right[i] = left[i] - right[i];
                    break;
                case 9: // side, right
                    left[i] += right[i];
                    break;
                default: // mid, side
                    var side = right[i];
                    var mid = (left[i] << 1) | (side & 1);
                    left[i] = (mid + side) >> 1;
                    right[i] = (mid - side) >> 1;
                    break;
            }
        }
    }

    /// <summary>Decodes a subframe (https://www.rfc-editor.org/rfc/rfc9639#section-9.2).</summary>
    private static void DecodeSubframe(ref BitReader reader, Span<long> output, int bitsPerSample)
    {
        if (reader.ReadBits(1) != 0)
            throw new InvalidDataException("The padding bit of a FLAC subframe header is set");

        var type = (int)reader.ReadBits(6);
        var wastedBits = 0;
        if (reader.ReadBits(1) != 0)
        {
            wastedBits = reader.ReadUnary() + 1;
            bitsPerSample -= wastedBits;
            if (bitsPerSample <= 0)
                throw new InvalidDataException("A FLAC subframe has more wasted bits than bits per sample");
        }

        switch (type)
        {
            case 0:
                output.Fill(reader.ReadSigned(bitsPerSample));
                break;

            case 1:
                for (var i = 0; i < output.Length; i++)
                {
                    output[i] = reader.ReadSigned(bitsPerSample);
                }

                break;

            case >= 8 and <= 12:
                DecodeFixed(ref reader, output, bitsPerSample, type - 8);
                break;

            case >= 32:
                DecodeLpc(ref reader, output, bitsPerSample, type - 31);
                break;

            default:
                throw new InvalidDataException($"The FLAC subframe type {type} is reserved");
        }

        if (wastedBits > 0)
        {
            for (var i = 0; i < output.Length; i++)
            {
                output[i] <<= wastedBits;
            }
        }
    }

    private static void ReadWarmUp(ref BitReader reader, Span<long> output, int bitsPerSample, int order)
    {
        if (order > output.Length)
            throw new InvalidDataException($"The predictor order {order} of a FLAC subframe is larger than its block size");

        for (var i = 0; i < order; i++)
        {
            output[i] = reader.ReadSigned(bitsPerSample);
        }
    }

    /// <summary>A subframe of the fixed predictors (https://www.rfc-editor.org/rfc/rfc9639#section-9.2.5).</summary>
    private static void DecodeFixed(ref BitReader reader, Span<long> output, int bitsPerSample, int order)
    {
        ReadWarmUp(ref reader, output, bitsPerSample, order);
        DecodeResidual(ref reader, output, order);
        for (var i = order; i < output.Length; i++)
        {
            output[i] += order switch
            {
                0 => 0,
                1 => output[i - 1],
                2 => (2 * output[i - 1]) - output[i - 2],
                3 => (3 * output[i - 1]) - (3 * output[i - 2]) + output[i - 3],
                _ => (4 * output[i - 1]) - (6 * output[i - 2]) + (4 * output[i - 3]) - output[i - 4],
            };
        }
    }

    /// <summary>A subframe of a linear predictor (https://www.rfc-editor.org/rfc/rfc9639#section-9.2.6).</summary>
    private static void DecodeLpc(ref BitReader reader, Span<long> output, int bitsPerSample, int order)
    {
        ReadWarmUp(ref reader, output, bitsPerSample, order);
        var precision = (int)reader.ReadBits(4) + 1;
        if (precision == 16)
            throw new InvalidDataException("The coefficient precision of a FLAC subframe uses a reserved code");

        var shift = (int)reader.ReadSigned(5);
        if (shift < 0)
            throw new InvalidDataException("The prediction shift of a FLAC subframe is negative");

        Span<long> coefficients = stackalloc long[32];
        for (var i = 0; i < order; i++)
        {
            coefficients[i] = reader.ReadSigned(precision);
        }

        DecodeResidual(ref reader, output, order);
        for (var i = order; i < output.Length; i++)
        {
            long prediction = 0;
            for (var j = 0; j < order; j++)
            {
                prediction += coefficients[j] * output[i - 1 - j];
            }

            output[i] += prediction >> shift;
        }
    }

    /// <summary>Decodes the Rice-coded residual (https://www.rfc-editor.org/rfc/rfc9639#section-9.2.7) into the samples after the warm-up ones.</summary>
    private static void DecodeResidual(ref BitReader reader, Span<long> output, int order)
    {
        var method = (int)reader.ReadBits(2);
        if (method > 1)
            throw new InvalidDataException("The residual coding method of a FLAC subframe is reserved");

        var parameterBits = method == 0 ? 4 : 5;
        var escape = (1 << parameterBits) - 1;
        var partitionOrder = (int)reader.ReadBits(4);
        var partitions = 1 << partitionOrder;
        var partitionSize = output.Length >> partitionOrder;
        if ((partitionSize << partitionOrder) != output.Length || partitionSize < order)
            throw new InvalidDataException("The partition order of a FLAC residual does not divide its block size");

        var index = order;
        for (var p = 0; p < partitions; p++)
        {
            var count = p == 0 ? partitionSize - order : partitionSize;
            var parameter = (int)reader.ReadBits(parameterBits);
            if (parameter == escape)
            {
                // Unencoded residual: signed integers of a fixed number of bits
                var bits = (int)reader.ReadBits(5);
                for (var i = 0; i < count; i++)
                {
                    output[index++] = bits == 0 ? 0 : reader.ReadSigned(bits);
                }
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    var quotient = (ulong)reader.ReadUnary();
                    var value = (quotient << parameter) | reader.ReadBits(parameter);

                    // The zigzag encoding of the signed value
                    output[index++] = (long)(value >> 1) ^ -(long)(value & 1);
                }
            }
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct StreamInfo(int SampleRate, int Channels, int BitsPerSample, long TotalSamples);

    /// <summary>Accumulates the samples of the frames as floats.</summary>
    private sealed class SampleWriter
    {
        private readonly long _capacity;
        private float[][] _buffers;

        public SampleWriter(int channels, long totalSamples)
        {
            // The number of samples of the STREAMINFO block is not trusted for the allocation: the buffers grow with the frames
            _capacity = totalSamples;
            _buffers = new float[channels][];
            for (var c = 0; c < channels; c++)
            {
                _buffers[c] = new float[totalSamples > 0 ? Math.Min(totalSamples, 1 << 20) : 4096];
            }
        }

        public long Frames { get; private set; }

        public void Write(long[][] samples, int blockSize, int bitsPerSample)
        {
            // The frames after the number of samples of the STREAMINFO block are ignored
            var count = _capacity > 0 ? (int)Math.Min(blockSize, _capacity - Frames) : blockSize;
            EnsureCapacity(Frames + count);
            var scale = 1.0 / (1L << (bitsPerSample - 1));
            for (var c = 0; c < _buffers.Length; c++)
            {
                var source = samples[c];
                var destination = _buffers[c].AsSpan((int)Frames, count);
                for (var i = 0; i < count; i++)
                {
                    destination[i] = (float)(source[i] * scale);
                }
            }

            Frames += count;
        }

        private void EnsureCapacity(long length)
        {
            if (length <= _buffers[0].Length)
                return;

            if (length > Array.MaxLength)
                throw new InvalidDataException("The FLAC stream has too many samples");

            var newLength = (int)Math.Min(Array.MaxLength, Math.Max(length, _buffers[0].Length * 2L));
            var buffers = new float[_buffers.Length][];
            for (var c = 0; c < buffers.Length; c++)
            {
                buffers[c] = new float[newLength];
                _buffers[c].AsSpan(0, (int)Frames).CopyTo(buffers[c]);
            }

            _buffers = buffers;
        }

        public float[][] ToArrays()
        {
            if (_buffers[0].Length == Frames)
                return _buffers;

            var result = new float[_buffers.Length][];
            for (var c = 0; c < result.Length; c++)
            {
                result[c] = _buffers[c].AsSpan(0, (int)Frames).ToArray();
            }

            return result;
        }
    }

    /// <summary>Reads the bits of a stream, most significant bit first.</summary>
    [StructLayout(LayoutKind.Auto)]
    private ref struct BitReader(ReadOnlySpan<byte> data, int byteOffset)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private long _bitPosition = byteOffset * 8L;

        public readonly int BytePosition => (int)((_bitPosition + 7) / 8);

        public uint ReadBits(int count)
        {
            if (count == 0)
                return 0;

            if (_bitPosition + count > _data.Length * 8L)
                throw new InvalidDataException("The FLAC stream is truncated");

            ulong value = 0;
            var remaining = count;
            while (remaining > 0)
            {
                var byteIndex = (int)(_bitPosition >> 3);
                var bitOffset = (int)(_bitPosition & 7);
                var available = 8 - bitOffset;
                var take = Math.Min(available, remaining);
                var bits = (_data[byteIndex] >> (available - take)) & ((1 << take) - 1);
                value = (value << take) | (uint)bits;
                remaining -= take;
                _bitPosition += take;
            }

            return (uint)value;
        }

        /// <summary>Reads a two's complement integer of up to 33 bits (the side channel of 32-bit samples).</summary>
        public long ReadSigned(int count)
        {
            if (count > 32)
            {
                var high = (long)ReadBits(count - 32);
                var low = ReadBits(32);
                var value = (high << 32) | low;
                return (value << (64 - count)) >> (64 - count);
            }

            var bits = (long)ReadBits(count);
            return (bits << (64 - count)) >> (64 - count);
        }

        /// <summary>Reads the number of 0 bits before a 1 bit.</summary>
        public int ReadUnary()
        {
            var count = 0;
            while (true)
            {
                if (_bitPosition >= _data.Length * 8L)
                    throw new InvalidDataException("The FLAC stream is truncated");

                var byteIndex = (int)(_bitPosition >> 3);
                var bitOffset = (int)(_bitPosition & 7);
                var remainingBits = (byte)(_data[byteIndex] << bitOffset);
                if (remainingBits == 0)
                {
                    // The rest of the byte is zeros
                    count += 8 - bitOffset;
                    _bitPosition += 8 - bitOffset;
                    continue;
                }

                var zeros = System.Numerics.BitOperations.LeadingZeroCount((uint)remainingBits) - 24;
                count += zeros;
                _bitPosition += zeros + 1;
                return count;
            }
        }

        public void AlignToByte() => _bitPosition = (_bitPosition + 7) & ~7L;
    }
}
