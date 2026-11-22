using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Http;

// The preload list is a large immutable data set: ~95,000 host names that never expire and carry a single
// bit of information each. It is stored in a form that is searched where it is, so loading it copies and
// decompresses nothing: the embedded resource is mapped with the assembly, and the heap it costs is the decoding
// tables and an index of the blocks, a few tens of kilobytes.
//
// The names are stored with their labels reversed ("www.example.com" as "com.example.www") and sorted, so the
// names of a domain are next to each other and share their beginning. They are cut into blocks: the first name
// of a block is stored whole and the others as the length of the prefix they share with the previous name and
// the rest, their suffix. The characters are Huffman coded with one code per previous character, which holds the
// list in less space than Zstandard holds the names. Each suffix is preceded by its length in bits, so a lookup
// binary-searches the first names of the blocks, then walks one block and decodes only the suffixes that can
// match. See tools/Meziantou.Framework.Http.Hsts.Generator, which writes the resource, for its layout.
internal sealed partial class HstsPreloadList
{
    // A host name is at most 253 bytes, so a name the decoder writes on the stack is bounded by it and a resource
    // claiming to hold anything longer is rejected rather than trusted.
    private const int MaxHostNameLength = 253;

    private const int FormatVersion = 1;
    private const string ResourceName = "preload.bin";
    private const string IncludePreloadListSwitchName = "Meziantou.Framework.Http.Hsts.IncludePreloadList";

    // Keeps alive the memory _data points into: the resource stream, or a pinned copy of the resource
    private readonly object _owner;
    private readonly byte* _data;
    private readonly int _length;

    private readonly int _entryCount;
    private readonly int _blockSize;
    private readonly int _blockCount;
    private readonly int _blockOffsetsOffset;
    private readonly int _bitsOffset;
    private readonly int _bitLength;
    private readonly int _flagsOffset;

    // The symbol of each ASCII character, 0 for the characters no name uses, and the character of each symbol.
    // Symbol 0 is the end of a name: it is never coded, and the lookup appends it to the probe.
    private readonly byte[] _symbolOfCharacter;
    private readonly byte[] _characterOfSymbol;
    private readonly byte _dotSymbol;

    // The code of a character depends on the one before it: _symbolDecoders[previous symbol], 0 at the start. The
    // short codes of every context are in one table, indexed by the context and the next bits of the stream.
    private readonly HuffmanDecoder[] _symbolDecoders;
    private readonly ushort[] _symbolFastTable;

    // The bit length of the first name of a block, and the prefix length and bit length of the other names, coded
    // as one symbol so that skipping a name is a single decode
    private readonly HuffmanDecoder _firstNameBitLengthDecoder;
    private readonly HuffmanDecoder _lengthPairDecoder;
    private readonly byte[] _pairPrefixLengths;
    private readonly ushort[] _pairBitLengths;

    // The first symbols of the first name of each block, packed in an integer in their order, so the binary search
    // compares integers and decodes a name only when they are equal
    private readonly ulong[] _blockKeys;
    private readonly int _bitsPerSymbol;
    private readonly int _symbolsPerKey;

