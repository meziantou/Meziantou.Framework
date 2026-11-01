using System.Buffers;
using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

// Snapshot format, version 1. Every field is little-endian:
//
//   offset  size  field
//        0     4  magic "MZBF"
//        4     2  format version
//        6     1  filter kind (1: Bloom filter, 2: counting Bloom filter)
//        7     1  hash algorithm
//        8     2  hash derivation version (how the algorithm's hash is turned into positions)
//       10     1  byte order of the keys (1: little-endian, 2: big-endian)
//       11     1  reserved, 0
//       12     4  hash count
//       16     8  position count (bits or counters)
//       24     8  payload length in bytes
//       32     n  payload: the bits as 64-bit little-endian words, or one byte per counter
//     32+n     8  XxHash3 of the header and the payload
//
// The payload is copied through a small buffer, so writing or reading a filter never allocates a second copy of it.
internal static class BloomFilterSnapshot
{
    public const ushort FormatVersion = 1;

    // Bump when BloomFilterHash or the position derivation changes: the same key would then map to other positions,
    // and an older snapshot would report its values as absent.
    public const ushort HashDerivationVersion = 1;

    private const int HeaderSize = 32;
    private const int TrailerSize = sizeof(ulong);
    private const int BufferSize = 64 * 1024; // A multiple of 8, so a chunk never splits a 64-bit word
    private const byte LittleEndianKeys = 1;
    private const byte BigEndianKeys = 2;

    private static ReadOnlySpan<byte> Magic => "MZBF"u8;

    private static byte NativeKeyByteOrder => BitConverter.IsLittleEndian ? LittleEndianKeys : BigEndianKeys;

    public enum FilterKind : byte
    {
        BloomFilter = 1,
        CountingBloomFilter = 2,
    }

    public enum HashAlgorithm : byte
    {
        XXHash128 = 1,
        XXHash64 = 2,
        XXHash32 = 3,
        XXHash3 = 4,
        Crc64 = 5,
        Crc32 = 6,
        Adler32 = 7,
    }

    public static long GetPayloadLength(FilterKind kind, long positionCount)
    {
        return kind switch
        {
            FilterKind.BloomFilter => (((positionCount - 1) / 64) + 1) * sizeof(ulong),
            _ => positionCount,
        };
    }

    public static void Write(Stream stream, Header header, Payload payload)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var hasher = new XxHash3();
            header.WriteTo(buffer);
            hasher.Append(buffer.AsSpan(0, HeaderSize));
            stream.Write(buffer, 0, HeaderSize);

            var remaining = payload.Length;
            while (remaining > 0)
            {
                var count = (int)Math.Min(BufferSize, remaining);
                payload.CopyTo(buffer.AsSpan(0, count));
                hasher.Append(buffer.AsSpan(0, count));
                stream.Write(buffer, 0, count);
                remaining -= count;
            }

