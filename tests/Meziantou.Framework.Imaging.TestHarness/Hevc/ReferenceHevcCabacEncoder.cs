namespace Meziantou.Framework.Imaging.TestHarness.Hevc;

/// <summary>
/// An arithmetic encoder for the tests of the HEVC arithmetic decoder (Rec. ITU-T H.265, clause 9.3.5 "Arithmetic encoding
/// process", which is informative): it writes the bins that a test gives it, each one with a context variable, in bypass
/// mode or before termination. It shares no code with the library; its tables are transcribed from Tables 9-52 and 9-53 of
/// the edition 01/2026.
/// </summary>
public sealed class ReferenceHevcCabacEncoder
{
    private readonly List<byte> _bytes = [];
    private int _currentByte;
    private int _bitCount;
    private int _ivlLow;
    private int _ivlCurrRange = 510;
    private bool _firstBitFlag = true;
    private int _bitsOutstanding;

    private static ReadOnlySpan<byte> RangeTabLps =>
    [
        128, 176, 208, 240, 128, 167, 197, 227, 128, 158, 187, 216, 123, 150, 178, 205,
        116, 142, 169, 195, 111, 135, 160, 185, 105, 128, 152, 175, 100, 122, 144, 166,
        95, 116, 137, 158, 90, 110, 130, 150, 85, 104, 123, 142, 81, 99, 117, 135,
        77, 94, 111, 128, 73, 89, 105, 122, 69, 85, 100, 116, 66, 80, 95, 110,
        62, 76, 90, 104, 59, 72, 86, 99, 56, 69, 81, 94, 53, 65, 77, 89,
        51, 62, 73, 85, 48, 59, 69, 80, 46, 56, 66, 76, 43, 53, 63, 72,
        41, 50, 59, 69, 39, 48, 56, 65, 37, 45, 54, 62, 35, 43, 51, 59,
        33, 41, 48, 56, 32, 39, 46, 53, 30, 37, 43, 50, 29, 35, 41, 48,
        27, 33, 39, 45, 26, 31, 37, 43, 24, 30, 35, 41, 23, 28, 33, 39,
        22, 27, 32, 37, 21, 26, 30, 35, 20, 24, 29, 33, 19, 23, 27, 31,
        18, 22, 26, 30, 17, 21, 25, 28, 16, 20, 23, 27, 15, 19, 22, 25,
        14, 18, 21, 24, 14, 17, 20, 23, 13, 16, 19, 22, 12, 15, 18, 21,
        12, 14, 17, 20, 11, 14, 16, 19, 11, 13, 15, 18, 10, 12, 15, 17,
        10, 12, 14, 16, 9, 11, 13, 15, 9, 11, 12, 14, 8, 10, 12, 14,
        8, 9, 11, 13, 7, 9, 11, 12, 7, 9, 10, 12, 7, 8, 10, 11,
        6, 8, 9, 11, 6, 7, 9, 10, 6, 7, 8, 9, 2, 2, 2, 2,
    ];

    private static ReadOnlySpan<byte> TransIdxLps =>
    [
        0, 0, 1, 2, 2, 4, 4, 5, 6, 7, 8, 9, 9, 11, 11, 12,
        13, 13, 15, 15, 16, 16, 18, 18, 19, 19, 21, 21, 22, 22, 23, 24,
        24, 25, 26, 26, 27, 27, 28, 29, 29, 30, 30, 30, 31, 32, 32, 33,
        33, 33, 34, 34, 35, 35, 35, 36, 36, 36, 37, 37, 37, 38, 38, 63,
    ];

