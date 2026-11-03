using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Appends the bits of a block to a <see cref="JpegBitWriter"/> without a branch on the data: the accumulator is held in
/// locals, and every <see cref="Write"/> stores the 8 bytes that start at the write position (the pending bits, left-aligned,
/// then bits that later writes replace) and advances the position by the whole bytes among them. The zero bytes stuffed
/// after each <c>0xFF</c> are inserted by <see cref="Complete"/>, which also gives the state back to the bit writer: the
/// bytes are those of <see cref="JpegBitWriter.Write"/>.
/// </summary>
/// <remarks>
/// The destination needs <see cref="SpeculativeBytes"/> bytes past the unstuffed bytes, and room for the stuffed ones;
/// a write that would store past its end throws <see cref="InvalidOperationException"/>, so no store leaves it.
/// </remarks>
internal ref struct JpegBlockBitWriter
{
    /// <summary>The number of bytes past the coded bytes that a write stores.</summary>
    public const int SpeculativeBytes = 8;

    private readonly Span<byte> _destination;
    private readonly int _start;
    private readonly nint _limit;
    private nint _position;
    private ulong _buffer;
    private int _count;

    /// <summary>Starts appending at <paramref name="position"/>, after the pending bits of <paramref name="writer"/>.</summary>
    public JpegBlockBitWriter(Span<byte> destination, int position, in JpegBitWriter writer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        _destination = destination;
        _start = position;
        _position = position;
        _limit = destination.Length - SpeculativeBytes;
        _buffer = writer.Accumulator;
        _count = writer.PendingBitCount;
    }

    /// <summary>Appends <paramref name="length"/> bits (1 to 32, right-aligned, without higher bits set).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(uint bits, int length)
    {
        // At most 7 pending bits before, at most 39 after: the 8 bytes stored hold every pending bit
        var buffer = (_buffer << length) | bits;
        var count = _count + length;
        var position = _position;
        if (position > _limit)
            ThrowFull();

        var aligned = buffer << (64 - count);
        unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(_destination), position), BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(aligned) : aligned); }
        _position = position + (count >> 3);
        _count = count & 7;
        _buffer = buffer;
    }

    /// <summary>Inserts the stuffed zero bytes, stores the pending bits in <paramref name="writer"/>, and returns the new write position.</summary>
    public int Complete(ref JpegBitWriter writer)
    {
        var end = (int)_position;
        var written = _destination[_start..end];
        var first = written.IndexOf((byte)0xFF);
        if (first >= 0)
        {
            // Moves the bytes from the back, each once, a zero after every 0xFF
            var stuffed = written[first..].Count((byte)0xFF);
            var stuffedEnd = end + stuffed;
            if (stuffedEnd > _destination.Length)
                ThrowFull();

            var target = stuffedEnd;
            for (var i = end - 1; stuffed > 0; i--)
            {
                var value = _destination[i];
                if (value == 0xFF)
                {
                    _destination[--target] = 0;
                    stuffed--;
                }

                _destination[--target] = value;
            }

            end = stuffedEnd;
        }

        writer.Restore(_buffer, _count);
        return end;
    }

    private static void ThrowFull() => throw new InvalidOperationException("The destination of the JPEG block is too small.");
}