    private HstsPreloadList(object owner, byte* data, int length, int expectedEntryCount)
    {
        _owner = owner;
        _data = data;
        _length = length;

        var bytes = unsafe(new ReadOnlySpan<byte>(data, length));
        var reader = new HeaderReader(bytes);
        if (reader.ReadInt32() != FormatVersion)
            throw CreateInvalidResourceException("its format version is not supported");

        _entryCount = reader.ReadInt32();
        if (_entryCount != expectedEntryCount)
            throw CreateInvalidResourceException($"it declares {_entryCount} entries but the generated code expects {expectedEntryCount}");

        _blockSize = reader.ReadInt32();
        MaxLabelCount = reader.ReadInt32();
        if (_blockSize < 1 || MaxLabelCount is < 1 or > (MaxHostNameLength + 1) / 2)
            throw CreateInvalidResourceException("its header is invalid");

        var symbolCount = reader.ReadByte();
        if (symbolCount < 2)
            throw CreateInvalidResourceException("it declares no character");

        _symbolOfCharacter = new byte[128];
        _characterOfSymbol = new byte[symbolCount];
        for (var symbol = 1; symbol < symbolCount; symbol++)
        {
            // Ascending, so that the order of the symbols is the ordinal order of the names, and folded, so
            // that the lookup can fold the probe the same way
            var character = reader.ReadByte();
            if (character is <= (byte)' ' or >= 0x7F or (>= (byte)'A' and <= (byte)'Z') || character <= _characterOfSymbol[symbol - 1])
                throw CreateInvalidResourceException("its characters are invalid");

            _characterOfSymbol[symbol] = character;
            _symbolOfCharacter[character] = (byte)symbol;
        }

        _dotSymbol = _symbolOfCharacter['.'];

        _symbolDecoders = new HuffmanDecoder[symbolCount];
        _symbolFastTable = new ushort[symbolCount << HuffmanDecoder.FastBits];
        for (var context = 0; context < symbolCount; context++)
        {
            // The end of a name is never coded: a name that could decode it would compare equal to the end of the
            // probe and read past it
            var lengths = reader.ReadBytes(symbolCount);
            _symbolDecoders[context] = (lengths[0] is 0 ? HuffmanDecoder.Create(lengths, _symbolFastTable.AsSpan(context << HuffmanDecoder.FastBits, 1 << HuffmanDecoder.FastBits)) : null)
                ?? throw CreateInvalidResourceException("a character code is invalid");
        }

        _firstNameBitLengthDecoder = HuffmanDecoder.Create(reader.ReadBytes(reader.ReadUInt16()), HuffmanDecoder.FastBits)
            ?? throw CreateInvalidResourceException("the bit length code is invalid");

        var pairCount = reader.ReadUInt16();
        _pairPrefixLengths = new byte[pairCount];
        _pairBitLengths = new ushort[pairCount];
        var pairCodeLengths = new byte[pairCount];
        for (var i = 0; i < pairCount; i++)
        {
            _pairPrefixLengths[i] = reader.ReadByte();
            _pairBitLengths[i] = reader.ReadUInt16();
            pairCodeLengths[i] = reader.ReadByte();
        }

        // Most of the pair codes are longer than the character codes, and decoding them is most of what walking a
        // block does, so they get a larger table
        _lengthPairDecoder = HuffmanDecoder.Create(pairCodeLengths, fastBits: 11)
            ?? throw CreateInvalidResourceException("the length pair code is invalid");

        _blockCount = reader.ReadInt32();
        if (_blockCount != (int)(((long)_entryCount + _blockSize - 1) / _blockSize))
            throw CreateInvalidResourceException($"its {_blockCount} blocks do not hold {_entryCount} entries");

        _blockOffsetsOffset = reader.Position;
        var blockOffsets = reader.ReadBytes(checked(_blockCount * sizeof(int)));

        _bitLength = reader.ReadInt32();
        if (_bitLength < 0)
            throw CreateInvalidResourceException("its bit stream length is invalid");

        // The bit stream is followed by 8 bytes of padding, so a decoder can read 8 bytes from any position in it
        _bitsOffset = reader.Position;
        reader.ReadBytes((int)(((long)_bitLength + 7) / 8) + sizeof(ulong));

        _flagsOffset = reader.Position;
        reader.ReadBytes((int)(((long)_entryCount + 7) / 8));
        if (!reader.IsAtEnd)
            throw CreateInvalidResourceException("it contains more data than its header declares");

        // Every name takes at least one bit, so the blocks start in strictly increasing order within the stream.
        // A lookup can then trust any offset it reads.
        var previous = -1;
        for (var i = 0; i < _blockCount; i++)
        {
            var offset = BinaryPrimitives.ReadInt32LittleEndian(blockOffsets[(i * sizeof(int))..]);
            if (offset <= previous || offset >= _bitLength || (i == 0 && offset != 0))
                throw CreateInvalidResourceException("its block offsets are invalid");

            previous = offset;
        }

        _bitsPerSymbol = 32 - BitOperations.LeadingZeroCount((uint)symbolCount - 1);
        _symbolsPerKey = 64 / _bitsPerSymbol;
        _blockKeys = BuildBlockKeys(bytes);
    }

