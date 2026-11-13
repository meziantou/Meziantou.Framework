namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// Corrects the errors of a Reed-Solomon codeword over GF(2^m): syndromes, Berlekamp-Massey for the error locator,
/// Chien search for the error positions and Forney for the error values.
/// </summary>
internal static class ReedSolomonDecoder
{
    /// <summary>
    /// Corrects <paramref name="codewords"/> in place: the data codewords followed by <paramref name="errorCorrectionCount"/>
    /// error correction codewords, highest degree first.
    /// </summary>
    /// <param name="field">The field of the codewords.</param>
    /// <param name="codewords">The codewords to correct.</param>
    /// <param name="errorCorrectionCount">The number of error correction codewords.</param>
    /// <param name="firstRoot">The power of α of the first root of the generator polynomial: 0 for QR codes, 1 for Data Matrix and Aztec.</param>
    /// <returns>The number of corrected codewords, or -1 when there are too many errors to correct.</returns>
    public static int Correct(BinaryGaloisField field, Span<int> codewords, int errorCorrectionCount, int firstRoot)
    {
        if (errorCorrectionCount <= 0)
            return 0;

        var syndromes = new int[errorCorrectionCount];
        if (!ComputeSyndromes(field, codewords, firstRoot, syndromes))
            return 0;

        var locator = ComputeErrorLocator(field, syndromes, out var errorCount);
        if (errorCount * 2 > errorCorrectionCount)
            return -1;

        // Chien search: the roots of the locator are the inverses of the error positions α^(degree)
        var positions = new int[errorCount];
        var found = 0;
        for (var index = 0; index < codewords.Length; index++)
        {
            var degree = codewords.Length - 1 - index;
            if (Evaluate(field, locator, field.Exp(-degree)) is 0)
            {
                if (found == errorCount)
                    return -1;

                positions[found++] = index;
            }
        }

        if (found != errorCount)
            return -1;

        // Forney: the error evaluator is Ω(x) = S(x)Λ(x) mod x^(2t), and the error value is X^(1-b) Ω(X^-1) / Λ'(X^-1)
        var evaluator = new int[errorCorrectionCount];
        for (var i = 0; i < errorCorrectionCount; i++)
        {
            var value = 0;
            for (var j = 0; j <= i && j < locator.Length; j++)
            {
                value ^= field.Multiply(locator[j], syndromes[i - j]);
            }

            evaluator[i] = value;
        }

        foreach (var index in positions)
        {
            var degree = codewords.Length - 1 - index;
            var inverse = field.Exp(-degree);
            var derivative = 0;
            for (var i = 1; i < locator.Length; i += 2)
            {
                derivative ^= field.Multiply(locator[i], field.Exp(-degree * (i - 1)));
            }

            if (derivative is 0)
                return -1;

            var value = field.Multiply(Evaluate(field, evaluator, inverse), field.Inverse(derivative));
            value = field.Multiply(value, field.Exp(degree * (1 - firstRoot)));
            codewords[index] ^= value;
        }

        // A codeword with more errors than the code corrects can look correctable: check that the result is a codeword
        return ComputeSyndromes(field, codewords, firstRoot, syndromes) ? -1 : errorCount;
    }

    /// <summary>Computes the syndromes S_j = r(α^(j + b)), and returns whether one of them is not zero.</summary>
    private static bool ComputeSyndromes(BinaryGaloisField field, ReadOnlySpan<int> codewords, int firstRoot, Span<int> syndromes)
    {
        var hasError = false;
        for (var j = 0; j < syndromes.Length; j++)
        {
            var root = field.Exp(j + firstRoot);
            var value = 0;
            foreach (var codeword in codewords)
            {
                value = field.Multiply(value, root) ^ codeword;
            }

            syndromes[j] = value;
            hasError |= value is not 0;
        }

        return hasError;
    }

    /// <summary>Computes the error locator Λ(x), lowest degree first, with the Berlekamp-Massey algorithm.</summary>
    private static int[] ComputeErrorLocator(BinaryGaloisField field, ReadOnlySpan<int> syndromes, out int errorCount)
    {
        var current = new int[syndromes.Length + 1];
        var previous = new int[syndromes.Length + 1];
        var temporary = new int[syndromes.Length + 1];
        current[0] = 1;
        previous[0] = 1;
        var length = 0;
        var shift = 1;
        var previousDiscrepancy = 1;

        for (var n = 0; n < syndromes.Length; n++)
        {
            var discrepancy = syndromes[n];
            for (var i = 1; i <= length; i++)
            {
                discrepancy ^= field.Multiply(current[i], syndromes[n - i]);
            }

            if (discrepancy is 0)
            {
                shift++;
                continue;
            }

            var factor = field.Multiply(discrepancy, field.Inverse(previousDiscrepancy));
            if (2 * length <= n)
            {
                current.CopyTo(temporary.AsSpan());
                SubtractShifted(field, current, previous, factor, shift);
                length = n + 1 - length;
                temporary.CopyTo(previous.AsSpan());
                previousDiscrepancy = discrepancy;
                shift = 1;
            }
            else
            {
                SubtractShifted(field, current, previous, factor, shift);
                shift++;
            }
        }

        errorCount = length;
        return current[..(length + 1)];
    }

    // target -= factor * x^shift * source
    private static void SubtractShifted(BinaryGaloisField field, Span<int> target, ReadOnlySpan<int> source, int factor, int shift)
    {
        for (var i = 0; i + shift < target.Length; i++)
        {
            target[i + shift] ^= field.Multiply(source[i], factor);
        }
    }

    // Evaluates a polynomial, lowest degree first, with Horner's rule
    private static int Evaluate(BinaryGaloisField field, ReadOnlySpan<int> polynomial, int x)
    {
        var value = 0;
        for (var i = polynomial.Length - 1; i >= 0; i--)
        {
            value = field.Multiply(value, x) ^ polynomial[i];
        }

        return value;
    }
}
