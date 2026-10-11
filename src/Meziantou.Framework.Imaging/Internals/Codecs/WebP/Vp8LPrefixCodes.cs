using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The canonical prefix codes of one WebP lossless image stream (WebP Lossless Bitstream specification, section 6),
/// stored as two-level lookup tables in one growable arena rented from the decoder's allocation scope.
/// </summary>
/// <remarks>
/// <para>
/// Codes are canonical (DEFLATE convention): shorter codes first, then increasing symbol values; the code bits are read
/// most significant first from the least-significant-bit-first stream, so table indexes are bit-reversed codes. Each code
/// has a root table of <c>2^min(maxLength, 8)</c> entries; longer codes go through one second-level table per root prefix.
/// </para>
/// <para>
/// Validation (all <see cref="InvalidImageContentException"/>): code lengths above 15, codes that are not complete (the sum
/// of <c>2^-length</c> must be exactly one), and codes without any symbol. A code with a single used symbol is a zero-bit
/// code whatever its declared length (specification: "the single leaf node tree").
/// </para>
/// <para>Entry layout: bits 16-31 are the number of bits consumed at this level, bits 0-15 the symbol (or, for a root entry whose bit count exceeds the root bits, the offset of its second-level table relative to the code).</para>
/// </remarks>
internal sealed class Vp8LPrefixCodes : IDisposable
{
    public const int MaxCodeLength = 15;
    private const int MaxRootBits = 8;
    private const int InitialCapacity = 1024;

    private readonly AllocationScope _scope;
    private PooledBuffer? _arena;
    private int _used;

    public Vp8LPrefixCodes(AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
    }

    /// <summary>Gets the current end of the arena, to release the codes built afterward with <see cref="Release"/>.</summary>
    public int Mark => _used;

    /// <summary>Releases every code (the arena is reused by the next <see cref="Build"/>).</summary>
    public void Reset() => _used = 0;

