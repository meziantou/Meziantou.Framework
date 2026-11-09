namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The arithmetic encoder of the entropy-coded segments of a scan (ITU-T T.81 section D.1): binary decisions coded into
/// the code register with the adaptive probability estimate of a statistics bin, the estimation state machine of table
/// D.3, and the fixed estimate of one half (section F.1.4.4.2). It is the inverse of <see cref="JpegArithmeticDecoder"/>.
/// </summary>
/// <remarks>
/// <para>
/// A statistics bin is one byte, as for the decoder: the index of its state in table D.3 (bits 0-6) and the sense of its
/// more probable symbol (bit 7). A new bin is zero.
/// </para>
/// <para>
/// The bytes are appended to a buffer, with a zero byte stuffed after each <c>0xFF</c> (section D.1.6). A carry can still
/// change the last byte written to the code register, so bytes of <c>0xFF</c> are kept on a stack until the carry is
/// decided, and zero bytes are kept until a nonzero byte follows them: the final zero bytes of a segment are not written
/// (section D.1.8), as the decoder reads zeros past the end. The procedures are those of libjpeg (<c>jcarith.c</c>).
/// </para>
/// </remarks>
internal sealed class JpegArithmeticEncoder
{
    private byte[] _buffer = new byte[4096];
    private int _count;

    // The code register (C, with three spacer bits above the output byte at bits 19-26) and the interval register (A)
    private int _code;
    private int _interval;

    // The number of shifts before the next byte is ready
    private int _shifts;

    // The output byte that a carry can still increment, or -1 before the first one; then the stacked 0xFF bytes and the
    // zero bytes not written yet
    private int _pending;
    private int _stackedFF;
    private int _stackedZeros;

    public JpegArithmeticEncoder() => Reset();

    /// <summary>Gets the bytes written so far (stuffed), which the caller copies to the output before calling <see cref="ClearOutput"/>.</summary>
    public ReadOnlySpan<byte> Output => _buffer.AsSpan(0, _count);

    /// <summary>Forgets the bytes of <see cref="Output"/>, which the caller wrote.</summary>
    public void ClearOutput() => _count = 0;

    /// <summary>Starts a new entropy-coded segment (figure D.2: the initialization of the encoder).</summary>
    public void Reset()
    {
        _code = 0;
        _interval = 0x10000;
        _shifts = 11;
        _pending = -1;
        _stackedFF = 0;
        _stackedZeros = 0;
    }

    /// <summary>Codes one binary decision with the estimate of a statistics bin, and adapts the bin (T.81 sections D.1.4 and D.1.5).</summary>
    /// <param name="bin">The statistics bin.</param>
    /// <param name="decision">The decision: 0 or 1.</param>
    public void Encode(ref byte bin, int decision)
    {
        var state = bin & 0x7F;
        var mps = bin >> 7;
        var estimate = JpegArithmeticDecoder.Estimates[state];
        _interval -= estimate;
        if (decision != mps)
        {
            // The less probable symbol, coded in the upper sub-interval unless it is the larger one (conditional exchange)
            if (_interval >= estimate)
            {
                _code += _interval;
                _interval = estimate;
            }

            var next = JpegArithmeticDecoder.SwitchesSense(state) ? mps ^ 1 : mps;
            bin = (byte)(JpegArithmeticDecoder.NextAfterLps[state] | (next << 7));
        }
        else
        {
            // The estimate is only revised when the interval is renormalized
            if (_interval >= 0x8000)
                return;

            if (_interval < estimate)
            {
                _code += _interval;
                _interval = estimate;
            }

            bin = (byte)(JpegArithmeticDecoder.NextAfterMps[state] | (mps << 7));
        }

        Renormalize();
    }