    private static ReadOnlySpan<byte> TransIdxMps =>
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
        17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
        33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48,
        49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 62, 63,
    ];

    /// <summary>Gets the number of bits written so far.</summary>
    public long BitCount => ((long)_bytes.Count * 8) + _bitCount;

    /// <summary>
    /// Gets the initial state of a context variable (clause 9.3.2.2): <c>pStateIdx</c> in the bits 1 to 6 and
    /// <c>valMps</c> in the bit 0.
    /// </summary>
    /// <param name="initValue">The value of <c>initValue</c> of the context variable.</param>
    /// <param name="sliceQpY"><c>SliceQpY</c>.</param>
    /// <returns>The state.</returns>
    public static byte GetInitialState(int initValue, int sliceQpY)
    {
        var slopeIdx = initValue >> 4;
        var offsetIdx = initValue & 15;
        var m = (slopeIdx * 5) - 45;
        var n = (offsetIdx << 3) - 16;
        var preCtxState = Math.Clamp(((m * Math.Clamp(sliceQpY, 0, 51)) >> 4) + n, 1, 126);
        var valMps = preCtxState <= 63 ? 0 : 1;
        var pStateIdx = valMps is 1 ? preCtxState - 64 : 63 - preCtxState;
        return (byte)((pStateIdx << 1) | valMps);
    }

    /// <summary>Encodes a bin with a context variable, which is updated (clause 9.3.5.3).</summary>
    /// <param name="state">The context variable, as <see cref="GetInitialState"/> returns it.</param>
    /// <param name="binVal">The bin: 0 or 1.</param>
    public void EncodeDecision(ref byte state, int binVal)
    {
        var pStateIdx = state >> 1;
        var valMps = state & 1;
        var qRangeIdx = (_ivlCurrRange >> 6) & 3;
        int ivlLpsRange = RangeTabLps[(pStateIdx * 4) + qRangeIdx];
        _ivlCurrRange -= ivlLpsRange;
        if (binVal != valMps)
        {
            _ivlLow += _ivlCurrRange;
            _ivlCurrRange = ivlLpsRange;
            if (pStateIdx is 0)
            {
                valMps = 1 - valMps;
            }

            pStateIdx = TransIdxLps[pStateIdx];
        }
        else
        {
            pStateIdx = TransIdxMps[pStateIdx];
        }

        state = (byte)((pStateIdx << 1) | valMps);
        Renormalize();
    }

    /// <summary>Encodes a bin in bypass mode (clause 9.3.5.5).</summary>
    /// <param name="binVal">The bin: 0 or 1.</param>
    public void EncodeBypass(int binVal)
    {
        _ivlLow <<= 1;
        if (binVal is not 0)
        {
            _ivlLow += _ivlCurrRange;
        }

        if (_ivlLow >= 1024)
        {
            PutBit(1);
            _ivlLow -= 1024;
        }
        else if (_ivlLow < 512)
        {
            PutBit(0);
        }
        else
        {
            _ivlLow -= 512;
            _bitsOutstanding++;
        }
    }

    /// <summary>
    /// Encodes a bin before termination (clause 9.3.5.6). A bin equal to 1 terminates the encoding: the codeword is
    /// flushed, and its last bit, which is 1, is the stop bit or the alignment bit of what follows.
    /// </summary>
    /// <param name="binVal">The bin: 0 or 1.</param>
    public void EncodeTerminate(int binVal)
    {
        _ivlCurrRange -= 2;
        if (binVal is 0)
        {
            Renormalize();
            return;
        }

        // The flushing at termination
        _ivlLow += _ivlCurrRange;
        _ivlCurrRange = 2;
        Renormalize();
        PutBit((_ivlLow >> 9) & 1);
        WriteBits(((_ivlLow >> 7) & 3) | 1, 2);
    }

    /// <summary>Writes bits that are not arithmetic coded, after a termination: alignment bits, PCM samples.</summary>
    /// <param name="value">The bits, the first one being the most significant.</param>
    /// <param name="count">The number of bits.</param>
    public void WriteBits(int value, int count)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            _currentByte = (_currentByte << 1) | ((value >> i) & 1);
            if (++_bitCount is 8)
            {
                _bytes.Add((byte)_currentByte);
                _currentByte = 0;
                _bitCount = 0;
            }
        }
    }

    /// <summary>Writes zero bits up to the next byte.</summary>
    public void AlignToByte()
    {
        while (_bitCount is not 0)
        {
            WriteBits(0, 1);
        }
    }

    /// <summary>Starts a new arithmetic codeword at the current position, which is a byte (clause 9.3.5.2).</summary>
    public void Restart()
    {
        if (_bitCount is not 0)
            throw new InvalidOperationException("An arithmetic codeword starts on a byte.");

        _ivlLow = 0;
        _ivlCurrRange = 510;
        _firstBitFlag = true;
        _bitsOutstanding = 0;
    }

    /// <summary>Gets the bytes written so far; the last one is completed with zero bits.</summary>
    /// <returns>The bytes.</returns>
    public byte[] ToArray()
    {
        var bytes = new List<byte>(_bytes);
        if (_bitCount is not 0)
        {
            bytes.Add((byte)(_currentByte << (8 - _bitCount)));
        }

        return [.. bytes];
    }

    // Clause 9.3.5.4
    private void Renormalize()
    {
        while (_ivlCurrRange < 256)
        {
            if (_ivlLow < 256)
            {
                PutBit(0);
            }
            else if (_ivlLow >= 512)
            {
                _ivlLow -= 512;
                PutBit(1);
            }
            else
            {
                _ivlLow -= 256;
                _bitsOutstanding++;
            }

            _ivlCurrRange <<= 1;
            _ivlLow <<= 1;
        }
    }

    // PutBit( B ): the first bit of a codeword is not written, and the bits whose value depended on a carry follow
    private void PutBit(int bit)
    {
        if (_firstBitFlag)
        {
            _firstBitFlag = false;
        }
        else
        {
            WriteBits(bit, 1);
        }

        while (_bitsOutstanding > 0)
        {
            WriteBits(1 - bit, 1);
            _bitsOutstanding--;
        }
    }
}
