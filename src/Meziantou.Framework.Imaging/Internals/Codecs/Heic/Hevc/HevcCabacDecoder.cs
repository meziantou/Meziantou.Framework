using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The arithmetic decoding engine of the slice segment data and its context variables (Rec. ITU-T H.265, clauses 9.3.2
/// "Initialization process" and 9.3.4.3 "Arithmetic decoding process").
/// </summary>
/// <remarks>
/// <para>
/// The specification reads the data one bit at a time into <c>ivlOffset</c>. The decoder reads whole bytes ahead instead:
/// <see cref="HevcCabacEngine.Offset"/> holds <c>ivlOffset</c> followed by the <see cref="HevcCabacEngine.ExtraBits"/> bits
/// that the specification has not read yet, so that a renormalization only changes <see cref="HevcCabacEngine.ExtraBits"/>.
/// The position of the specification in the data stays known, which is where the decoding continues after a bin that
/// terminates it (the PCM samples, the next substream).
/// </para>
/// <para>
/// A context variable is one byte: <c>pStateIdx</c> in the bits 1 to 6, <c>valMps</c> in the bit 0. Reading past the end
/// of the data is <see cref="InvalidImageContentException"/>.
/// </para>
/// </remarks>
internal sealed class HevcCabacDecoder
{
    private readonly byte[] _contexts = new byte[HevcContextLayout.Count];
    private byte[] _data = [];
    private int _position;
    private int _end;
    private HevcCabacEngine _engine;

    /// <summary>Gets the context variables, in the layout of <see cref="HevcContextLayout"/>.</summary>
    public Span<byte> Contexts => _contexts;

    /// <summary>
    /// Gets the registers of the arithmetic decoding engine: a loop that decodes many bins copies them to a local and stores
    /// the local back before the decoder is used again.
    /// </summary>
    public ref HevcCabacEngine Engine => ref _engine;

    /// <summary>
    /// Gets the position in the data of the bit that follows the last bit that the arithmetic decoder of the specification
    /// has read.
    /// </summary>
    public long BitPosition => ((long)_position << 3) - _engine.ExtraBits;

    /// <summary>
    /// Initializes the context variables of a slice (clause 9.3.2.2 "Initialization process for context variables").
    /// </summary>
    /// <param name="initType">The initialization type of the slice (Equation 9-7).</param>
    /// <param name="sliceQpY"><c>SliceQpY</c>.</param>
    public void InitializeContexts(int initType, int sliceQpY)
    {
        var initValues = initType switch
        {
            0 => HevcCabacTables.InitValues0,
            1 => HevcCabacTables.InitValues1,
            _ => HevcCabacTables.InitValues2,
        };
        var qp = Math.Clamp(sliceQpY, 0, 51);
        for (var i = 0; i < _contexts.Length; i++)
        {
            // Equations 9-4 to 9-6
            var initValue = initValues[i];
            var slopeIdx = initValue >> 4;
            var offsetIdx = initValue & 15;
            var m = (slopeIdx * 5) - 45;
            var n = (offsetIdx << 3) - 16;
            var preCtxState = Math.Clamp(((m * qp) >> 4) + n, 1, 126);
            var valMps = preCtxState <= 63 ? 0 : 1;
            var pStateIdx = valMps is not 0 ? preCtxState - 64 : 63 - preCtxState;
            _contexts[i] = (byte)((pStateIdx << 1) | valMps);
        }
    }

    /// <summary>
    /// Initializes the arithmetic decoding engine at a byte of the data (clause 9.3.2.6 "Initialization process for the
    /// arithmetic decoding engine").
    /// </summary>
    /// <param name="data">The array that holds the payload of the NAL unit, without emulation prevention bytes.</param>
    /// <param name="position">The position of the first byte to read.</param>
    /// <param name="end">The position of the end of the payload.</param>
    /// <exception cref="InvalidImageContentException">The data are truncated, or start with a value that is not one of an arithmetic codeword.</exception>
    public void Initialize(byte[] data, int position, int end)
    {
        _data = data;
        _position = position;
        _end = end;
        _engine.Range = 510;

        // ivlOffset is read_bits( 9 )
        (_engine.Offset, _engine.ExtraBits) = Refill(0, -9);
        if ((_engine.Offset >> _engine.ExtraBits) >= 510)
            throw HevcErrors.Invalid("the arithmetic codeword of a slice segment starts with a forbidden value.");
    }

    /// <summary>Decodes a bin with a context variable (clause 9.3.4.3.2 "Arithmetic decoding process for a binary decision").</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeDecision(int ctxIdx) => _engine.DecodeDecision(_contexts, ctxIdx, this);

    /// <summary>Decodes a bin without context (clause 9.3.4.3.4 "Bypass decoding process for binary decisions").</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeBypass() => _engine.DecodeBypass(this);

    /// <summary>Decodes <paramref name="count"/> bypass bins, the first one being the most significant bit of the result.</summary>
    public int DecodeBypassBits(int count) => _engine.DecodeBypassBits(count, this);

    /// <summary>
    /// Decodes a bin before termination (clause 9.3.4.3.5 "Decoding process for binary decisions before termination"):
    /// <c>end_of_slice_segment_flag</c>, <c>end_of_subset_one_bit</c> and <c>pcm_flag</c>. After a bin equal to 1, the
    /// arithmetic decoding is terminated and <see cref="BitPosition"/> is the position of the bit that follows the
    /// codeword.
    /// </summary>
    /// <exception cref="InvalidImageContentException">The codeword does not end with a bit equal to 1.</exception>
    public int DecodeTerminate()
    {
        ref var engine = ref _engine;
        engine.Range -= 2;
        var scaledRange = (ulong)engine.Range << engine.ExtraBits;
        if (engine.Offset >= scaledRange)
        {
            // The last bit read into ivlOffset is rbsp_stop_one_bit or alignment_bit_equal_to_one, and the bit that an
            // encoder writes after pcm_flag (clause 9.3.5.6). It is checked in the data: a bypass bin may have changed it
            // in ivlOffset since it was read
            var lastBit = BitPosition - 1;
            if (((_data[(int)(lastBit >> 3)] >> (7 - (int)(lastBit & 7))) & 1) is 0)
                throw HevcErrors.Invalid("an arithmetic codeword does not end with a bit equal to 1.");

            return 1;
        }

        if (engine.Range < 256)
        {
            engine.Range <<= 1;
            if (--engine.ExtraBits < 0)
            {
                (engine.Offset, engine.ExtraBits) = Refill(engine.Offset, engine.ExtraBits);
            }
        }

        return 0;
    }

    /// <summary>Copies the context variables to <paramref name="destination"/> (clause 9.3.2.4, the storage process).</summary>
    public void SaveContexts(Span<byte> destination) => _contexts.CopyTo(destination);

    /// <summary>Copies the context variables from <paramref name="source"/> (clause 9.3.2.5, the synchronization process).</summary>
    public void LoadContexts(ReadOnlySpan<byte> source) => source.CopyTo(_contexts);

    /// <summary>
    /// Reads the bytes that the specification is reading bits of, and the bytes that follow them while there is room: the
    /// registers of the engine are passed and returned by value, so that a local copy of them stays in registers.
    /// </summary>
    /// <param name="offset"><see cref="HevcCabacEngine.Offset"/>.</param>
    /// <param name="extraBits"><see cref="HevcCabacEngine.ExtraBits"/>: negative when the specification reads bits that are not in <paramref name="offset"/> yet.</param>
    /// <exception cref="InvalidImageContentException">The data are truncated.</exception>
    internal (ulong Offset, int ExtraBits) Refill(ulong offset, int extraBits)
    {
        do
        {
            if (_position >= _end)
                throw HevcErrors.Truncated();

            offset = (offset << 8) | _data[_position++];
            extraBits += 8;
        }
        while (extraBits < 0);

        return ReadAhead(offset, extraBits);
    }

    /// <summary>
    /// Reads the bytes that follow the bits of <paramref name="offset"/> while there is room for them and the data do not
    /// end: the registers of the engine are passed and returned by value, like <see cref="Refill"/>.
    /// </summary>
    /// <param name="offset"><see cref="HevcCabacEngine.Offset"/>.</param>
    /// <param name="extraBits"><see cref="HevcCabacEngine.ExtraBits"/>.</param>
    internal (ulong Offset, int ExtraBits) ReadAhead(ulong offset, int extraBits)
    {
        while (extraBits <= 32 && _position < _end)
        {
            offset = (offset << 8) | _data[_position++];
            extraBits += 8;
        }

        return (offset, extraBits);
    }
}
