namespace Meziantou.Framework.Barcodes.Internal.Pdf417;

/// <summary>Reed-Solomon error correction over the prime field GF(929) (ISO/IEC 15438, 5.10).</summary>
internal static class Pdf417ErrorCorrection
{
    public const int Modulus = 929;

    private static readonly int[]?[] GeneratorCache = new int[]?[9];

    /// <summary>Gets the number of error correction codewords of a level: 2^(level + 1).</summary>
    public static int GetCodewordCount(int level) => 2 << level;

    /// <summary>Computes the error correction codewords of the data codewords.</summary>
    /// <param name="data">The data codewords: the symbol length descriptor, the data and the pad codewords.</param>
    /// <param name="level">The error correction level, from 0 to 8.</param>
    /// <param name="destination">Receives the error correction codewords, in the order of the symbol.</param>
    public static void Encode(ReadOnlySpan<int> data, int level, Span<int> destination)
    {
        var coefficients = GetGenerator(level);
        var count = coefficients.Length;
        destination = destination[..count];
        destination.Clear();

        // The remainder of data(x) * x^k by the generator, the coefficient of x^(k - 1) first
        foreach (var codeword in data)
        {
            var factor = Add(codeword, destination[0]);
            for (var i = 0; i < count - 1; i++)
            {
                destination[i] = Subtract(destination[i + 1], Multiply(factor, coefficients[i]));
            }

            destination[count - 1] = Subtract(0, Multiply(factor, coefficients[count - 1]));
        }

        // The error correction codewords are the complements of the coefficients of the remainder
        for (var i = 0; i < count; i++)
        {
            destination[i] = Subtract(0, destination[i]);
        }
    }

    // The coefficients of g(x) = (x - 3)(x - 3^2)...(x - 3^k), without the leading 1, the coefficient of x^(k - 1) first
    private static int[] GetGenerator(int level)
    {
        var generator = GeneratorCache[level];
        if (generator is not null)
            return generator;

        var count = GetCodewordCount(level);

        // polynomial[i] is the coefficient of x^i
        var polynomial = new int[count + 1];
        polynomial[0] = 1;
        var root = 1;
        for (var degree = 1; degree <= count; degree++)
        {
            root = Multiply(root, 3);
            for (var i = degree; i > 0; i--)
            {
                polynomial[i] = Subtract(polynomial[i - 1], Multiply(root, polynomial[i]));
            }

            polynomial[0] = Subtract(0, Multiply(root, polynomial[0]));
        }

        generator = new int[count];
        for (var i = 0; i < count; i++)
        {
            generator[i] = polynomial[count - 1 - i];
        }

        GeneratorCache[level] = generator;
        return generator;
    }

    private static int Add(int a, int b) => (a + b) % Modulus;

    private static int Subtract(int a, int b) => (a - b + Modulus) % Modulus;

    private static int Multiply(int a, int b) => a * b % Modulus;
}
