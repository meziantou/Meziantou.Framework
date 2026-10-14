namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the values of one entropy-coded stream (ISO/IEC 18181-1, section D.3): a token of the cluster of the context,
/// with a prefix code or the ANS decoder, completed into a hybrid integer; and, when LZ77 is enabled, the copies of
/// previous values (section D.3.7).
/// </summary>
internal sealed class JpegXlEntropyDecoder : IDisposable
{
    private const int WindowSize = 1 << 20;
    private const int WindowMask = WindowSize - 1;
    private const int SpecialDistanceCount = 120;

    private readonly JpegXlEntropyCode _code;
    private readonly AllocationScope _scope;

    // What every value reads, taken from the code once: the properties of the code are not read for each value
    private readonly byte[] _contextMap;
    private readonly int _contextMapLength;
    private readonly JpegXlHybridUintConfig[] _configs;
    private readonly bool _usesPrefixCodes;
    private readonly bool _isLz77Enabled;
    private readonly int _logAlphabetSize;
    private readonly int _logBucketSize;
    private readonly byte[]? _aliasTables;
    private readonly int _aliasTableLength;
    private Av1PooledArray<uint>? _window;
    private uint _state;
    private uint _copyCount;
    private uint _copyPosition;
    private uint _decodedCount;

    public JpegXlEntropyDecoder(JpegXlEntropyCode code, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(scope);
        _code = code;
        _scope = scope;
        _contextMap = code.ContextMapBuffer;
        _contextMapLength = code.ContextMap.Length;
        _configs = code.ConfigArray;
        _usesPrefixCodes = code.UsesPrefixCodes;
        _isLz77Enabled = code.IsLz77Enabled;
        _logAlphabetSize = code.LogAlphabetSize;
        _logBucketSize = JpegXlAnsDistribution.LogTableSize - code.LogAlphabetSize;
        _aliasTables = code.AliasTableBuffer;
        _aliasTableLength = code.AliasTableLength;
    }

    // The offsets (in pixels then in rows) of the 120 shortest distances of a two-dimensional stream, as (column + 7) * 16 + row
    private static ReadOnlySpan<byte> SpecialDistances =>
    [
        0x71, 0x80, 0x81, 0x61, 0x72, 0x90, 0x82, 0x62, 0x91, 0x51, 0x92, 0x52,
        0x73, 0xa0, 0x83, 0x63, 0xa1, 0x41, 0x93, 0x53, 0xa2, 0x42, 0x74, 0xb0,
        0x84, 0x64, 0xb1, 0x31, 0xa3, 0x43, 0x94, 0x54, 0xb2, 0x32, 0x75, 0xa4,
        0x44, 0xb3, 0x33, 0xc0, 0x85, 0x65, 0xc1, 0x21, 0x95, 0x55, 0xc2, 0x22,
        0xb4, 0x34, 0xa5, 0x45, 0xc3, 0x23, 0x76, 0xd0, 0x86, 0x66, 0xd1, 0x11,
        0x96, 0x56, 0xd2, 0x12, 0xb5, 0x35, 0xc4, 0x24, 0xa6, 0x46, 0xd3, 0x13,
        0x77, 0xe0, 0x87, 0x67, 0xc5, 0x25, 0xe1, 0x01, 0xb6, 0x36, 0xd4, 0x14,
        0x97, 0x57, 0xe2, 0x02, 0xa7, 0x47, 0xe3, 0x03, 0xc6, 0x26, 0xd5, 0x15,
        0xf0, 0xb7, 0x37, 0xe4, 0x04, 0xf1, 0xf2, 0xd6, 0x16, 0xf3, 0xc7, 0x27,
        0xe5, 0x05, 0xf4, 0xd7, 0x17, 0xe6, 0x06, 0xf5, 0xe7, 0x07, 0xf6, 0xf7,
    ];

    /// <summary>Starts the stream: reads the initial state of the ANS decoder (nothing for prefix codes).</summary>
    public void Begin(JpegXlBitReader reader)
    {
        _copyCount = 0;
        _copyPosition = 0;
        _decodedCount = 0;
        _state = _usesPrefixCodes ? JpegXlAnsDistribution.InitialState : reader.ReadBits(32);
        if (_isLz77Enabled && _window is null)
        {
            _window = Av1PooledArray<uint>.Rent(_scope, WindowSize, clear: false);
        }
    }

    /// <summary>Checks that the ANS decoder ended in its final state, which detects most corrupted streams.</summary>
    /// <exception cref="InvalidImageContentException">The final state is not the expected one.</exception>
    public void EnsureFinalState()
    {
        if (_state != JpegXlAnsDistribution.InitialState)
            throw JpegXlFormat.Invalid("an entropy-coded stream does not end in its final state.");
    }