    /// <summary>Gets the instance shared by every collection built with the preload list.</summary>
    /// <remarks>Loading reads the header of the embedded resource and indexes its blocks, which is cheap, but it is still paid only if something asks for it. The data is immutable, so sharing it is safe.</remarks>
    public static HstsPreloadList Shared => SharedHolder.Instance;

    /// <summary>Gets a value indicating whether the preload data is available in this application.</summary>
    /// <remarks>
    /// An application can stop the preload list being loaded at all by setting the
    /// <c>Meziantou.Framework.Http.Hsts.IncludePreloadList</c> feature switch to <see langword="false"/>. The embedded
    /// resource still ships in the assembly either way. See readme.md.
    /// <see cref="FeatureSwitchDefinitionAttribute"/> lets the trimmer substitute the value when the switch is set
    /// with <c>Trim="true"</c>, and drop the code that loads the list.
    /// </remarks>
    [FeatureSwitchDefinition(IncludePreloadListSwitchName)]
    public static bool IsSupported
        => AppContext.TryGetSwitch(IncludePreloadListSwitchName, out var enabled) ? enabled : true;

    /// <summary>Gets the highest number of labels any preloaded host name has.</summary>
    public int MaxLabelCount { get; }

    private ReadOnlySpan<byte> Data => unsafe(new ReadOnlySpan<byte>(_data, _length));

    /// <summary>Looks a host name up. <paramref name="host"/> must be the canonicalized, ASCII form.</summary>
    public bool TryGetValue(ReadOnlySpan<char> host, int labelCount, out bool includeSubdomains)
    {
        includeSubdomains = false;
        if (labelCount < 1 || labelCount > MaxLabelCount || host.Length is 0 or > MaxHostNameLength)
            return false;

        // The probe is coded with the symbols of the names and ends with symbol 0, which is smaller than any
        // character, so the comparisons are comparisons of bytes. A character no name uses means there is nothing
        // to search.
        Span<byte> probe = stackalloc byte[MaxHostNameLength + 1];
        if (!TryEncodeProbe(host, probe))
            return false;

        probe = probe[..(host.Length + 1)];

        // The last block whose first name is not after the probe is the only one that can hold it. The search
        // narrows by halves without branching on the comparisons, which a binary search mispredicts half the time.
        var keys = _blockKeys;
        if (keys.Length == 0)
            return false;

        var probeKey = GetKey(probe);
        var block = 0;
        for (var remaining = keys.Length; remaining > 1; remaining -= remaining / 2)
        {
            var candidate = block + (remaining / 2);
            block = keys[candidate] <= probeKey ? candidate : block;
        }

        if (keys[block] > probeKey)
            return false;

        // Blocks whose first names start with the same symbols as the probe have the same key, and the last of them
        // may start after the probe
        var data = Data;
        while (keys[block] == probeKey && CompareFirstName(data, block, probe) > 0)
        {
            if (block == 0 || keys[block - 1] != probeKey)
                return block > 0 && TryFindInBlock(data, block - 1, probe, out includeSubdomains);

            block--;
        }

        return TryFindInBlock(data, block, probe, out includeSubdomains);
    }

    private int CompareFirstName(ReadOnlySpan<byte> data, int block, ReadOnlySpan<byte> probe)
    {
        var bitPosition = GetBlockOffset(data, block);
        var bitLength = DecodeFirstNameBitLength(ref bitPosition);
        return CompareSuffix(bitPosition, bitPosition + bitLength, start: 0, probe, out _);
    }

    // The names of the block are walked in order, keeping how much of the probe the current name matches. The
    // current name is before the probe and differs from it at that position, so the length of the prefix the next
    // name shares with it says whether that name can be the probe, and most names are skipped by their bit length
    // without decoding them.
    private bool TryFindInBlock(ReadOnlySpan<byte> data, int block, ReadOnlySpan<byte> probe, out bool includeSubdomains)
    {
        includeSubdomains = false;

        var first = block * _blockSize;
        var count = Math.Min(_blockSize, _entryCount - first);
        var bitPosition = GetBlockOffset(data, block);
        var matched = 0;
        for (var i = 0; i < count; i++)
        {
            var (prefixLength, bitLength) = i == 0 ? (0, DecodeFirstNameBitLength(ref bitPosition)) : DecodeLengthPair(ref bitPosition);
            var end = bitPosition + bitLength;

            // The previous name and the probe agree before `matched`, so a name that leaves the previous one
            // earlier is after the probe, and so is every name after it
            if (prefixLength < matched)
                return false;

            // A name that leaves the previous one later is still before the probe, where the previous one was, so
            // it is skipped. One that leaves it where the probe does is compared with the probe from there.
            if (prefixLength == matched)
            {
                var comparison = CompareSuffix(bitPosition, end, prefixLength, probe, out matched);
                if (comparison == 0)
                {
                    includeSubdomains = GetIncludeSubdomains(data, first + i);
                    return true;
                }

                if (comparison > 0)
                    return false;
            }

            bitPosition = end;
        }

        return false;
    }

