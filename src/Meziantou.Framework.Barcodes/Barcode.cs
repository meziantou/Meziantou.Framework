using Meziantou.Framework.Barcodes.Internal.Linear;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Represents a generated barcode as a boolean matrix of modules.
/// </summary>
public sealed class Barcode
{
    private readonly bool[,] _modules;

    internal Barcode(bool[,] modules, BarcodeType type, string text)
    {
        _modules = modules;
        Type = type;
        Text = text;
        Height = modules.GetLength(0);
        Width = modules.GetLength(1);
    }

    /// <summary>Gets the type of barcode.</summary>
    public BarcodeType Type { get; }

    /// <summary>Gets the human-readable interpretation of the barcode: the encoded data as it is printed under the bars.</summary>
    /// <remarks>
    /// It includes the characters the barcode adds: the check digit of EAN-8, EAN-13 and UPC-A (followed by a space and
    /// the add-on when there is one), the Mod 43 check character of Code 39 when it is included, and the start and stop
    /// characters of Codabar. It does not include the Code 39 and Code 93 start and stop characters.
    /// </remarks>
    public string Text { get; }

    /// <summary>Gets the width (number of columns).</summary>
    public int Width { get; }

    /// <summary>Gets the height (number of rows).</summary>
    public int Height { get; }

    /// <summary>
    /// Gets whether the module at the specified position is dark.
    /// </summary>
    /// <param name="row">The row index (0-based).</param>
    /// <param name="column">The column index (0-based).</param>
    /// <returns><see langword="true"/> if the module is dark; otherwise, <see langword="false"/>.</returns>
    public bool this[int row, int column] => _modules[row, column];

    /// <summary>
    /// Creates a Code 39 barcode from the specified data.
    /// </summary>
    /// <param name="data">The data to encode.</param>
    /// <param name="includeChecksum"><see langword="true"/> to append the optional Mod 43 checksum character; otherwise, <see langword="false"/>.</param>
    /// <returns>A new <see cref="Barcode"/> instance.</returns>
    public static Barcode CreateCode39(string data, bool includeChecksum = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        return Code39Encoder.Encode(data, includeChecksum);
    }

    /// <summary>
    /// Creates a Code 128 barcode from the specified data.
    /// </summary>
    /// <param name="data">The data to encode: ASCII characters (U+0000 to U+007F), control characters included.</param>
    /// <remarks>
    /// The encoder chooses the code sets: C for runs of digits, A for control characters, B for lowercase letters, with a
    /// SHIFT when the other code set is needed for a single character.
    /// </remarks>
    /// <returns>A new <see cref="Barcode"/> instance.</returns>
    public static Barcode CreateCode128(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        return Code128Encoder.Encode(data);
    }

    /// <summary>
    /// Creates a GS1-128 barcode from GS1 element strings.
    /// </summary>
    /// <param name="elements">The element strings, in the order they are encoded.</param>
    /// <returns>A new <see cref="Barcode"/> instance, whose <see cref="Text"/> is the human-readable interpretation: each application identifier in parentheses, followed by its data.</returns>
    /// <remarks>
    /// <para>
    /// The symbol starts with the FNC1 function character that identifies GS1-128, and a FNC1 separator follows each element
    /// whose application identifier does not have a predefined length, except the last one. The parentheses of the
    /// human-readable interpretation are not encoded. A reader returns the data with the separators as the ASCII group
    /// separator character (U+001D) and the symbology identifier <c>]C1</c>.
    /// </para>
    /// <para>
    /// Put the elements of variable length last to save separators. <see cref="CreateCode128(string)"/> never adds FNC1:
    /// the symbols it creates are not GS1-128 symbols, even when the data looks like element strings.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="elements"/> is empty, contains <see langword="null"/>, or needs more than the 48 data characters of a GS1-128 symbol (application identifiers and separators included).</exception>
    public static Barcode CreateGs1128(params ReadOnlySpan<Gs1Element> elements)
    {
        if (elements.IsEmpty)
        {
            throw new ArgumentException("The elements cannot be empty.", nameof(elements));
        }

        return Gs1128Encoder.Encode(elements);
    }

    /// <summary>
    /// Creates a Code 93 barcode from the specified data.
    /// </summary>
    /// <param name="data">The data to encode.</param>
    /// <returns>A new <see cref="Barcode"/> instance.</returns>
    public static Barcode CreateCode93(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        return Code93Encoder.Encode(data);
    }

    /// <summary>
    /// Creates an EAN-8 barcode from the specified data.
    /// </summary>
    /// <param name="data">The data to encode (7 digits without checksum, or 8 digits with checksum).</param>
    /// <param name="extension">Optional UPC/EAN 2-digit or 5-digit supplemental extension.</param>
    /// <returns>A new <see cref="Barcode"/> instance.</returns>
    public static Barcode CreateEan8(string data, string? extension = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        return EanUpcEncoder.EncodeEan8(data, extension);
    }

    /// <summary>
    /// Creates an EAN-13 barcode from the specified data.
    /// </summary>
    /// <param name="data">The data to encode (12 digits without checksum, or 13 digits with checksum).</param>
    /// <param name="extension">Optional UPC/EAN 2-digit or 5-digit supplemental extension.</param>
    /// <returns>A new <see cref="Barcode"/> instance.</returns>
    public static Barcode CreateEan13(string data, string? extension = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        return EanUpcEncoder.EncodeEan13(data, extension);
    }

    /// <summary>
    /// Creates a UPC-A barcode from the specified data.
    /// </summary>
    /// <param name="data">The data to encode (11 digits without checksum, or 12 digits with checksum).</param>
    /// <param name="extension">Optional UPC/EAN 2-digit or 5-digit supplemental extension.</param>
    /// <returns>A new <see cref="Barcode"/> instance.</returns>
    public static Barcode CreateUpcA(string data, string? extension = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        return EanUpcEncoder.EncodeUpcA(data, extension);
    }

    /// <summary>
    /// Creates a Codabar barcode from the specified data.
    /// </summary>
    /// <param name="data">The data to encode.</param>
    /// <param name="startCharacter">The start character (<c>A</c>, <c>B</c>, <c>C</c>, or <c>D</c>).</param>
    /// <param name="stopCharacter">The stop character (<c>A</c>, <c>B</c>, <c>C</c>, or <c>D</c>).</param>
    /// <returns>A new <see cref="Barcode"/> instance.</returns>
    public static Barcode CreateCodabar(string data, char startCharacter = 'A', char stopCharacter = 'B')
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        return CodabarEncoder.Encode(data, startCharacter, stopCharacter);
    }

    /// <summary>
    /// Creates an Interleaved 2 of 5 (ITF) barcode from the specified data.
    /// </summary>
    /// <param name="data">The data to encode (digits only, even number of digits).</param>
    /// <returns>A new <see cref="Barcode"/> instance.</returns>
    public static Barcode CreateItf(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        return ItfEncoder.Encode(data);
    }
}
