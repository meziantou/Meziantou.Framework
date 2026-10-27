namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The byte reordering and the delta predictor that RLE, ZIPS and ZIP apply to the uncompressed data of a chunk
/// (compression notes, section 2): the bytes at even offsets, then the bytes at odd offsets, each byte stored as its
/// difference with the previous one plus 128, modulo 256.
/// </summary>
internal static class ExrPredictor
{
    /// <summary>Reorders and predicts the bytes of a chunk.</summary>
    /// <param name="source">The uncompressed data.</param>
    /// <param name="destination">Receives the transformed bytes (as many as <paramref name="source"/>).</param>
    public static void Encode(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var length = source.Length;
        var half = (length + 1) / 2;
        for (var k = 0; k < half; k++)
        {
            destination[k] = source[2 * k];
        }

        for (var k = 0; k < length / 2; k++)
        {
            destination[half + k] = source[(2 * k) + 1];
        }

        // Backward, so that each difference reads the byte before it unchanged
        for (var i = length - 1; i > 0; i--)
        {
            destination[i] = (byte)(destination[i] - destination[i - 1] + 128);
        }
    }

    /// <summary>Undoes <see cref="Encode"/>.</summary>
    /// <param name="source">The transformed bytes; overwritten with the reordered bytes.</param>
    /// <param name="destination">Receives the uncompressed data (as many bytes as <paramref name="source"/>).</param>
    public static void Decode(Span<byte> source, Span<byte> destination)
    {
        var length = source.Length;
        for (var i = 1; i < length; i++)
        {
            source[i] = (byte)(source[i - 1] + source[i] - 128);
        }

        var half = (length + 1) / 2;
        for (var k = 0; k < half; k++)
        {
            destination[2 * k] = source[k];
        }

        for (var k = 0; k < length / 2; k++)
        {
            destination[(2 * k) + 1] = source[half + k];
        }
    }
}