    // Compares the name whose suffix is the bits from `bitPosition` to `end` with the probe. The name and the probe
    // agree before `start`, where the suffix starts, so the character before the suffix is the probe's. `matched`
    // is the length of the prefix they share.
    private int CompareSuffix(int bitPosition, int end, int start, ReadOnlySpan<byte> probe, out int matched)
    {
        var context = start == 0 ? 0 : probe[start - 1];
        var index = start;
        while (bitPosition < end)
        {
            var symbol = DecodeSymbol(context, ref bitPosition, end);

            // The probe ends with symbol 0, which no name codes, so a name longer than the probe differs from it
            // at the end of the probe at the latest and `index` stays within it
            var expected = probe[index];
            if (symbol != expected)
            {
                matched = index;
                return symbol < expected ? -1 : 1;
            }

            context = symbol;
            index++;
        }

        // The name ends here: it is the probe if the probe does too, and otherwise a prefix of it, before it
        matched = index;
        return probe[index] == 0 ? 0 : -1;
    }

    /// <summary>Enumerates every preloaded entry, materializing the host names on demand.</summary>
    /// <remarks>The entries come out ordered by their labels from the last one, not in the order of their names.</remarks>
    public IEnumerable<(string Host, bool IncludeSubdomains)> GetEntries()
    {
        var name = new byte[MaxHostNameLength];
        var bitPosition = 0;
        var length = 0;
        for (var i = 0; i < _entryCount; i++)
        {
            length = DecodeEntry(i, ref bitPosition, name, length);
            yield return (GetHostName(name.AsSpan(0, length)), GetIncludeSubdomains(Data, i));
        }
    }

    // Decodes the name of entry `index` over the previous one, which `name` holds, and returns its length
    private int DecodeEntry(int index, ref int bitPosition, Span<byte> name, int previousLength)
    {
        var (prefixLength, bitLength) = index % _blockSize == 0 ? (0, DecodeFirstNameBitLength(ref bitPosition)) : DecodeLengthPair(ref bitPosition);
        if (prefixLength > previousLength)
            throw CreateInvalidResourceException("a name shares more with the previous one than its length");

        var end = bitPosition + bitLength;
        var length = DecodeSymbols(ref bitPosition, end, name, prefixLength, name.Length);
        if (bitPosition != end)
            throw CreateInvalidResourceException($"a name is longer than {MaxHostNameLength} bytes");

        return length;
    }

    // Decodes the symbols of a suffix into `name` from `start`, until the end of the suffix or `limit` symbols, and
    // returns the length of the name
    private int DecodeSymbols(ref int bitPosition, int end, Span<byte> name, int start, int limit)
    {
        var context = start == 0 ? 0 : name[start - 1];
        var index = start;
        while (bitPosition < end && index < limit)
        {
            var symbol = DecodeSymbol(context, ref bitPosition, end);
            name[index++] = (byte)symbol;
            context = symbol;
        }

        return index;
    }

    // The first symbols of the first name of each block. Decoding them is most of the cost of loading the list, so
    // this is compiled optimized from the start rather than run once in unoptimized code.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private ulong[] BuildBlockKeys(ReadOnlySpan<byte> data)
    {
        var keys = new ulong[_blockCount];
        Span<byte> name = stackalloc byte[MaxHostNameLength];
        for (var i = 0; i < keys.Length; i++)
        {
            var bitPosition = GetBlockOffset(data, i);
            var bitLength = DecodeFirstNameBitLength(ref bitPosition);
            var end = bitPosition + bitLength;
            var length = DecodeSymbols(ref bitPosition, end, name, start: 0, _symbolsPerKey);
            keys[i] = GetKey(name[..length]);
            if (i > 0 && keys[i] < keys[i - 1])
                throw CreateInvalidResourceException("its names are not sorted");
        }

        return keys;
    }

