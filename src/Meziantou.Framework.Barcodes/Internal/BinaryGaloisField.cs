namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// GF(2^m) arithmetic and Reed-Solomon encoding for the symbologies whose generator polynomials have the roots
/// α^1 to α^n: Data Matrix and Aztec. (QR codes use <see cref="GaloisField"/>, whose roots start at α^0.)
/// </summary>
internal sealed class BinaryGaloisField
{
    private readonly int[] _exp;
    private readonly int[] _log;
    private readonly int[]?[] _generators;

    private BinaryGaloisField(int size, int primitivePolynomial)
    {
        Size = size;
        _exp = new int[size];
        _log = new int[size];
        var value = 1;
        for (var i = 0; i < size - 1; i++)
        {
            _exp[i] = value;
            _log[value] = i;
            value <<= 1;
            if (value >= size)
            {
                value ^= primitivePolynomial;
            }
        }

        _generators = new int[]?[size];
    }

    /// <summary>GF(16), x^4 + x + 1: the mode message of Aztec symbols.</summary>
    public static BinaryGaloisField Aztec16 { get; } = new(16, 0x13);

    /// <summary>GF(64), x^6 + x + 1: Aztec symbols of 1 or 2 layers.</summary>
    public static BinaryGaloisField Aztec64 { get; } = new(64, 0x43);

    /// <summary>GF(256), x^8 + x^5 + x^3 + x^2 + 1: Data Matrix, and Aztec symbols of 3 to 8 layers.</summary>
    public static BinaryGaloisField Field256 { get; } = new(256, 0x12D);

    /// <summary>GF(1024), x^10 + x^3 + 1: Aztec symbols of 9 to 22 layers.</summary>
    public static BinaryGaloisField Aztec1024 { get; } = new(1024, 0x409);

    /// <summary>GF(4096), x^12 + x^6 + x^5 + x^3 + 1: Aztec symbols of 23 to 32 layers.</summary>
    public static BinaryGaloisField Aztec4096 { get; } = new(4096, 0x1069);

    public int Size { get; }

    public int Multiply(int a, int b)
    {
        if (a == 0 || b == 0)
            return 0;

        return _exp[(_log[a] + _log[b]) % (Size - 1)];
    }

    /// <summary>Computes the error correction codewords of <paramref name="data"/>, highest degree first.</summary>
    public void ComputeErrorCorrection(ReadOnlySpan<int> data, Span<int> errorCorrection)
    {
        var generator = GetGenerator(errorCorrection.Length);
        errorCorrection.Clear();
        foreach (var codeword in data)
        {
            var factor = codeword ^ errorCorrection[0];
            errorCorrection[1..].CopyTo(errorCorrection);
            errorCorrection[^1] = 0;
            if (factor == 0)
                continue;

            for (var j = 0; j < errorCorrection.Length; j++)
            {
                errorCorrection[j] ^= Multiply(generator[j + 1], factor);
            }
        }
    }

    // (x - α^1)(x - α^2)...(x - α^degree), coefficients highest degree first
    private int[] GetGenerator(int degree)
    {
        if (degree < _generators.Length && Volatile.Read(ref _generators[degree]) is { } cached)
            return cached;

        var polynomial = new int[] { 1 };
        for (var i = 1; i <= degree; i++)
        {
            var next = new int[polynomial.Length + 1];
            var root = _exp[i % (Size - 1)];
            for (var j = 0; j < polynomial.Length; j++)
            {
                next[j] ^= polynomial[j];
                next[j + 1] ^= Multiply(polynomial[j], root);
            }

            polynomial = next;
        }

        if (degree < _generators.Length)
        {
            Volatile.Write(ref _generators[degree], polynomial);
        }

        return polynomial;
    }
}
