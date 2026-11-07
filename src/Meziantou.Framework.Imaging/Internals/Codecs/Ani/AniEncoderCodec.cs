using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The animated cursor (<c>ANI</c>) encoder registration: every frame of the output is one step of the animation.
/// </summary>
internal sealed class AniEncoderCodec : ImageEncoderCodec
{
    private AniEncoderCodec()
    {
    }

    public static AniEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Ani;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    /// <summary>
    /// The encoding state of one animated cursor. The file is written forward, frame by frame, and the values known only at
    /// the end are patched at completion (<see cref="ImageOutputBuffer.AddPatch"/>; the destination is seekable, see
    /// <see cref="ImageOutputCapabilities.RequiresSeekableOutput"/>):
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item><description>before the first frame: the RIFF header, the <c>INFO</c> list (title, author), the <c>anih</c> chunk and the header of the <c>fram</c> list, with zero placeholders;</description></item>
    /// <item><description>per frame: a one-representation cursor file (<see cref="IcoDocumentWriter"/>) in an <c>icon</c> chunk, unless an identical one was already written;</description></item>
    /// <item><description>at completion: the <c>rate</c> chunk when the steps do not all have the same rate, the <c>seq </c> chunk when a frame is shown by several steps, then the RIFF size, the <c>anih</c> fields and the size of the frame list.</description></item>
    /// </list>
    /// <para>
    /// Only one encoded frame is alive at a time. What grows with the animation is 4 bytes per step for each of the two
    /// tables and the digest of each distinct frame, all charged to the allocation scope of the writer.
    /// </para>
    /// <para>
    /// Two frames are the same stored frame when their encoded cursor files are identical: same size, same hotspot and
    /// same payload bytes. They are compared by SHA-256 digest, because the bytes of an earlier frame were already handed to
    /// the destination.
    /// </para>
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        private const int EmitPieceLength = 64 * 1024;
        private const int DigestLength = 32;
        private const int DigestEntryBytes = 64;
        private const int InitialDigestCapacity = 16;

        private readonly AniEncoder _encoder;
        private readonly IcoEncoder _frameEncoder;
        private readonly byte[] _prefix;
        private readonly int _headerOffset;
        private readonly int _frameListSizeOffset;
        private readonly Dictionary<FrameDigest, uint> _frames = [];
        private readonly UInt32Table _rates;
        private readonly UInt32Table _sequence;
        private IncrementalHash? _hash;
        private AllocationCharge? _digestCharge;
        private int _digestCapacity;
        private ImageFrame? _frame;
        private bool _prefixWritten;
        private bool _completing;
        private bool _completionStarted;
        private bool _anyReuse;
        private bool _uniformRate = true;
        private uint _firstRate;
        private uint _frameCount;
        private uint _stepCount;
        private long _frameListEnd;
        private int _emitStage;
        private int _emitOffset;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _encoder = (AniEncoder)options.Encoder;

            // The rule of icon and cursor payloads: 16-bit samples go to a PNG, and an explicit DIB for them is an error
            var usePng = IcoDocumentWriter.UsesPngPayload(ImageFormat.Ani, _encoder.PayloadFormat, options.CanvasSize, options.PixelFormat);