    /// <summary>Reads the next value of the stream.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="context">The context of the value.</param>
    /// <param name="distanceMultiplier">The width of the rows of a two-dimensional stream, which gives a meaning to the shortest LZ77 distances; 0 otherwise.</param>
    /// <returns>The value.</returns>
    public uint Read(JpegXlBitReader reader, int context, uint distanceMultiplier = 0)
    {
        if (!_isLz77Enabled)
        {
            var cluster = _contextMap.AsSpan(0, _contextMapLength)[context];
            var token = _usesPrefixCodes ? ReadPrefixToken(reader, cluster) : ReadAnsToken(reader, cluster);
            return _configs[cluster].ReadValue(reader, token);
        }

        return ReadWithLz77(reader, context, distanceMultiplier);
    }

    public void Dispose()
    {
        _window?.Dispose();
        _window = null;
    }

    private uint ReadWithLz77(JpegXlBitReader reader, int context, uint distanceMultiplier)
    {
        var window = _window!.Span;
        if (_copyCount == 0)
        {
            var map = _contextMap.AsSpan(0, _contextMapLength);
            var cluster = map[context];
            var token = ReadToken(reader, cluster);
            if (token < _code.Lz77MinSymbol)
            {
                var value = _configs[cluster].ReadValue(reader, token);
                window[(int)(_decodedCount++ & WindowMask)] = value;
                return value;
            }

            // A copy: its length, then its distance with the cluster of the last context
            _copyCount = _code.Lz77LengthConfig.ReadValue(reader, token - _code.Lz77MinSymbol) + _code.Lz77MinLength;
            var distanceCluster = map[^1];
            var distance = _configs[distanceCluster].ReadValue(reader, ReadToken(reader, distanceCluster));
            if (distanceMultiplier == 0)
            {
                distance++;
            }
            else if (distance >= SpecialDistanceCount)
            {
                distance -= SpecialDistanceCount - 1;
            }
            else
            {
                var special = SpecialDistances[(int)distance];
                var offset = (long)((special >> 4) - 7) + ((long)distanceMultiplier * (special & 7));
                distance = (uint)Math.Max(1, offset);
            }

            distance = Math.Min(Math.Min(distance, _decodedCount), WindowSize);
            _copyPosition = _decodedCount - distance;
            if (distance == 0)
            {
                // Nothing was decoded yet: the copy reads zeros
                window[..(int)Math.Min(_copyCount, WindowSize)].Clear();
            }

            // A length that overflowed copies nothing more than its first value
            if (_copyCount < _code.Lz77MinLength)
            {
                _copyCount = 0;
                return 0;
            }
        }

        _copyCount--;
        var copied = window[(int)(_copyPosition++ & WindowMask)];
        window[(int)(_decodedCount++ & WindowMask)] = copied;
        return copied;
    }

    private uint ReadToken(JpegXlBitReader reader, int cluster) => _usesPrefixCodes ? ReadPrefixToken(reader, cluster) : ReadAnsToken(reader, cluster);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private uint ReadPrefixToken(JpegXlBitReader reader, int cluster)
    {
        var table = _code.GetPrefixTable(cluster, out var maxLength);
        return table.IsEmpty ? 0 : JpegXlPrefixCode.ReadSymbol(reader, table, maxLength);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private uint ReadAnsToken(JpegXlBitReader reader, int cluster)
    {
        // ANS (section D.3.3): the low 12 bits of the state select a bucket and a position in it
        var state = _state;
        var index = state & (JpegXlAnsDistribution.TableSize - 1);
        var bucket = (int)(index >> _logBucketSize);
        var position = index & ((1u << _logBucketSize) - 1);
        ref readonly var entry = ref JpegXlEntropyCode.GetAliasTables(_aliasTables!, _aliasTableLength)[(cluster << _logAlphabetSize) + bucket];
        uint symbol;
        uint frequency;
        uint offset;
        if (position < entry.Cutoff)
        {
            symbol = (uint)bucket;
            frequency = entry.Frequency;
            offset = position;
        }
        else
        {
            symbol = entry.Symbol;
            frequency = entry.SymbolFrequency;
            offset = entry.Offset + position;
        }

        state = (frequency * (state >> JpegXlAnsDistribution.LogTableSize)) + offset;
        if (state < 1u << 16)
        {
            state = (state << 16) | reader.ReadBits(16);
        }

        _state = state;
        return symbol;
    }
}