            BinaryPrimitives.WriteUInt64LittleEndian(buffer, hasher.GetCurrentHashAsUInt64());
            stream.Write(buffer, 0, TrailerSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task WriteAsync(Stream stream, Header header, Payload payload, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var hasher = new XxHash3();
            header.WriteTo(buffer);
            hasher.Append(buffer.AsSpan(0, HeaderSize));
            await stream.WriteAsync(buffer.AsMemory(0, HeaderSize), cancellationToken).ConfigureAwait(false);

            var remaining = payload.Length;
            while (remaining > 0)
            {
                var count = (int)Math.Min(BufferSize, remaining);
                payload.CopyTo(buffer.AsSpan(0, count));
                hasher.Append(buffer.AsSpan(0, count));
                await stream.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                remaining -= count;
            }

            BinaryPrimitives.WriteUInt64LittleEndian(buffer, hasher.GetCurrentHashAsUInt64());
            await stream.WriteAsync(buffer.AsMemory(0, TrailerSize), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <param name="createPayload">Validates the header against the requested type, then allocates the filter. It runs only once the header and the stream length are valid.</param>
    public static T Read<T>(Stream stream, FilterKind expectedKind, Func<Header, (T Filter, Payload Payload)> createPayload)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var hasher = new XxHash3();
            ReadExactly(stream, buffer.AsSpan(0, HeaderSize));
            var header = Header.Parse(buffer.AsSpan(0, HeaderSize), expectedKind);
            ValidateRemainingLength(stream, header);
            hasher.Append(buffer.AsSpan(0, HeaderSize));

            var (filter, payload) = createPayload(header);
            var remaining = payload.Length;
            while (remaining > 0)
            {
                var count = (int)Math.Min(BufferSize, remaining);
                ReadExactly(stream, buffer.AsSpan(0, count));
                hasher.Append(buffer.AsSpan(0, count));
                payload.CopyFrom(buffer.AsSpan(0, count));
                remaining -= count;
            }

            ReadExactly(stream, buffer.AsSpan(0, TrailerSize));
            ValidateChecksum(hasher, buffer);
            return filter;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task<T> ReadAsync<T>(Stream stream, FilterKind expectedKind, Func<Header, (T Filter, Payload Payload)> createPayload, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var hasher = new XxHash3();
            await ReadExactlyAsync(stream, buffer.AsMemory(0, HeaderSize), cancellationToken).ConfigureAwait(false);
            var header = Header.Parse(buffer.AsSpan(0, HeaderSize), expectedKind);
            ValidateRemainingLength(stream, header);
            hasher.Append(buffer.AsSpan(0, HeaderSize));

            var (filter, payload) = createPayload(header);
            var remaining = payload.Length;
            while (remaining > 0)
            {
                var count = (int)Math.Min(BufferSize, remaining);
                await ReadExactlyAsync(stream, buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                hasher.Append(buffer.AsSpan(0, count));
                payload.CopyFrom(buffer.AsSpan(0, count));
                remaining -= count;
            }

            await ReadExactlyAsync(stream, buffer.AsMemory(0, TrailerSize), cancellationToken).ConfigureAwait(false);
            ValidateChecksum(hasher, buffer);
            return filter;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static InvalidDataException CreateTypeMismatchException(HashAlgorithm algorithm, Type actualType, Type requestedType)
    {
        return new InvalidDataException($"The snapshot contains a {actualType.Name} filter (hash algorithm {algorithm}), which is not a {requestedType.Name}.");
    }

    private static void ValidateRemainingLength(Stream stream, Header header)
    {
        // A seekable stream tells how much data is left, so a truncated snapshot is rejected before the filter is
        // allocated. Other streams are only known to be truncated when their end is reached.
        if (!stream.CanSeek)
            return;

        var remaining = stream.Length - stream.Position;
        if (remaining < header.PayloadLength + TrailerSize)
            throw CreateTruncatedException();
    }

    private static void ValidateChecksum(XxHash3 hasher, byte[] buffer)
    {
        var expected = BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        if (hasher.GetCurrentHashAsUInt64() != expected)
            throw new InvalidDataException("The Bloom filter snapshot is corrupted: its checksum does not match its content.");
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException ex)
        {
            throw CreateTruncatedException(ex);
        }
    }

    private static async ValueTask ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException ex)
        {
            throw CreateTruncatedException(ex);
        }
    }

    private static InvalidDataException CreateTruncatedException(Exception? innerException = null)
    {
        return new InvalidDataException("The Bloom filter snapshot is truncated: the stream ended before the end of the snapshot.", innerException);
    }

    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Header(FilterKind Kind, HashAlgorithm Algorithm, int HashCount, long PositionCount)
    {
        public long PayloadLength => GetPayloadLength(Kind, PositionCount);

        public void WriteTo(Span<byte> destination)
        {
            Magic.CopyTo(destination);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], FormatVersion);
            destination[6] = (byte)Kind;
            destination[7] = (byte)Algorithm;
            BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], HashDerivationVersion);
            destination[10] = NativeKeyByteOrder;
            destination[11] = 0;
            BinaryPrimitives.WriteInt32LittleEndian(destination[12..], HashCount);
            BinaryPrimitives.WriteInt64LittleEndian(destination[16..], PositionCount);
            BinaryPrimitives.WriteInt64LittleEndian(destination[24..], PayloadLength);
        }

        public static Header Parse(ReadOnlySpan<byte> source, FilterKind expectedKind)
        {
            if (!source[..4].SequenceEqual(Magic))
                throw new InvalidDataException("The data is not a Bloom filter snapshot: the magic number is invalid.");

            var formatVersion = BinaryPrimitives.ReadUInt16LittleEndian(source[4..]);
            if (formatVersion != FormatVersion)
                throw new InvalidDataException($"The Bloom filter snapshot format version {formatVersion} is not supported. Supported version: {FormatVersion}.");

            var kind = (FilterKind)source[6];
            if (kind is not FilterKind.BloomFilter and not FilterKind.CountingBloomFilter)
                throw new InvalidDataException($"The Bloom filter snapshot has an unknown filter kind: {source[6]}.");

            if (kind != expectedKind)
            {
                throw kind is FilterKind.CountingBloomFilter
                    ? new InvalidDataException("The snapshot contains a counting Bloom filter. Load it with CountingBloomFilter.Load.")
                    : new InvalidDataException("The snapshot contains a Bloom filter. Load it with BloomFilter.Load.");
            }

            var algorithm = (HashAlgorithm)source[7];
            if (algorithm is < HashAlgorithm.XXHash128 or > HashAlgorithm.Adler32)
                throw new InvalidDataException($"The Bloom filter snapshot uses an unknown hash algorithm: {source[7]}.");

            var hashDerivationVersion = BinaryPrimitives.ReadUInt16LittleEndian(source[8..]);
            if (hashDerivationVersion != HashDerivationVersion)
                throw new InvalidDataException($"The Bloom filter snapshot uses the hash derivation version {hashDerivationVersion}, which is not supported. Supported version: {HashDerivationVersion}.");

            var keyByteOrder = source[10];
            if (keyByteOrder is not LittleEndianKeys and not BigEndianKeys)
                throw new InvalidDataException($"The Bloom filter snapshot has an unknown key byte order: {keyByteOrder}.");

            // Keys are hashed from their in-memory bytes, so a filter written on a machine with another byte order
            // would report the numbers and strings it contains as absent.
            if (keyByteOrder != NativeKeyByteOrder)
                throw new InvalidDataException($"The Bloom filter snapshot was written on a {(keyByteOrder is LittleEndianKeys ? "little" : "big")}-endian machine and cannot be loaded on a {(BitConverter.IsLittleEndian ? "little" : "big")}-endian machine: its keys were hashed with another byte order.");

            if (source[11] != 0)
                throw new InvalidDataException("The Bloom filter snapshot header is corrupted: the reserved byte is not zero.");

            var hashCount = BinaryPrimitives.ReadInt32LittleEndian(source[12..]);
            if (hashCount <= 0)
                throw new InvalidDataException($"The Bloom filter snapshot has an invalid hash count: {hashCount}.");

            var positionCount = BinaryPrimitives.ReadInt64LittleEndian(source[16..]);
            if (positionCount <= 0 || positionCount > GetMaxPositionCount(kind))
                throw new InvalidDataException($"The Bloom filter snapshot has an invalid size: {positionCount}.");

            var header = new Header(kind, algorithm, hashCount, positionCount);
            var payloadLength = BinaryPrimitives.ReadInt64LittleEndian(source[24..]);
            if (payloadLength != header.PayloadLength)
                throw new InvalidDataException($"The Bloom filter snapshot is corrupted: its payload length is {payloadLength} bytes, but a filter of size {positionCount} needs {header.PayloadLength} bytes.");

            return header;
        }

        private static long GetMaxPositionCount(FilterKind kind)
        {
            // The storages split the positions into arrays of 2^30 elements, and the array of segments has at most
            // Array.MaxLength elements.
            return kind switch
            {
                FilterKind.BloomFilter => long.MaxValue,
                _ => (long)Array.MaxLength << 30,
            };
        }
    }

    // Copies the storage to or from the stream in order, one chunk at a time.
    internal abstract class Payload
    {
        public abstract long Length { get; }

        public abstract void CopyTo(Span<byte> destination);

        public abstract void CopyFrom(ReadOnlySpan<byte> source);
    }

    internal sealed class BitPayload(SegmentedBitStorage storage) : Payload
    {
        private readonly long _wordCount = ((storage.BitCount - 1) / 64) + 1;
        private readonly ulong _paddingMask = storage.BitCount % 64 is 0 ? 0 : ulong.MaxValue << (int)(storage.BitCount % 64);
        private long _wordIndex;
        private int _segmentIndex;
        private int _indexInSegment;

        public override long Length => _wordCount * sizeof(ulong);

        public override void CopyTo(Span<byte> destination)
        {
            for (var offset = 0; offset < destination.Length; offset += sizeof(ulong))
            {
                BinaryPrimitives.WriteUInt64LittleEndian(destination[offset..], Volatile.Read(ref NextWord()));
            }
        }

        public override void CopyFrom(ReadOnlySpan<byte> source)
        {
            for (var offset = 0; offset < source.Length; offset += sizeof(ulong))
            {
                var isLastWord = _wordIndex == _wordCount - 1;
                var value = BinaryPrimitives.ReadUInt64LittleEndian(source[offset..]);

                // The bits past the size of the filter are never set, so a set one means the data is corrupted.
                if (isLastWord && (value & _paddingMask) != 0)
                    throw new InvalidDataException("The Bloom filter snapshot is corrupted: a bit past the size of the filter is set.");

                NextWord() = value;
            }
        }

        private ref ulong NextWord()
        {
            var segment = storage.Segments[_segmentIndex];
            ref var word = ref segment[_indexInSegment];
            _wordIndex++;
            _indexInSegment++;
            if (_indexInSegment == segment.Length)
            {
                _segmentIndex++;
                _indexInSegment = 0;
            }

            return ref word;
        }
    }

    internal sealed class CounterPayload(SegmentedCounterStorage storage) : Payload
    {
        private int _segmentIndex;
        private int _indexInSegment;

        public override long Length => storage.CounterCount;

        public override void CopyTo(Span<byte> destination)
        {
            while (!destination.IsEmpty)
            {
                var segment = NextChunk(destination.Length);
                segment.CopyTo(destination);
                destination = destination[segment.Length..];
            }
        }

        public override void CopyFrom(ReadOnlySpan<byte> source)
        {
            while (!source.IsEmpty)
            {
                var segment = NextChunk(source.Length);
                source[..segment.Length].CopyTo(segment);
                source = source[segment.Length..];
            }
        }

        private Span<byte> NextChunk(int maxLength)
        {
            var segment = storage.Segments[_segmentIndex];
            var length = Math.Min(segment.Length - _indexInSegment, maxLength);
            var result = segment.AsSpan(_indexInSegment, length);
            _indexInSegment += length;
            if (_indexInSegment == segment.Length)
            {
                _segmentIndex++;
                _indexInSegment = 0;
            }

            return result;
        }
    }
}