    // The symbols are packed from the most significant bits, so the keys compare as the names they start, and
    // the symbols after the end of a name are 0 like the end itself
    private ulong GetKey(ReadOnlySpan<byte> name)
    {
        var key = 0UL;
        for (var i = 0; i < _symbolsPerKey; i++)
        {
            key = (key << _bitsPerSymbol) | (i < name.Length ? name[i] : 0UL);
        }

        return key;
    }

    private int DecodeSymbol(int context, ref int bitPosition, int end)
    {
        var window = ReadWindow(bitPosition);
        int symbol;
        int length;
        var entry = _symbolFastTable[(context << HuffmanDecoder.FastBits) | (int)(window >> (64 - HuffmanDecoder.FastBits))];
        if (entry != 0)
        {
            symbol = entry >> 8;
            length = entry & 0xFF;
        }
        else
        {
            symbol = _symbolDecoders[context].DecodeLongCode(window, out length);
        }

        bitPosition = Advance(bitPosition, length, end);
        return symbol;
    }

    private int DecodeFirstNameBitLength(ref int bitPosition)
    {
        var bitLength = _firstNameBitLengthDecoder.Decode(ReadWindow(bitPosition), out var length);
        bitPosition = CheckSuffix(Advance(bitPosition, length, _bitLength), bitLength);
        return bitLength;
    }

    // Walking a block is mostly this
    private (int PrefixLength, int BitLength) DecodeLengthPair(ref int bitPosition)
    {
        var pair = _lengthPairDecoder.Decode(ReadWindow(bitPosition), out var length);
        var bitLength = _pairBitLengths[pair];
        bitPosition = CheckSuffix(Advance(bitPosition, length, _bitLength), bitLength);
        return (_pairPrefixLengths[pair], bitLength);
    }

    // The suffix that follows must fit in the stream, so the caller can jump past it
    private int CheckSuffix(int bitPosition, int bitLength)
    {
        if (bitLength > _bitLength - bitPosition)
            throw CreateInvalidResourceException("a name runs past the end of its bit stream");

        return bitPosition;
    }

    // The next 64 bits of the stream from `position`, of which at least 57 are valid: enough for any code. The
    // positions never pass the end of the stream, which the 8 bytes of padding follow, so the read stays within
    // the resource.
    private ulong ReadWindow(int position)
    {
        var value = unsafe(Unsafe.ReadUnaligned<ulong>(_data + _bitsOffset + (position >> 3)));
        if (BitConverter.IsLittleEndian)
        {
            value = BinaryPrimitives.ReverseEndianness(value);
        }

        return value << (position & 7);
    }

    private static int Advance(int position, int length, int end)
    {
        position += length;
        if (position > end)
            throw CreateInvalidResourceException("a code runs past the end of its name");

        return position;
    }

    // Writes the symbols of the host name with its labels reversed, and the end of the name
    private bool TryEncodeProbe(ReadOnlySpan<char> host, Span<byte> probe)
    {
        var written = 0;
        var end = host.Length;
        for (var i = host.Length - 1; i >= -1; i--)
        {
            if (i >= 0 && host[i] != '.')
                continue;

            foreach (var c in host[(i + 1)..end])
            {
                var folded = c is >= 'A' and <= 'Z' ? (char)(c | 0x20) : c;
                if (folded >= _symbolOfCharacter.Length)
                    return false;

                var symbol = _symbolOfCharacter[folded];
                if (symbol == 0)
                    return false;

                probe[written++] = symbol;
            }

            if (i >= 0)
            {
                if (_dotSymbol == 0)
                    return false;

                probe[written++] = _dotSymbol;
            }

            end = i;
        }

        probe[written] = 0;
        return true;
    }

