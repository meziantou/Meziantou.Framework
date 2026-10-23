using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>
/// The arithmetic decoder of JBIG2 (T.88, Annex E), which is the MQ decoder of JPEG 2000: the one of the JPEG 2000 codec
/// of Meziantou.Framework.Imaging. A context is one byte (its state and its more probable symbol), all 0 at first. The
/// integer procedures of Annex A decode with it.
/// </summary>
internal sealed class Jbig2ArithmeticDecoder
{
    private Jpeg2000MqDecoder _decoder;

    public Jbig2ArithmeticDecoder(byte[] data, int offset, int length)
    {
        _decoder.Initialize(data, offset, length);
    }

    public int Decode(byte[] contexts, int index) => _decoder.Decode(ref contexts[index]);

    /// <summary>Decodes an integer with the procedure of A.2 (IAx); <see langword="null"/> for the out-of-band value.</summary>
    public int? DecodeInteger(byte[] contexts)
    {
        var previous = 1;
        int Bit()
        {
            var bit = Decode(contexts, previous);
            previous = previous < 256 ? (previous << 1) | bit : (((previous << 1) | bit) & 511) | 256;
            return bit;
        }

        int Bits(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                value = (value << 1) | Bit();
            }

            return value;
        }

        var sign = Bit();
        long result;
        if (Bit() == 0)
        {
            result = Bits(2);
        }
        else if (Bit() == 0)
        {
            result = Bits(4) + 4;
        }
        else if (Bit() == 0)
        {
            result = Bits(6) + 20;
        }
        else if (Bit() == 0)
        {
            result = Bits(8) + 84;
        }
        else if (Bit() == 0)
        {
            result = Bits(12) + 340;
        }
        else
        {
            result = (uint)Bits(32) + 4436L;
        }

        if (sign == 1 && result == 0)
            return null;

        if (result > int.MaxValue)
            throw new InvalidPdfContentException("A JBIG2 integer is out of range");

        return sign == 1 ? -(int)result : (int)result;
    }

    /// <summary>Decodes a symbol identifier with the procedure of A.3 (IAID): <paramref name="codeLength"/> bits.</summary>
    public int DecodeSymbolId(byte[] contexts, int codeLength)
    {
        var previous = 1;
        for (var i = 0; i < codeLength; i++)
        {
            previous = (previous << 1) | Decode(contexts, previous);
        }

        return previous - (1 << codeLength);
    }
}
