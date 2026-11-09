using System.Runtime.CompilerServices;
using Meziantou.Framework.Barcodes.Internal;
using Meziantou.Framework.Barcodes.Internal.Aztec;
using Meziantou.Framework.Barcodes.Internal.DataMatrix;
using Meziantou.Framework.Barcodes.Internal.Pdf417;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Represents a generated two-dimensional barcode (Data Matrix, PDF417 or Aztec) as a boolean matrix of modules.
/// </summary>
/// <remarks>
/// <para>
/// A text made of ASCII characters only (U+0000 to U+007F) is encoded as it is. Another text is encoded as UTF-8, preceded
/// by the ECI designator of UTF-8 (assignment number 26), so that readers do not decode its bytes with the default
/// character set of the symbology. Binary data is encoded as it is, without an ECI designator.
/// </para>
/// <para>
/// The modules are square. The rows of a PDF417 symbol are taller than its modules are wide: each row of the symbol is
/// <see cref="Pdf417Options.RowHeight"/> rows of the matrix.
/// </para>
/// </remarks>
public sealed class Barcode2D
{
    private const int Utf8EciAssignmentNumber = 26;

    private readonly bool[,] _modules;

    internal Barcode2D(bool[,] modules, Barcode2DType type)
    {
        _modules = modules;
        Type = type;
        Height = modules.GetLength(0);
        Width = modules.GetLength(1);
    }

    /// <summary>Gets the type of barcode.</summary>
    public Barcode2DType Type { get; }

    /// <summary>Gets the width (number of columns), quiet zone excluded.</summary>
    public int Width { get; }

    /// <summary>Gets the height (number of rows), quiet zone excluded.</summary>
    public int Height { get; }

    /// <summary>
    /// Gets whether the module at the specified position is dark.
    /// </summary>
    /// <param name="row">The row index (0-based).</param>
    /// <param name="column">The column index (0-based).</param>
    /// <returns><see langword="true"/> if the module is dark; otherwise, <see langword="false"/>.</returns>
    public bool this[int row, int column] => _modules[row, column];

    /// <summary>
    /// Creates a Data Matrix (ECC 200) symbol from the specified text.
    /// </summary>
    /// <param name="data">The text to encode.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults: the smallest square symbol that holds the data.</param>
    /// <returns>A new <see cref="Barcode2D"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty, or no Data Matrix size satisfies the constraints of <paramref name="options"/>.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest allowed size.</exception>
    public static Barcode2D CreateDataMatrix(string data, DataMatrixOptions? options = null)
    {
        var (bytes, eci) = GetTextBytes(data);
        return new Barcode2D(DataMatrixEncoder.Encode(bytes, gs1: false, eci, options), Barcode2DType.DataMatrix);
    }

    /// <summary>
    /// Creates a Data Matrix (ECC 200) symbol from the specified binary data.
    /// </summary>
    /// <param name="data">The bytes to encode.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults: the smallest square symbol that holds the data.</param>
    /// <returns>A new <see cref="Barcode2D"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty, or no Data Matrix size satisfies the constraints of <paramref name="options"/>.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest allowed size.</exception>
    public static Barcode2D CreateDataMatrix(ReadOnlySpan<byte> data, DataMatrixOptions? options = null)
    {
        if (data.IsEmpty)
            throw new ArgumentException("The data cannot be empty.", nameof(data));

        return new Barcode2D(DataMatrixEncoder.Encode(data, gs1: false, eci: null, options), Barcode2DType.DataMatrix);
    }

    /// <summary>
    /// Creates a GS1 Data Matrix symbol from GS1 element strings.
    /// </summary>
    /// <param name="elements">The element strings, in the order they are encoded.</param>
    /// <returns>A new <see cref="Barcode2D"/> instance.</returns>
    /// <remarks>
    /// The symbol starts with the FNC1 function character that identifies GS1 Data Matrix, and a FNC1 separator follows each
    /// element whose application identifier does not have a predefined length, except the last one. A reader returns the
    /// data with the separators as the ASCII group separator character (U+001D) and the symbology identifier <c>]d2</c>.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="elements"/> is empty or contains <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest Data Matrix symbol.</exception>
    public static Barcode2D CreateGs1DataMatrix(params ReadOnlySpan<Gs1Element> elements)
    {
        return CreateGs1DataMatrix(elements, options: null);
    }