    private string GetHostName(ReadOnlySpan<byte> name)
    {
        Span<char> host = stackalloc char[name.Length];
        var written = 0;
        var end = name.Length;
        for (var i = name.Length - 1; i >= -1; i--)
        {
            if (i >= 0 && name[i] != _dotSymbol)
                continue;

            foreach (var symbol in name[(i + 1)..end])
            {
                host[written++] = (char)_characterOfSymbol[symbol];
            }

            if (i >= 0)
            {
                host[written++] = '.';
            }

            end = i;
        }

        return new string(host);
    }

    private int GetBlockOffset(ReadOnlySpan<byte> data, int block)
        => BinaryPrimitives.ReadInt32LittleEndian(data[(_blockOffsetsOffset + (block * sizeof(int)))..]);

    private bool GetIncludeSubdomains(ReadOnlySpan<byte> data, int index)
        => (data[_flagsOffset + (index >> 3)] & (1 << (index & 7))) != 0;

    private static InvalidOperationException CreateInvalidResourceException(string reason)
        => new($"The embedded resource '{ResourceName}' is invalid: {reason}.");

    // The resource is mapped with the assembly, so the stream gives the address of its bytes and the list is read
    // there, for as long as the assembly is loaded: the stream is kept only to tie its lifetime to this instance.
    // A stream that does not give an address is copied once, to memory the GC does not move.
    private static HstsPreloadList Load()
    {
        var stream = typeof(HstsPreloadList).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource '{ResourceName}' is missing from the assembly.");

        if (stream is UnmanagedMemoryStream unmanaged)
        {
            unsafe
            {
                return new HstsPreloadList(unmanaged, unmanaged.PositionPointer, checked((int)(unmanaged.Length - unmanaged.Position)), ExpectedEntryCount);
            }
        }

        using (stream)
        {
            var copy = GC.AllocateArray<byte>(checked((int)stream.Length), pinned: true);
            stream.ReadExactly(copy);
            return FromPinnedArray(copy, ExpectedEntryCount);
        }
    }

    /// <summary>Reads a list from a copy of <paramref name="data"/>, in the format of the embedded resource.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="data"/> is not a valid list of <paramref name="expectedEntryCount"/> entries.</exception>
    internal static HstsPreloadList Create(ReadOnlySpan<byte> data, int expectedEntryCount)
    {
        var copy = GC.AllocateArray<byte>(data.Length, pinned: true);
        data.CopyTo(copy);
        return FromPinnedArray(copy, expectedEntryCount);
    }

    // The array is allocated on the pinned object heap and kept alive by the instance, so its address stays valid
    // for as long as the instance reads it
    private static HstsPreloadList FromPinnedArray(byte[] data, int expectedEntryCount)
        => new(data, unsafe((byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(data))), data.Length, expectedEntryCount);

    // A nested type so the data is loaded the first time it is used and not when the containing type is
    // first touched for any other reason
    private static class SharedHolder
    {
        public static readonly HstsPreloadList Instance = Load();
    }

    private ref struct HeaderReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;

        public int Position { get; private set; }

        public readonly bool IsAtEnd => Position == _data.Length;