    /// <summary>Releases the codes built after <paramref name="mark"/> (a value of <see cref="Mark"/>).</summary>
    public void Release(int mark)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mark);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(mark, _used);
        _used = mark;
    }

    /// <summary>Builds the lookup tables of a code from its code lengths.</summary>
    /// <param name="codeLengths">The code length of each symbol of the alphabet (0 = unused).</param>
    /// <returns>The code handle.</returns>
    /// <exception cref="InvalidImageContentException">The lengths do not describe a valid complete prefix code.</exception>
    public Vp8LPrefixCode Build(ReadOnlySpan<byte> codeLengths)
    {
        Span<int> counts = stackalloc int[MaxCodeLength + 1];
        counts.Clear();
        var usedSymbols = 0;
        var lastSymbol = 0;
        var maxLength = 0;
        for (var symbol = 0; symbol < codeLengths.Length; symbol++)
        {
            int length = codeLengths[symbol];
            if (length == 0)
                continue;

            if (length > MaxCodeLength)
                throw Invalid("A WebP lossless code length is above 15.");

            counts[length]++;
            usedSymbols++;
            lastSymbol = symbol;
            maxLength = Math.Max(maxLength, length);
        }

        if (usedSymbols == 0)
            throw Invalid("A WebP lossless prefix code has no symbol.");

        if (usedSymbols == 1)
        {
            // The single leaf node tree: no bit is consumed
            var offset = Allocate(1);
            Entries[offset] = (uint)lastSymbol;
            return new Vp8LPrefixCode(offset, 0);
        }

        // Complete tree: sum(count[len] * 2^(15 - len)) == 2^15
        long kraft = 0;
        for (var length = 1; length <= MaxCodeLength; length++)
        {
            kraft += (long)counts[length] << (MaxCodeLength - length);
        }

        if (kraft != 1L << MaxCodeLength)
            throw Invalid("A WebP lossless prefix code is not complete (its code lengths do not describe a full binary tree).");

        Span<int> nextCode = stackalloc int[MaxCodeLength + 1];
        var code = 0;
        for (var length = 1; length <= MaxCodeLength; length++)
        {
            code = (code + counts[length - 1]) << 1;
            nextCode[length] = code;
        }

        nextCode[0] = 0;
        var rootBits = Math.Min(maxLength, MaxRootBits);
        var rootSize = 1 << rootBits;

        // First pass: the size of the second-level table of each root prefix (the longest code sharing it)
        Span<byte> subBits = stackalloc byte[1 << MaxRootBits];
        subBits[..rootSize].Clear();
        if (maxLength > rootBits)
        {
            Span<int> next = stackalloc int[MaxCodeLength + 1];
            nextCode.CopyTo(next);
            for (var symbol = 0; symbol < codeLengths.Length; symbol++)
            {
                int length = codeLengths[symbol];
                if (length == 0)
                    continue;

                var reversed = Reverse(next[length]++, length);
                if (length > rootBits)
                {
                    var prefix = reversed & (rootSize - 1);
                    subBits[prefix] = (byte)Math.Max(subBits[prefix], length - rootBits);
                }
            }
        }

        var total = rootSize;
        Span<int> subOffsets = stackalloc int[1 << MaxRootBits];
        for (var prefix = 0; prefix < rootSize; prefix++)
        {
            if (subBits[prefix] != 0)
            {
                subOffsets[prefix] = total;
                total += 1 << subBits[prefix];
            }
        }

        var baseOffset = Allocate(total);
        var table = Entries.Slice(baseOffset, total);
        for (var prefix = 0; prefix < rootSize; prefix++)
        {
            if (subBits[prefix] != 0)
            {
                table[prefix] = ((uint)(rootBits + subBits[prefix]) << 16) | (uint)subOffsets[prefix];
            }
        }

        for (var symbol = 0; symbol < codeLengths.Length; symbol++)
        {
            int length = codeLengths[symbol];
            if (length == 0)
                continue;

            var reversed = Reverse(nextCode[length]++, length);
            if (length <= rootBits)
            {
                var entry = ((uint)length << 16) | (uint)symbol;
                for (var index = reversed; index < rootSize; index += 1 << length)
                {
                    table[index] = entry;
                }
            }
            else
            {
                var prefix = reversed & (rootSize - 1);
                var bits = subBits[prefix];
                var subLength = length - rootBits;
                var entry = ((uint)subLength << 16) | (uint)symbol;
                var sub = table.Slice(subOffsets[prefix], 1 << bits);
                for (var index = reversed >> rootBits; index < sub.Length; index += 1 << subLength)
                {
                    sub[index] = entry;
                }
            }
        }

        return new Vp8LPrefixCode(baseOffset, rootBits);
    }

    /// <summary>Reads one symbol.</summary>
    /// <exception cref="InvalidImageContentException">The bitstream ends inside the code.</exception>
    public int ReadSymbol(Vp8LBitReader reader, Vp8LPrefixCode code) => ReadSymbol(reader, Entries, code);

    /// <summary>Gets the table entries (valid until the next <see cref="Build"/>), for loops reading many symbols with <see cref="ReadSymbol(Vp8LBitReader, ReadOnlySpan{uint}, Vp8LPrefixCode)"/>.</summary>
    public ReadOnlySpan<uint> GetEntries() => Entries;

    /// <summary>Reads one symbol with the entries of <see cref="GetEntries"/>.</summary>
    /// <exception cref="InvalidImageContentException">The bitstream ends inside the code.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadSymbol(Vp8LBitReader reader, ReadOnlySpan<uint> entries, Vp8LPrefixCode code)
    {
        var rootBits = code.RootBits;
        var peek = reader.PeekBits(MaxCodeLength);
        var entry = entries[code.Offset + (int)(peek & ((1u << rootBits) - 1))];
        var bits = (int)(entry >> 16);
        if (bits <= rootBits)
        {
            reader.Skip(bits);
            return (int)(entry & 0xFFFF);
        }

        var subBits = bits - rootBits;
        entry = entries[code.Offset + (int)(entry & 0xFFFF) + (int)((peek >> rootBits) & ((1u << subBits) - 1))];
        reader.Skip(rootBits + (int)(entry >> 16));
        return (int)(entry & 0xFFFF);
    }

    public void Dispose()
    {
        _arena?.Dispose();
        _arena = null;
    }

    private Span<uint> Entries => unsafe(MemoryMarshal.Cast<byte, uint>(_arena!.RawBuffer.AsSpan()));

    private static int Reverse(int code, int length)
    {
        var result = 0;
        for (var i = 0; i < length; i++)
        {
            result = (result << 1) | (code & 1);
            code >>= 1;
        }

        return result;
    }

    private int Allocate(int count)
    {
        var required = (long)_used + count;
        var capacity = _arena is null ? 0 : _arena.Capacity / sizeof(uint);
        if (required > capacity)
        {
            var newCapacity = Math.Max(Math.Max(InitialCapacity, required), (long)capacity * 2);
            if (newCapacity * sizeof(uint) > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(_scope.Limits);

            var larger = _scope.Rent((int)(newCapacity * sizeof(uint)), AllocationKind.DecoderState, clear: false);
            if (_arena is not null)
            {
                _arena.RawBuffer.AsSpan(0, _used * sizeof(uint)).CopyTo(larger.RawBuffer);
                _arena.Dispose();
            }

            _arena = larger;
        }

        var offset = _used;
        _used = (int)required;
        return offset;
    }

    private static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.WebP);
}
