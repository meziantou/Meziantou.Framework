using Meziantou.Framework.Barcodes.Internal;
using Meziantou.Framework.Barcodes.Internal.Aztec;
using Meziantou.Framework.Barcodes.Internal.DataMatrix;
using Meziantou.Framework.Barcodes.Internal.Pdf417;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Reads the data of Data Matrix (ECC 200), PDF417 and Aztec symbols from their modules, with error correction.
/// </summary>
/// <remarks>
/// <para>
/// The decoder works on modules, not on pixels. To read the symbols of an image, use the <c>ReadBarcodes2D</c> methods of
/// the <c>Meziantou.Framework.Imaging.Barcodes</c> package, which locate the symbols and sample their modules.
/// </para>
/// <para>
/// A Data Matrix or Aztec symbol can be in any orientation, mirrored included: the decoder finds it from the finder
/// pattern. The rows of a PDF417 symbol can be read from either side, and each row of the symbol can span several rows of
/// the matrix, as <see cref="Barcode2D"/> draws them.
/// </para>
/// </remarks>
public static class Barcode2DDecoder
{
    /// <summary>Reads the data of a symbol.</summary>
    /// <param name="barcode">The symbol.</param>
    /// <returns>The content of the symbol.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Barcodes.Tests/DataMatrixTests.cs" ref:region="Barcode2DDecoder_Decode" ref:dedent="true">
    /// var dataMatrix = Barcode2D.CreateDataMatrix("https://www.meziantou.net");
    ///
    /// var result = Barcode2DDecoder.Decode(dataMatrix);
    ///
    /// Assert.Equal("https://www.meziantou.net", result.Text);
    /// Assert.Equal(Barcode2DType.DataMatrix, result.Type);
    /// </code>
    /// </example>
    public static Barcode2DDecodeResult Decode(Barcode2D barcode)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        return Decode(barcode.Modules, barcode.Type);
    }

    /// <summary>Reads the data of a symbol from its modules.</summary>
    /// <param name="modules">The modules, <see langword="true"/> for dark, indexed by row then column, without quiet zone.</param>
    /// <param name="type">The type of the symbol.</param>
    /// <returns>The content of the symbol.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="modules"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a value of <see cref="Barcode2DType"/>.</exception>
    /// <exception cref="FormatException">The modules are not a symbol of the type, or have more errors than the error correction can fix.</exception>
    public static Barcode2DDecodeResult Decode(bool[,] modules, Barcode2DType type)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ValidateType(type);
        var error = TryDecodeCore(modules, type, out var result);
        if (result is not null)
            return result;

        throw new FormatException(error);
    }

    /// <summary>Tries to read the data of a symbol from its modules.</summary>
    /// <param name="modules">The modules, <see langword="true"/> for dark, indexed by row then column, without quiet zone.</param>
    /// <param name="type">The type of the symbol.</param>
    /// <param name="result">When this method returns <see langword="true"/>, the content of the symbol.</param>
    /// <returns><see langword="true"/> if the modules are a symbol that could be read; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="modules"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a value of <see cref="Barcode2DType"/>.</exception>
    public static bool TryDecode(bool[,] modules, Barcode2DType type, [NotNullWhen(true)] out Barcode2DDecodeResult? result)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ValidateType(type);
        TryDecodeCore(modules, type, out result);
        return result is not null;
    }

    private static void ValidateType(Barcode2DType type)
    {
        if (type is not (Barcode2DType.DataMatrix or Barcode2DType.Pdf417 or Barcode2DType.Aztec))
            throw new ArgumentOutOfRangeException(nameof(type), type, "The type is not a two-dimensional barcode type.");
    }

    private static string TryDecodeCore(bool[,] modules, Barcode2DType type, out Barcode2DDecodeResult? result)
    {
        result = null;
        DecodedDataBuilder? data;
        string? error;
        var isGs1 = false;
        int correctedErrorCount;
        switch (type)
        {
            case Barcode2DType.DataMatrix:
                error = DataMatrixDecoder.TryDecodeAnyOrientation(modules, out data, out isGs1, out correctedErrorCount);
                break;

            case Barcode2DType.Aztec:
                error = AztecDecoder.TryDecode(modules, out data, out correctedErrorCount, out _);
                break;

            default:
                // The rows of the symbol are the rows of the matrix, or its columns when it is turned by a quarter
                data = new DecodedDataBuilder();
                error = Pdf417Decoder.TryDecode(modules, data, out correctedErrorCount);
                if (error is not null)
                {
                    var turned = new DecodedDataBuilder();
                    if (Pdf417Decoder.TryDecode(MatrixTransforms.Apply(modules, 1), turned, out correctedErrorCount) is null)
                    {
                        data = turned;
                        error = null;
                    }
                }

                break;
        }

        if (error is not null || data is null)
            return error ?? "";

        result = new Barcode2DDecodeResult(type, data.GetText(isGs1), data.GetBytes(), data.FirstEci, isGs1, correctedErrorCount);
        return "";
    }
}
