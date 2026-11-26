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

    /// <summary>The length of the buffer a <see cref="SuffixLookup"/> encodes a host name in: the longest name and the end of the probe.</summary>
    public const int SuffixLookupBufferLength = MaxHostNameLength + 1;

    private const int FormatVersion = 1;
    private const int PairFastBits = 11;
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

    // The short pair codes: the bit length of the name in the high 16 bits, then its prefix length and the length
    // of the code, 0 when the code is longer than PairFastBits
    private readonly uint[] _pairFastTable;

    // The first symbols of the first name of each block, packed in an integer in their order, so the binary search
    // compares integers and decodes a name only when they are equal
    private readonly ulong[] _blockKeys;

    // The number of bits the symbols of the key take in the first name of each block, so that comparing a probe that
    // has the same key with the name starts after them, or 0 when they take more bits than a byte counts
    private readonly byte[] _keyBitLengths;
    private readonly int _bitsPerSymbol;
    private readonly int _symbolsPerKey;

    // The number of leading symbols two different keys share, indexed by the leading zeros of their XOR
    private readonly byte[] _commonSymbolCounts;

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
        // block does, so they get a larger table, whose entries hold the lengths of the pair rather than its
        // symbol: the walk then reads one table per name, not three.
        _pairFastTable = new uint[1 << PairFastBits];
        _lengthPairDecoder = HuffmanDecoder.CreateLengthPairDecoder(pairCodeLengths, _pairPrefixLengths, _pairBitLengths, _pairFastTable)
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

        // A key holds as many symbols as fit with the bit that says whether the name goes on
        _bitsPerSymbol = 32 - BitOperations.LeadingZeroCount((uint)symbolCount - 1);
        _symbolsPerKey = 63 / _bitsPerSymbol;
        _keyBitLengths = new byte[_blockCount];
        _blockKeys = BuildBlockKeys(bytes, _keyBitLengths);

        // The symbols of a key fill its low bits, so the leading zeros of two keys XORed say how many symbols they
        // share: all of them when only the lowest bit differs. A table, because the division by the symbol width is
        // not by a constant.
        _commonSymbolCounts = new byte[65];
        var unusedBits = 63 - (_symbolsPerKey * _bitsPerSymbol);
        for (var leadingZeros = 0; leadingZeros < _commonSymbolCounts.Length; leadingZeros++)
        {
            _commonSymbolCounts[leadingZeros] = (byte)(Math.Max(0, leadingZeros - unusedBits) / _bitsPerSymbol);
        }
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
        var lookup = CreateSuffixLookup(host, stackalloc byte[SuffixLookupBufferLength]);
        return lookup.TryGetValue(host.Length, labelCount, out includeSubdomains);
    }

    /// <summary>Prepares the lookups of the suffixes of a host name, which must be the canonicalized, ASCII form.</summary>
    /// <param name="host">The host name.</param>
    /// <param name="buffer">The memory the lookups encode the host name in, of <see cref="SuffixLookupBufferLength"/> bytes.</param>
    public SuffixLookup CreateSuffixLookup(ReadOnlySpan<char> host, Span<byte> buffer) => new(this, host, buffer);

    // The probe is coded with the symbols of the names and ends with symbol 0, which is smaller than any character,
    // so the comparisons are comparisons of bytes. The search starts at `lowerBlock`, which must not be after the
    // block that can hold the probe, and leaves it at that block. `extendsNoName` is set when the search proved that
    // no name starts with the probe and goes on, so that no longer suffix of the same host is on the list either.
    private bool TryGetValue(ReadOnlySpan<byte> probe, ref int lowerBlock, out bool includeSubdomains, out bool extendsNoName)
    {
        includeSubdomains = false;
        extendsNoName = false;

        // The last block whose first name is not after the probe is the only one that can hold it. The search
        // narrows by halves without branching on the comparisons, which a binary search mispredicts half the time.
        var keys = _blockKeys;
        if (keys.Length == 0)
            return false;

        var probeKey = GetProbeKey(probe);
        var block = lowerBlock;
        for (var remaining = keys.Length - block; remaining > 1; remaining -= remaining / 2)
        {
            var candidate = block + (remaining / 2);
            block = keys[candidate] <= probeKey ? candidate : block;
        }

        if (keys[block] > probeKey)
            return false;

        // Blocks whose first names start with the same symbols as the probe, and go on like it, have the same key,
        // and the last of them may start after the probe. A key that holds the whole probe is its name. The first
        // name of `lowerBlock` is not after the probe, so this stops there.
        var data = Data;
        while (keys[block] == probeKey && IsLongerThanKey(probe) && CompareFirstName(data, block, probe) > 0)
        {
            if (block == 0)
                return false;

            block--;
        }

        lowerBlock = block;
        return TryFindInBlock(data, block, probe, probeKey, out includeSubdomains, out extendsNoName);
    }

    private int CompareFirstName(ReadOnlySpan<byte> data, int block, ReadOnlySpan<byte> probe)
    {
        var bitPosition = GetBlockOffset(data, block);
        var bitLength = DecodeFirstNameBitLength(ref bitPosition);
        return CompareFirstNameAfterKey(block, bitPosition, bitPosition + bitLength, probe).Comparison;
    }

    // Compares the first name of a block, whose symbols are the bits from `bitPosition` to `end`, with a probe that
    // has the same key and goes on after it like the name: they share the symbols of the key, so the comparison
    // starts after them.
    private (int Comparison, int Matched) CompareFirstNameAfterKey(int block, int bitPosition, int end, ReadOnlySpan<byte> probe)
    {
        var keyBitLength = _keyBitLengths[block];
        return keyBitLength == 0
            ? CompareSuffix(bitPosition, end, start: 0, probe)
            : CompareSuffix(bitPosition + keyBitLength, end, _symbolsPerKey, probe);
    }

    // The names of the block are walked in order, keeping how much of the probe the current name matches. The
    // current name is before the probe and differs from it at that position, so the length of the prefix the next
    // name shares with it says whether that name can be the probe, and most names are skipped by their bit length
    // without decoding them.
    //
    // The names that start with the probe and go on come right after it, so when the first name after the probe
    // leaves it before its end, there are none, which `extendsNoName` reports. A name that leaves the previous one
    // earlier than the probe does leaves the probe at that position too, before its end.
    private bool TryFindInBlock(ReadOnlySpan<byte> data, int block, ReadOnlySpan<byte> probe, ulong probeKey, out bool includeSubdomains, out bool extendsNoName)
    {
        includeSubdomains = false;
        extendsNoName = false;
        var probeLength = probe.Length - 1;

        var first = block * _blockSize;
        var count = Math.Min(_blockSize, _entryCount - first);
        var bitPosition = GetBlockOffset(data, block);
        var firstNameBitLength = DecodeFirstNameBitLength(ref bitPosition);
        var firstNameEnd = bitPosition + firstNameBitLength;
        int matched;

        // The block is not after the probe, so a key that differs from the probe's is before it, and the first
        // symbol where the two keys differ is where the first name leaves the probe: decoding the name would only
        // find the symbols the key already holds, which were decoded when the list was loaded.
        var blockKey = _blockKeys[block];
        Debug.Assert(blockKey <= probeKey);
        if (blockKey != probeKey)
        {
            matched = _commonSymbolCounts[BitOperations.LeadingZeroCount(blockKey ^ probeKey)];
        }
        else if (!IsLongerThanKey(probe))
        {
            // The key holds the whole first name, which is the probe
            includeSubdomains = GetIncludeSubdomains(data, first);
            return true;
        }
        else
        {
            (var comparison, matched) = CompareFirstNameAfterKey(block, bitPosition, firstNameEnd, probe);
            if (comparison == 0)
            {
                includeSubdomains = GetIncludeSubdomains(data, first);
                return true;
            }

            if (comparison > 0)
            {
                extendsNoName = matched < probeLength;
                return false;
            }
        }

        bitPosition = firstNameEnd;
        for (var i = 1; i < count; i++)
        {
            var (prefixLength, bitLength) = DecodeLengthPair(ref bitPosition);
            var end = bitPosition + bitLength;

            // The previous name and the probe agree before `matched`, so a name that leaves the previous one
            // earlier is after the probe, and so is every name after it
            if (prefixLength < matched)
            {
                extendsNoName = true;
                return false;
            }

            // A name that leaves the previous one later is still before the probe, where the previous one was, so
            // it is skipped. One that leaves it where the probe does is compared with the probe from there.
            if (prefixLength == matched)
            {
                (var comparison, matched) = CompareSuffix(bitPosition, end, prefixLength, probe);
                if (comparison == 0)
                {
                    includeSubdomains = GetIncludeSubdomains(data, first + i);
                    return true;
                }

                if (comparison > 0)
                {
                    extendsNoName = matched < probeLength;
                    return false;
                }
            }

            bitPosition = end;
        }

        return false;
    }

    // Compares the name whose suffix is the bits from `bitPosition` to `end` with the probe. The name and the probe
    // agree before `start`, where the suffix starts, so the character before the suffix is the probe's. `Matched`
    // is the length of the prefix they share. Returned rather than an out parameter, which would keep the walk's
    // state in memory and make it reload the fields of the list for every name.
    private (int Comparison, int Matched) CompareSuffix(int bitPosition, int end, int start, ReadOnlySpan<byte> probe)
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
                return (symbol < expected ? -1 : 1, index);

            context = symbol;
            index++;
        }

        // The name ends here: it is the probe if the probe does too, and otherwise a prefix of it, before it
        return (probe[index] == 0 ? 0 : -1, index);
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
    private ulong[] BuildBlockKeys(ReadOnlySpan<byte> data, byte[] keyBitLengths)
    {
        var keys = new ulong[_blockCount];
        Span<byte> name = stackalloc byte[MaxHostNameLength];
        for (var i = 0; i < keys.Length; i++)
        {
            var bitPosition = GetBlockOffset(data, i);
            var bitLength = DecodeFirstNameBitLength(ref bitPosition);
            var start = bitPosition;
            var end = bitPosition + bitLength;
            var length = DecodeSymbols(ref bitPosition, end, name, start: 0, _symbolsPerKey);
            keyBitLengths[i] = bitPosition - start is var keyBitLength and <= byte.MaxValue ? (byte)keyBitLength : (byte)0;
            keys[i] = GetKey(name[..length], isLonger: bitPosition < end);
            if (i > 0 && keys[i] < keys[i - 1])
                throw CreateInvalidResourceException("its names are not sorted");
        }

        return keys;
    }

    // The symbols are packed from the most significant bits, so the keys compare as the names they start, and
    // the symbols after the end of a name are 0 like the end itself. The lowest bit says whether the name goes on
    // after them, so that a name the key holds whole is before the ones that extend it, as in the order of the
    // names: two equal keys are then the same name, or two names that both go on.
    private ulong GetKey(ReadOnlySpan<byte> name, bool isLonger)
    {
        var key = 0UL;
        for (var i = 0; i < _symbolsPerKey; i++)
        {
            key = (key << _bitsPerSymbol) | (i < name.Length ? name[i] : 0UL);
        }

        return (key << 1) | (isLonger ? 1UL : 0UL);
    }

    // The key of a probe, which ends with symbol 0
    private ulong GetProbeKey(ReadOnlySpan<byte> probe)
    {
        return GetKey(probe, isLonger: IsLongerThanKey(probe));
    }

    private bool IsLongerThanKey(ReadOnlySpan<byte> probe)
    {
        return probe.Length - 1 > _symbolsPerKey;
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
        var window = ReadWindow(bitPosition);
        var entry = _pairFastTable[(int)(window >> (64 - PairFastBits))];
        int length;
        int prefixLength;
        int bitLength;
        if (entry != 0)
        {
            length = (int)(entry & 0xFF);
            prefixLength = (int)((entry >> 8) & 0xFF);
            bitLength = (int)(entry >> 16);
        }
        else
        {
            var pair = _lengthPairDecoder.DecodeLongCode(window, out length);
            prefixLength = _pairPrefixLengths[pair];
            bitLength = _pairBitLengths[pair];
        }

        bitPosition = CheckSuffix(Advance(bitPosition, length, _bitLength), bitLength);
        return (prefixLength, bitLength);
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

    /// <summary>The memory a <see cref="SuffixLookup"/> encodes a host name in.</summary>
    /// <remarks>
    /// The caller declares a local of this type rather than a <see langword="stackalloc"/>: a method that has a loop
    /// and a <see langword="stackalloc"/> is compiled fully optimized on its first call, which made the first lookup
    /// of the process about a millisecond slower.
    /// </remarks>
    [InlineArray(SuffixLookupBufferLength)]
    public struct SuffixLookupBuffer
    {
        private byte _element;
    }

    /// <summary>Looks up the suffixes of a host name, from the shortest to the longest.</summary>
    /// <remarks>
    /// The labels of the names are reversed, so the suffixes of a host name, which are its last labels, are the
    /// prefixes of its reversed name: "example.com" is "com.example", the start of "com.example.www". The name is
    /// encoded once, only as far as the longest suffix looked up, and the probe of a suffix is the start of it. A
    /// longer suffix is after a shorter one in the order of the names, so its search starts at the block where the
    /// shorter one was searched, and is not needed at all when the shorter one proved that no name extends it.
    /// </remarks>
    public ref struct SuffixLookup
    {
        private readonly HstsPreloadList _list;
        private readonly ReadOnlySpan<char> _host;
        private readonly Span<byte> _probe;

        // The symbols written so far, the end of the labels of the host that are not written yet (-1 once they all
        // are), and whether a character no name uses, or the length of the names, stopped the encoding there
        private int _encodedLength;
        private int _unencodedEnd;
        private bool _isStopped;

        private int _previousLength;
        private int _previousBlock;

        // The length of a suffix that no name starts with and goes on, so that the longer ones are not searched
        private int _unextendedLength;

        internal SuffixLookup(HstsPreloadList list, ReadOnlySpan<char> host, Span<byte> buffer)
        {
            _list = list;
            _host = host;
            _probe = buffer;
            _unencodedEnd = host.Length;
            _unextendedLength = int.MaxValue;
        }

        /// <summary>Looks up the suffix made of the last <paramref name="length"/> characters of the host name, which must start a label.</summary>
        public bool TryGetValue(int length, int labelCount, out bool includeSubdomains)
        {
            includeSubdomains = false;
            if (labelCount < 1 || labelCount > _list.MaxLabelCount || length is <= 0 or > MaxHostNameLength || length > _unextendedLength || !TryEncode(length))
                return false;

            // The probe ends with symbol 0 where the reversed name goes on with a dot
            var next = _probe[length];
            _probe[length] = 0;
            var block = length > _previousLength ? _previousBlock : 0;
            var found = _list.TryGetValue(_probe[..(length + 1)], ref block, out includeSubdomains, out var extendsNoName);
            _probe[length] = next;

            _previousLength = length;
            _previousBlock = block;
            if (extendsNoName)
            {
                _unextendedLength = length;
            }

            return found;
        }

        // Writes the labels of the host from the last one, separated by dots, until `length` symbols are written
        private bool TryEncode(int length)
        {
            while (_encodedLength < length)
            {
                if (_isStopped || _unencodedEnd < 0)
                    return false;

                if (_unencodedEnd < _host.Length && !TryWrite('.'))
                    return false;

                var labels = _host[.._unencodedEnd];
                var dot = labels.LastIndexOf('.');
                foreach (var c in labels[(dot + 1)..])
                {
                    if (!TryWrite(c))
                        return false;
                }

                _unencodedEnd = dot;
            }

            return true;
        }

        private bool TryWrite(char c)
        {
            var folded = c is >= 'A' and <= 'Z' ? (char)(c | 0x20) : c;
            var symbol = folded < _list._symbolOfCharacter.Length ? _list._symbolOfCharacter[folded] : (byte)0;
            if (symbol == 0 || _encodedLength == MaxHostNameLength)
            {
                // A suffix that holds this character is not on the list, but the shorter ones written so far may be
                _isStopped = true;
                return false;
            }

            _probe[_encodedLength++] = symbol;
            return true;
        }
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
        {
            var fast = new uint[1 << fastBits];
            return Create(lengths, fastBits, fast, fast, narrowFastTable: default);
        }

        /// <summary>Builds the decoder of a code whose symbols fit in a byte, writing the table of its short codes to <paramref name="fastTable"/> rather than keeping it.</summary>
        public static HuffmanDecoder? Create(ReadOnlySpan<byte> lengths, Span<ushort> fastTable)
            => lengths.Length <= byte.MaxValue + 1 ? Create(lengths, FastBits, fast: null, wideFastTable: default, fastTable) : null;

        /// <summary>Builds the decoder of the length pairs, writing to <paramref name="fastTable"/>, whose size sets how many bits it is indexed by, the lengths of each short code's pair rather than the pair.</summary>
        /// <remarks>An entry holds the bit length of the pair in its high 16 bits, then its prefix length and the length of the code.</remarks>
        public static HuffmanDecoder? CreateLengthPairDecoder(ReadOnlySpan<byte> lengths, ReadOnlySpan<byte> prefixLengths, ReadOnlySpan<ushort> bitLengths, uint[] fastTable)
            => Create(lengths, BitOperations.Log2((uint)fastTable.Length), fast: null, fastTable, narrowFastTable: default, prefixLengths, bitLengths);

        private static HuffmanDecoder? Create(ReadOnlySpan<byte> lengths, int fastBits, uint[]? fast, Span<uint> wideFastTable, Span<ushort> narrowFastTable, ReadOnlySpan<byte> pairPrefixLengths = default, ReadOnlySpan<ushort> pairBitLengths = default)
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
                        var entry = pairBitLengths.IsEmpty
                            ? ((uint)symbol << 8) | (uint)length
                            : ((uint)pairBitLengths[symbol] << 16) | ((uint)pairPrefixLengths[symbol] << 8) | (uint)length;
                        if (wideFastTable.IsEmpty)
                        {
                            narrowFastTable.Slice(start, 1 << (fastBits - length)).Fill((ushort)entry);
                        }
                        else
                        {
                            wideFastTable.Slice(start, 1 << (fastBits - length)).Fill(entry);
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
