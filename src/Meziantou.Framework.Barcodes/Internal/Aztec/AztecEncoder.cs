namespace Meziantou.Framework.Barcodes.Internal.Aztec;

// ISO/IEC 24778:2008
internal static class AztecEncoder
{
    /// <summary>Encodes the data and returns the module matrix.</summary>
    /// <param name="data">The bytes to encode.</param>
    /// <param name="eci">The ECI assignment number to declare at the start of the data, or <see langword="null"/>.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    public static bool[,] Encode(ReadOnlySpan<byte> data, int? eci, AztecOptions? options)
    {
        throw new NotSupportedException("Not implemented yet.");
    }
}