    /// <summary>Codes one binary decision with the fixed estimate of one half, whose more probable symbol is 0 (T.81 F.1.4.4.2).</summary>
    /// <param name="decision">The decision: 0 or 1.</param>
    public void EncodeFixed(int decision)
    {
        const int Estimate = JpegArithmeticDecoder.FixedEstimate;
        _interval -= Estimate;
        if (decision != 0)
        {
            if (_interval >= Estimate)
            {
                _code += _interval;
                _interval = Estimate;
            }
        }
        else
        {
            if (_interval >= 0x8000)
                return;

            if (_interval < Estimate)
            {
                _code += _interval;
                _interval = Estimate;
            }
        }

        Renormalize();
    }

    /// <summary>
    /// Ends the entropy-coded segment (T.81 section D.1.8): the code register is set to the value of the interval with the
    /// most trailing zero bits, and its remaining bytes are written, except the final zero bytes.
    /// </summary>
    public void Flush()
    {
        var temp = (_interval - 1 + _code) & unchecked((int)0xFFFF0000);
        _code = temp < _code ? temp + 0x8000 : temp;
        _code <<= _shifts;
        if ((_code & unchecked((int)0xF8000000)) != 0)
        {
            // One final carry
            if (_pending >= 0)
            {
                WriteStackedZeros();
                WriteStuffed(_pending + 1);
            }

            _stackedZeros += _stackedFF;
            _stackedFF = 0;
        }
        else
        {
            if (_pending == 0)
            {
                _stackedZeros++;
            }
            else if (_pending > 0)
            {
                WriteStackedZeros();
                WriteStuffed(_pending);
            }

            WriteStackedFF();
        }

        // The final bytes are written only when they are not zero
        if ((_code & 0x7FFF800) != 0)
        {
            WriteStackedZeros();
            WriteStuffed((_code >> 19) & 0xFF);
            if ((_code & 0x7F800) != 0)
            {
                WriteStuffed((_code >> 11) & 0xFF);
            }
        }

        Reset();
    }

    /// <summary>Appends a marker (an RSTn marker between two segments): the segment must be flushed first.</summary>
    public void WriteMarker(byte marker)
    {
        WriteByte(0xFF);
        WriteByte(marker);
    }

    /// <summary>Renormalization and output (T.81 section D.1.6): a byte is ready every eight shifts.</summary>
    private void Renormalize()
    {
        do
        {
            _interval <<= 1;
            _code <<= 1;
            if (--_shifts == 0)
            {
                var value = _code >> 19;
                if (value > 0xFF)
                {
                    // A carry: it increments the pending byte and turns the stacked 0xFF bytes into zero bytes
                    if (_pending >= 0)
                    {
                        WriteStackedZeros();
                        WriteStuffed(_pending + 1);
                    }

                    _stackedZeros += _stackedFF;
                    _stackedFF = 0;

                    // The spacer bits guarantee that the new byte cannot be 0xFF
                    _pending = value & 0xFF;
                }
                else if (value == 0xFF)
                {
                    // It might still receive a carry
                    _stackedFF++;
                }
                else
                {
                    // No carry can reach the stacked bytes any more
                    if (_pending == 0)
                    {
                        _stackedZeros++;
                    }
                    else if (_pending > 0)
                    {
                        WriteStackedZeros();
                        WriteStuffed(_pending);
                    }

                    WriteStackedFF();
                    _pending = value & 0xFF;
                }

                _code &= 0x7FFFF;
                _shifts += 8;
            }
        }
        while (_interval < 0x8000);
    }

    private void WriteStackedFF()
    {
        if (_stackedFF == 0)
            return;

        WriteStackedZeros();
        for (; _stackedFF > 0; _stackedFF--)
        {
            WriteByte(0xFF);
            WriteByte(0);
        }
    }

    private void WriteStackedZeros()
    {
        for (; _stackedZeros > 0; _stackedZeros--)
        {
            WriteByte(0);
        }
    }

    private void WriteStuffed(int value)
    {
        WriteByte((byte)value);
        if (value == 0xFF)
        {
            WriteByte(0);
        }
    }

    private void WriteByte(byte value)
    {
        if (_count == _buffer.Length)
        {
            Array.Resize(ref _buffer, _buffer.Length * 2);
        }

        _buffer[_count++] = value;
    }
}