            // Each frame is a complete cursor file holding one representation
            _frameEncoder = new IcoEncoder { Kind = IconKind.Cursor, PayloadFormat = usePng ? IconPayloadFormat.Png : IconPayloadFormat.Dib };
            _prefix = CreatePrefix(options.Metadata, out _headerOffset, out _frameListSizeOffset);
            _rates = new UInt32Table(options.Scope);
            _sequence = new UInt32Table(options.Scope);
        }

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);

            // Throws UnsupportedImageFeatureException for unrepresentable durations (strict mode, or above 2^32 - 1 jiffies)
            _ = AnimationTiming.ToAniRate(frame.Metadata.Duration, _encoder.DurationRounding);
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("ANI output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            _frame = frame;
        }

        public override void BeginComplete(int frameCount) => _completing = true;

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            if (_frame is { } frame)
            {
                _frame = null;
                EncodeFrame(frame, output);
                return true;
            }

            if (!_completing)
                throw new InvalidOperationException("No ANI operation was begun.");

            return EncodeCompletion(output);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _rates.Dispose();
                _sequence.Dispose();
                _hash?.Dispose();
                _hash = null;
                _digestCharge?.Dispose();
                _digestCharge = null;
                _frames.Clear();
                _frame = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>Builds everything that precedes the first <c>icon</c> chunk; the sizes and the header fields are patched at completion.</summary>
        private static byte[] CreatePrefix(MetadataWritePlan metadata, out int headerOffset, out int frameListSizeOffset)
        {
            var info = CreateInfoList(metadata);
            headerOffset = AniFormat.RiffHeaderLength + info.Length + AniFormat.ChunkHeaderLength;
            frameListSizeOffset = headerOffset + AniFormat.HeaderLength + 4;
            var prefix = new byte[frameListSizeOffset + 4 + 4];
            var span = prefix.AsSpan();
            AniFormat.WriteChunkHeader(span, AniFormat.Riff, 0);
            AniFormat.Acon.CopyTo(span[8..]);
            info.CopyTo(span[AniFormat.RiffHeaderLength..]);
            AniFormat.WriteChunkHeader(span[(headerOffset - AniFormat.ChunkHeaderLength)..], AniFormat.Header, AniFormat.HeaderLength);
            AniFormat.WriteChunkHeader(span[(headerOffset + AniFormat.HeaderLength)..], AniFormat.List, 0);
            AniFormat.Frames.CopyTo(span[(frameListSizeOffset + 4)..]);
            return prefix;
        }

        /// <summary>Builds the <c>INFO</c> list: the title and the author as NUL-terminated Latin-1 strings, in the order of the text entries.</summary>
        private static byte[] CreateInfoList(MetadataWritePlan metadata)
        {
            if (metadata.TextEntries.Count == 0)
                return [];

            var length = AniFormat.ChunkHeaderLength + 4;
            foreach (var entry in metadata.TextEntries)
            {
                var dataLength = entry.Value.Length + 1;
                length += AniFormat.ChunkHeaderLength + dataLength + (dataLength & 1);
            }

            var list = new byte[length];
            var span = list.AsSpan();
            AniFormat.WriteChunkHeader(span, AniFormat.List, (uint)(length - AniFormat.ChunkHeaderLength));
            AniFormat.Info.CopyTo(span[AniFormat.ChunkHeaderLength..]);
            var offset = AniFormat.ChunkHeaderLength + 4;
            foreach (var entry in metadata.TextEntries)
            {
                // The plan only keeps the two keywords the list stores, with Latin-1 values (one byte per character)
                var dataLength = entry.Value.Length + 1;
                AniFormat.WriteChunkHeader(span[offset..], string.Equals(entry.Keyword, AniFormat.TitleKeyword, StringComparison.Ordinal) ? AniFormat.Title : AniFormat.Author, (uint)dataLength);
                Encoding.Latin1.GetBytes(entry.Value, span[(offset + AniFormat.ChunkHeaderLength)..]);
                offset += AniFormat.ChunkHeaderLength + dataLength + (dataLength & 1);
            }

            return list;
        }

        private void EncodeFrame(ImageFrame frame, ImageOutputBuffer output)
        {
            if (!_prefixWritten)
            {
                output.Write(_prefix);
                _prefixWritten = true;
            }

            var rate = AnimationTiming.ToAniRate(frame.MetadataCore.Duration, _encoder.DurationRounding);
            if (_stepCount == 0)
            {
                _firstRate = rate;
            }
            else if (rate != _firstRate)
            {
                _uniformRate = false;
            }

            var hotspot = frame.MetadataCore.Hotspot ?? new Point(0, 0);
            var entry = IcoDocumentWriter.EncodeEntry(_frameEncoder, new IcoDocumentWriter.Entry(frame, Options.PixelFormat, hotspot), Options.Scope, Options.Configuration);
            try
            {
                var digest = ComputeDigest(entry);
                if (_frames.TryGetValue(digest, out var index))
                {
                    // The same cursor image was already written: this step shows it again
                    _anyReuse = true;
                }
                else
                {
                    index = _frameCount;
                    var fileLength = IcoFormat.HeaderLength + IcoFormat.EntryLength + entry.Payload.Length;
                    AniFormat.EnsureFileLength(output.TotalBytes + AniFormat.ChunkHeaderLength + fileLength + (fileLength & 1));
                    EnsureDigestCapacity();
                    Span<byte> header = stackalloc byte[AniFormat.ChunkHeaderLength];
                    AniFormat.WriteChunkHeader(header, AniFormat.Icon, (uint)fileLength);
                    output.Write(header);
                    IcoDocumentWriter.WriteEncoded(_frameEncoder, [entry], output);
                    if ((fileLength & 1) != 0)
                    {
                        output.Write([0]);
                    }

                    _frames.Add(digest, index);
                    _frameCount++;
                }

                _rates.Add(rate);
                _sequence.Add(index);
                _stepCount++;
            }
            finally
            {
                entry.Payload.Dispose();
            }
        }

        private FrameDigest ComputeDigest(IcoDocumentWriter.EncodedEntry entry)
        {
            var hash = _hash ??= IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var hotspot = entry.Hotspot ?? new Point(0, 0);
            Span<byte> buffer = stackalloc byte[DigestLength];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, entry.Size.Width);
            BinaryPrimitives.WriteInt32LittleEndian(buffer[4..], entry.Size.Height);
            BinaryPrimitives.WriteInt32LittleEndian(buffer[8..], hotspot.X);
            BinaryPrimitives.WriteInt32LittleEndian(buffer[12..], hotspot.Y);
            hash.AppendData(buffer[..16]);
            hash.AppendData(entry.Payload.Span);
            var written = hash.GetHashAndReset(buffer);
            if (written != DigestLength)
                throw new InvalidOperationException("The digest has an unexpected length.");

            return new FrameDigest(
                BinaryPrimitives.ReadUInt64LittleEndian(buffer),
                BinaryPrimitives.ReadUInt64LittleEndian(buffer[8..]),
                BinaryPrimitives.ReadUInt64LittleEndian(buffer[16..]),
                BinaryPrimitives.ReadUInt64LittleEndian(buffer[24..]));
        }

        /// <summary>Charges the digest table to the scope before it grows (doubling, like the table itself).</summary>
        private void EnsureDigestCapacity()
        {
            if (_frames.Count < _digestCapacity)
                return;

            var capacity = _digestCapacity == 0 ? InitialDigestCapacity : checked(_digestCapacity * 2);
            var charge = Options.Scope.Charge((long)capacity * DigestEntryBytes, AllocationKind.Temporary);
            _digestCharge?.Dispose();
            _digestCharge = charge;
            _digestCapacity = capacity;
            _frames.EnsureCapacity(capacity);
        }

        private bool EncodeCompletion(ImageOutputBuffer output)
        {
            if (!_completionStarted)
            {
                _completionStarted = true;
                _frameListEnd = output.TotalBytes;
                var tableLength = (long)_stepCount * AniFormat.TableEntryLength;
                long total = _frameListEnd;
                if (!_uniformRate)
                {
                    total += AniFormat.ChunkHeaderLength + tableLength;
                }

                if (_anyReuse)
                {
                    total += AniFormat.ChunkHeaderLength + tableLength;
                }

                // Before the first byte of a table, so that an oversized file is reported while the output is still consistent
                AniFormat.EnsureFileLength(total);
                _emitStage = 0;
                _emitOffset = -1;
            }

            // Bounded pieces: the writer flushes between calls
            var budget = EmitPieceLength;
            while (_emitStage < 2)
            {
                var isRateStage = _emitStage == 0;
                var isWritten = isRateStage ? !_uniformRate : _anyReuse;
                if (isWritten && !EmitTable(output, isRateStage ? _rates : _sequence, isRateStage ? AniFormat.Rate : AniFormat.Sequence, ref budget))
                    return false;

                _emitStage++;
                _emitOffset = -1;
            }

            Span<byte> buffer = stackalloc byte[AniFormat.HeaderLength];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)(output.TotalBytes - 8));
            output.AddPatch(4, buffer[..4]);

            buffer.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, AniFormat.HeaderLength);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..], _frameCount);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[8..], _stepCount);

            // The four geometry fields stay zero: the embedded cursor files describe themselves
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[28..], _firstRate);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[32..], AniFormat.IconFlag | (_anyReuse ? AniFormat.SequenceFlag : 0));
            output.AddPatch(_headerOffset, buffer);

            BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)(_frameListEnd - (_frameListSizeOffset + 4)));
            output.AddPatch(_frameListSizeOffset, buffer[..4]);
            _completing = false;
            return true;
        }

        /// <summary>Emits a table chunk within the budget of the call.</summary>
        /// <returns><see langword="true"/> once the whole chunk is written.</returns>
        private bool EmitTable(ImageOutputBuffer output, UInt32Table table, ReadOnlySpan<byte> fourCC, ref int budget)
        {
            var data = table.Bytes;
            if (_emitOffset < 0)
            {
                Span<byte> header = stackalloc byte[AniFormat.ChunkHeaderLength];
                AniFormat.WriteChunkHeader(header, fourCC, (uint)data.Length);
                output.Write(header);
                _emitOffset = 0;
            }

            var count = Math.Min(budget, data.Length - _emitOffset);
            output.Write(data.Slice(_emitOffset, count));
            _emitOffset += count;
            budget -= count;
            return _emitOffset == data.Length;
        }

        [StructLayout(LayoutKind.Auto)]
        private readonly record struct FrameDigest(ulong A, ulong B, ulong C, ulong D);

        /// <summary>A growable table of little-endian 32-bit values, as it is stored in the file, in a buffer of the allocation scope.</summary>
        private sealed class UInt32Table(AllocationScope scope) : IDisposable
        {
            private const int InitialCapacity = 256;

            private PooledBuffer? _buffer;
            private int _length;

            public ReadOnlySpan<byte> Bytes => _buffer is null ? [] : _buffer.Span[.._length];

            public void Add(uint value)
            {
                if (_buffer is null || _length == _buffer.Length)
                {
                    Grow();
                }

                BinaryPrimitives.WriteUInt32LittleEndian(_buffer!.Span[_length..], value);
                _length += AniFormat.TableEntryLength;
            }

            public void Dispose()
            {
                _buffer?.Dispose();
                _buffer = null;
                _length = 0;
            }

            private void Grow()
            {
                var capacity = _buffer is null ? InitialCapacity : (long)_buffer.Length * 2;
                if (capacity > CheckedSizes.MaxBufferLength)
                    throw CheckedSizes.CreateOverflowException(scope.Limits);

                var larger = scope.Rent((int)capacity, AllocationKind.Temporary, clear: false);
                if (_buffer is not null)
                {
                    _buffer.Span[.._length].CopyTo(larger.Span);
                    _buffer.Dispose();
                }

                _buffer = larger;
            }
        }
    }
}
