namespace Meziantou.Framework.Barcodes.Internal.DataMatrix;

// ISO/IEC 16022:2006
internal static class DataMatrixEncoder
{
    public static bool[,] Encode(ReadOnlySpan<byte> data, bool gs1, int? eci, DataMatrixOptions? options)
    {
        _ = BinaryGaloisField.Field256;
        throw new NotSupportedException("Not implemented yet.");
    }
}
