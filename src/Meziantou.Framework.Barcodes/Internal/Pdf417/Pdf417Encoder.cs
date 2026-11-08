namespace Meziantou.Framework.Barcodes.Internal.Pdf417;

// ISO/IEC 15438:2015
internal static class Pdf417Encoder
{
    /// <summary>Encodes the data and returns the module matrix, each row of the symbol repeated <see cref="Pdf417Options.RowHeight"/> times.</summary>
    /// <param name="data">The bytes to encode.</param>
    /// <param name="eci">The ECI assignment number to declare at the start of the data, or <see langword="null"/>.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    public static bool[,] Encode(ReadOnlySpan<byte> data, int? eci, Pdf417Options? options)
    {
        throw new NotSupportedException("Not implemented yet.");
    }
}