        public byte ReadByte() => ReadBytes(1)[0];

        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(sizeof(ushort)));

        public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(sizeof(int)));

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0 || count > _data.Length - Position)
                throw CreateInvalidResourceException("it is truncated");

            var result = _data.Slice(Position, count);
            Position += count;
            return result;
        }
    }

    // A canonical Huffman code, decoded from a 64-bit window of the stream: the codes of up to FastBits bits are
    // looked up in a table indexed by the next FastBits bits, and the longer ones by comparing the window with the
    // first code of each length.
    private sealed class HuffmanDecoder
    {
        // The size of the table of the character codes, which all the contexts share
        public const int FastBits = 8;
        private const int MaxCodeLength = 32;

        private readonly int _fastBits;

        // The symbol in the high bits and the code length in the low byte, 0 when the code is longer than FastBits.
        // Null for a code whose table is kept by the caller.
        private readonly uint[]? _fast;
        private readonly uint[] _firstCode;
        private readonly int[] _count;
        private readonly int[] _firstIndex;
        private readonly ushort[] _symbols;

        private HuffmanDecoder(int fastBits, uint[]? fast, uint[] firstCode, int[] count, int[] firstIndex, ushort[] symbols, int maxLength)
        {
            _fastBits = fastBits;
            _fast = fast;
            _firstCode = firstCode;
            _count = count;
            _firstIndex = firstIndex;
            _symbols = symbols;
            MaxLength = maxLength;
        }

        /// <summary>Gets the length of the longest code.</summary>
        public int MaxLength { get; }

        /// <summary>Builds the decoder of the code whose length is <paramref name="lengths"/>[symbol], 0 for the symbols that have none.</summary>
        /// <returns>The decoder, or <see langword="null"/> if the lengths do not describe a prefix code.</returns>
        public static HuffmanDecoder? Create(ReadOnlySpan<byte> lengths, int fastBits)
            => Create(lengths, fastBits, new uint[1 << fastBits], fastTable: default);

        /// <summary>Builds the decoder of a code whose symbols fit in a byte, writing the table of its short codes to <paramref name="fastTable"/> rather than keeping it.</summary>
        public static HuffmanDecoder? Create(ReadOnlySpan<byte> lengths, Span<ushort> fastTable)
            => lengths.Length <= byte.MaxValue + 1 ? Create(lengths, FastBits, fast: null, fastTable) : null;

        private static HuffmanDecoder? Create(ReadOnlySpan<byte> lengths, int fastBits, uint[]? fast, Span<ushort> fastTable)
        {
            var count = new int[MaxCodeLength + 1];
            var maxLength = 0;
            foreach (var length in lengths)
            {
                if (length > MaxCodeLength)
                    return null;

                count[length]++;
                maxLength = Math.Max(maxLength, length);
            }

            count[0] = 0;

            // The codes of a length follow the last code of the length before, shifted. More codes than a length
            // has room for would make two symbols share a code.
            var firstCode = new uint[MaxCodeLength + 1];
            var firstIndex = new int[MaxCodeLength + 1];
            var code = 0UL;
            var index = 0;
            for (var length = 1; length <= MaxCodeLength; length++)
            {
                if (code + (ulong)count[length] > 1UL << length)
                    return null;

                firstCode[length] = (uint)code;
                firstIndex[length] = index;
                index += count[length];
                code = (code + (ulong)count[length]) << 1;
            }

            var symbols = new ushort[index];
            var next = (int[])firstIndex.Clone();
            for (var length = 1; length <= MaxCodeLength; length++)
            {
                for (var symbol = 0; symbol < lengths.Length; symbol++)
                {
                    if (lengths[symbol] != length)
                        continue;

                    var rank = next[length]++;
                    symbols[rank] = (ushort)symbol;
                    if (length <= fastBits)
                    {
                        var symbolCode = (int)firstCode[length] + (rank - firstIndex[length]);
                        var start = symbolCode << (fastBits - length);
                        var entry = ((uint)symbol << 8) | (uint)length;
                        if (fast is null)
                        {
                            fastTable.Slice(start, 1 << (fastBits - length)).Fill((ushort)entry);
                        }
                        else
                        {
                            fast.AsSpan(start, 1 << (fastBits - length)).Fill(entry);
                        }
                    }
                }
            }

            return new HuffmanDecoder(fastBits, fast, firstCode, count, firstIndex, symbols, maxLength);
        }

        /// <summary>Decodes the symbol at the start of <paramref name="window"/>, for a decoder that keeps its table of short codes.</summary>
        public int Decode(ulong window, out int length)
        {
            Debug.Assert(_fast is not null);
            var entry = _fast[(int)(window >> (64 - _fastBits))];
            if (entry != 0)
            {
                length = (int)(entry & 0xFF);
                return (int)(entry >> 8);
            }

            return DecodeLongCode(window, out length);
        }

        /// <summary>Decodes the symbol at the start of <paramref name="window"/>, knowing its code is longer than the ones of the table.</summary>
        public int DecodeLongCode(ulong window, out int length)
        {
            for (length = _fastBits + 1; length <= MaxLength; length++)
            {
                var offset = (uint)(window >> (64 - length)) - _firstCode[length];
                if (offset < (uint)_count[length])
                    return _symbols[_firstIndex[length] + (int)offset];
            }

            throw CreateInvalidResourceException("it contains a code that is not in its table");
        }
    }
}