    /// <summary>
    /// Creates a GS1 Data Matrix symbol from GS1 element strings.
    /// </summary>
    /// <param name="elements">The element strings, in the order they are encoded.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults: the smallest square symbol that holds the data.</param>
    /// <returns>A new <see cref="Barcode2D"/> instance.</returns>
    /// <remarks>
    /// The symbol starts with the FNC1 function character that identifies GS1 Data Matrix, and a FNC1 separator follows each
    /// element whose application identifier does not have a predefined length, except the last one. A reader returns the
    /// data with the separators as the ASCII group separator character (U+001D) and the symbology identifier <c>]d2</c>.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="elements"/> is empty or contains <see langword="null"/>, or no Data Matrix size satisfies the constraints of <paramref name="options"/>.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest allowed size.</exception>
    public static Barcode2D CreateGs1DataMatrix(ReadOnlySpan<Gs1Element> elements, DataMatrixOptions? options)
    {
        if (elements.IsEmpty)
            throw new ArgumentException("The elements cannot be empty.", nameof(elements));

        // The FNC1 separators are given to the encoder as GS characters, which the data of an element cannot contain
        var data = Gs1ElementStrings.Concatenate(elements, separator: '\u001D', nameof(elements));
        return new Barcode2D(DataMatrixEncoder.Encode(Encoding.ASCII.GetBytes(data), gs1: true, eci: null, options), Barcode2DType.DataMatrix);
    }

    /// <summary>
    /// Creates a PDF417 symbol from the specified text.
    /// </summary>
    /// <param name="data">The text to encode.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults: the error correction level recommended for the data length, and a number of columns chosen for the data.</param>
    /// <returns>A new <see cref="Barcode2D"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in a PDF417 symbol with the constraints of <paramref name="options"/>.</exception>
    public static Barcode2D CreatePdf417(string data, Pdf417Options? options = null)
    {
        var (bytes, eci) = GetTextBytes(data);
        return new Barcode2D(Pdf417Encoder.Encode(bytes, eci, options), Barcode2DType.Pdf417);
    }

    /// <summary>
    /// Creates a PDF417 symbol from the specified binary data.
    /// </summary>
    /// <param name="data">The bytes to encode.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults: the error correction level recommended for the data length, and a number of columns chosen for the data.</param>
    /// <returns>A new <see cref="Barcode2D"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in a PDF417 symbol with the constraints of <paramref name="options"/>.</exception>
    public static Barcode2D CreatePdf417(ReadOnlySpan<byte> data, Pdf417Options? options = null)
    {
        if (data.IsEmpty)
            throw new ArgumentException("The data cannot be empty.", nameof(data));

        return new Barcode2D(Pdf417Encoder.Encode(data, eci: null, options), Barcode2DType.Pdf417);
    }

    /// <summary>
    /// Creates an Aztec symbol from the specified text.
    /// </summary>
    /// <param name="data">The text to encode.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults: the smallest compact or full-range symbol that holds the data with 23% of error correction.</param>
    /// <returns>A new <see cref="Barcode2D"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty, or <see cref="AztecOptions.Layers"/> is greater than 4 for a compact symbol.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest allowed size.</exception>
    public static Barcode2D CreateAztec(string data, AztecOptions? options = null)
    {
        var (bytes, eci) = GetTextBytes(data);
        return new Barcode2D(AztecEncoder.Encode(bytes, eci, options), Barcode2DType.Aztec);
    }

    /// <summary>
    /// Creates an Aztec symbol from the specified binary data.
    /// </summary>
    /// <param name="data">The bytes to encode.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults: the smallest compact or full-range symbol that holds the data with 23% of error correction.</param>
    /// <returns>A new <see cref="Barcode2D"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty, or <see cref="AztecOptions.Layers"/> is greater than 4 for a compact symbol.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest allowed size.</exception>
    public static Barcode2D CreateAztec(ReadOnlySpan<byte> data, AztecOptions? options = null)
    {
        if (data.IsEmpty)
            throw new ArgumentException("The data cannot be empty.", nameof(data));

        return new Barcode2D(AztecEncoder.Encode(data, eci: null, options), Barcode2DType.Aztec);
    }

    private static (byte[] Bytes, int? Eci) GetTextBytes(string data, [CallerArgumentExpression(nameof(data))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(data, paramName);
        if (data.Length == 0)
            throw new ArgumentException("The data cannot be empty.", paramName);

        return Ascii.IsValid(data)
            ? (Encoding.ASCII.GetBytes(data), null)
            : (Encoding.UTF8.GetBytes(data), Utf8EciAssignmentNumber);
    }
}
