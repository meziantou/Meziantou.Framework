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

    /// <summary>
    /// Corrects the codewords in place: the data codewords followed by the error correction codewords, as they are in the
    /// symbol. The code over GF(929) has the roots 3^1 to 3^k: Berlekamp-Massey finds the error locator, Chien search the
    /// positions and Forney the values.
    /// </summary>
    /// <returns>The number of corrected codewords, or -1 when there are too many errors to correct.</returns>
    public static int Correct(Span<int> codewords, int errorCorrectionCount)
    {
        var n = codewords.Length;
        var syndromes = new int[errorCorrectionCount];
        if (!ComputeSyndromes(codewords, syndromes))
            return 0;

        // Berlekamp-Massey, the coefficients lowest degree first
        var current = new int[errorCorrectionCount + 1];
        var previous = new int[errorCorrectionCount + 1];
        current[0] = 1;
        previous[0] = 1;
        var length = 0;
        var shift = 1;
        var previousDiscrepancy = 1;
        for (var k = 0; k < errorCorrectionCount; k++)
        {
            var discrepancy = syndromes[k];
            for (var i = 1; i <= length; i++)
            {
                discrepancy = Add(discrepancy, Multiply(current[i], syndromes[k - i]));
            }

            if (discrepancy is 0)
            {
                shift++;
                continue;
            }

            var factor = Multiply(discrepancy, Inverse(previousDiscrepancy));
            var copy = (int[])current.Clone();
            for (var i = 0; i + shift < current.Length; i++)
            {
                current[i + shift] = Subtract(current[i + shift], Multiply(factor, previous[i]));
            }

            if (2 * length <= k)
            {
                length = k + 1 - length;
                previous = copy;
                previousDiscrepancy = discrepancy;
                shift = 1;
            }
            else
            {
                shift++;
            }
        }

        if (length * 2 > errorCorrectionCount)
            return -1;

        var locator = current.AsSpan(0, length + 1);

        // Ω(x) = S(x)Λ(x) mod x^k
        var evaluator = new int[errorCorrectionCount];
        for (var i = 0; i < errorCorrectionCount; i++)
        {
            var value = 0;
            for (var j = 0; j <= i && j < locator.Length; j++)
            {
                value = Add(value, Multiply(locator[j], syndromes[i - j]));
            }

            evaluator[i] = value;
        }

        var found = 0;
        for (var index = 0; index < n; index++)
        {
            var degree = n - 1 - index;
            var inverse = Inverse(Power(3, degree));
            if (Evaluate(locator, inverse) is not 0)
                continue;

            found++;

            // Forney with the first root 3^1: e = -Ω(X^-1) / Λ'(X^-1)
            var derivative = 0;
            for (var i = 1; i < locator.Length; i++)
            {
                derivative = Add(derivative, Multiply(Multiply(i % Modulus, locator[i]), Power(inverse, i - 1)));
            }

            if (derivative is 0)
                return -1;

            var error = Subtract(0, Multiply(Evaluate(evaluator, inverse), Inverse(derivative)));
            codewords[index] = Subtract(codewords[index], error);
        }

        if (found != length)
            return -1;

        // A word with more errors than the code corrects can look correctable: check that the result is a codeword
        return ComputeSyndromes(codewords, syndromes) ? -1 : length;
    }

    // S_j = r(3^(j + 1)), and whether one of them is not zero
    private static bool ComputeSyndromes(ReadOnlySpan<int> codewords, Span<int> syndromes)
    {
        var hasError = false;
        for (var j = 0; j < syndromes.Length; j++)
        {
            var root = Power(3, j + 1);
            var value = 0;
            foreach (var codeword in codewords)
            {
                value = Add(Multiply(value, root), codeword % Modulus);
            }

            syndromes[j] = value;
            hasError |= value is not 0;
        }

        return hasError;
    }

    // Evaluates a polynomial, lowest degree first
    private static int Evaluate(ReadOnlySpan<int> polynomial, int x)
    {
        var value = 0;
        for (var i = polynomial.Length - 1; i >= 0; i--)
        {
            value = Add(Multiply(value, x), polynomial[i]);
        }

        return value;
    }

    private static int Power(int value, int exponent)
    {
        var result = 1;
        var factor = value % Modulus;
        while (exponent > 0)
        {
            if ((exponent & 1) is not 0)
            {
                result = Multiply(result, factor);
            }

            factor = Multiply(factor, factor);
            exponent >>= 1;
        }

        return result;
    }

    // Fermat: a^(p - 2) is the inverse of a in GF(p)
    private static int Inverse(int value) => Power(value, Modulus - 2);

    private static int Add(int a, int b) => (a + b) % Modulus;

    private static int Subtract(int a, int b) => (a - b + Modulus) % Modulus;

    private static int Multiply(int a, int b) => a * b % Modulus;
}
