using Meziantou.Framework.Barcodes.Internal.Linear;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Represents a generated barcode as a boolean matrix of modules.
/// </summary>
public sealed class Barcode
{
    private readonly bool[,] _modules;

    internal Barcode(bool[,] modules, BarcodeType type, string text, bool isStacked = false)
    {
        _modules = modules;
        Type = type;
        Text = text;
        IsStacked = isStacked;
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

    /// <summary>Gets whether the barcode is a stacked symbol: several rows of bars, whose heights are part of the symbol.</summary>
    /// <remarks>
    /// A linear barcode has one row, as high as the renderer draws it (<see cref="BarcodeSvgOptions.ModuleHeight"/>,
    /// <see cref="BarcodePngOptions.ModuleHeight"/>). A stacked barcode, such as <see cref="BarcodeType.Gs1DataBarStacked"/>,
    /// has one row per module of height: a row of bars 5 modules high is 5 identical rows, and the renderers draw each row
    /// as high as a module is wide.
    /// </remarks>
    public bool IsStacked { get; }

    /// <summary>
    /// Gets whether the module at the specified position is dark.
    /// </summary>
    /// <param name="row">The row index (0-based).</param>
    /// <param name="column">The column index (0-based).</param>
    /// <returns><see langword="true"/> if the module is dark; otherwise, <see langword="false"/>.</returns>
    public bool this[int row, int column] => _modules[row, column];

    /// <summary>Gets the modules, without copying them: the callers must not modify them.</summary>
    internal bool[,] Modules => _modules;

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
    /// Creates a UPC-E barcode: a UPC-A number of number system 0 or 1, without some of its zeros.
    /// </summary>
    /// <param name="data">
    /// The UPC-E number: 6 digits (number system 0, without check digit), 7 digits (the number system and 6 digits) or
    /// 8 digits (the number system, 6 digits and the check digit). Or the UPC-A number to compress: 11 digits (without
    /// check digit) or 12 digits (with check digit).
    /// </param>
    /// <param name="extension">Optional UPC/EAN 2-digit or 5-digit supplemental extension.</param>
    /// <returns>A new <see cref="Barcode"/> instance, whose <see cref="Text"/> is the 8 digits of the UPC-E number.</returns>
    /// <remarks>
    /// The check digit is the one of the UPC-A number. A UPC-A number has a UPC-E form when it has the zeros UPC-E removes:
    /// for instance the UPC-A number <c>03600000029</c> is the UPC-E number <c>0360290</c>, and <c>03600029145</c> has no
    /// UPC-E form. A UPC-A number has a single UPC-E form: the other 6 digits that stand for it are rejected.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="data"/> has another length, characters other than digits, a number system other than 0 or 1, an invalid check digit, or is a UPC-A number with no UPC-E form.</exception>
    public static Barcode CreateUpcE(string data, string? extension = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        return EanUpcEncoder.EncodeUpcE(data, extension);
    }

    /// <summary>
    /// Creates a GS1 DataBar Omnidirectional barcode from a GTIN.
    /// </summary>
    /// <param name="data">The GTIN: 13 digits (without check digit) or 14 digits (with check digit). Add leading zeros to a GTIN-8, GTIN-12 or GTIN-13.</param>
    /// <returns>A new <see cref="Barcode"/> instance, whose <see cref="Text"/> is the GTIN as the element string <c>(01)</c> followed by its 14 digits.</returns>
    /// <remarks>
    /// The symbol is 96 modules wide; GS1 DataBar Omnidirectional is at least 33 modules high. Rendered 13 modules high
    /// (13 times the module width), it is GS1 DataBar Truncated, which has the same modules.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="data"/> is not a GTIN of 13 or 14 digits, or its check digit is invalid.</exception>
    public static Barcode CreateGs1DataBar(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return DataBarEncoder.EncodeOmnidirectional(data, BarcodeType.Gs1DataBar);
    }

    /// <summary>
    /// Creates a GS1 DataBar Stacked barcode from a GTIN: GS1 DataBar in two rows 5 and 7 modules high.
    /// </summary>
    /// <param name="data">The GTIN: 13 digits (without check digit) or 14 digits (with check digit).</param>
    /// <returns>A new stacked <see cref="Barcode"/> instance, 50 modules wide and 13 modules high.</returns>
    /// <exception cref="ArgumentException"><paramref name="data"/> is not a GTIN of 13 or 14 digits, or its check digit is invalid.</exception>
    public static Barcode CreateGs1DataBarStacked(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return DataBarEncoder.EncodeOmnidirectional(data, BarcodeType.Gs1DataBarStacked);
    }

    /// <summary>
    /// Creates a GS1 DataBar Stacked Omnidirectional barcode from a GTIN: GS1 DataBar in two rows 33 modules high.
    /// </summary>
    /// <param name="data">The GTIN: 13 digits (without check digit) or 14 digits (with check digit).</param>
    /// <returns>A new stacked <see cref="Barcode"/> instance, 50 modules wide and 69 modules high.</returns>
    /// <exception cref="ArgumentException"><paramref name="data"/> is not a GTIN of 13 or 14 digits, or its check digit is invalid.</exception>
    public static Barcode CreateGs1DataBarStackedOmnidirectional(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return DataBarEncoder.EncodeOmnidirectional(data, BarcodeType.Gs1DataBarStackedOmnidirectional);
    }

    /// <summary>
    /// Creates a GS1 DataBar Limited barcode from a GTIN that starts with 0 or 1.
    /// </summary>
    /// <param name="data">The GTIN: 13 digits (without check digit) or 14 digits (with check digit), the first one 0 or 1.</param>
    /// <returns>A new <see cref="Barcode"/> instance, 74 modules wide, whose <see cref="Text"/> is the GTIN as the element string <c>(01)</c> followed by its 14 digits.</returns>
    /// <exception cref="ArgumentException"><paramref name="data"/> is not a GTIN of 13 or 14 digits, its check digit is invalid, or its first digit is not 0 or 1.</exception>
    public static Barcode CreateGs1DataBarLimited(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return DataBarEncoder.EncodeLimited(data);
    }

    /// <summary>
    /// Creates a GS1 DataBar Expanded barcode from GS1 element strings.
    /// </summary>
    /// <param name="elements">The element strings, in the order they are encoded.</param>
    /// <returns>A new <see cref="Barcode"/> instance, whose <see cref="Text"/> is the human-readable interpretation: each application identifier in parentheses, followed by its data.</returns>
    /// <remarks>
    /// The symbol holds up to 74 digits or 41 letters. It is shorter when the data starts with a GTIN (<c>01</c>), and
    /// shorter still for a GTIN of a variable measure trade item (starting with 9) followed by its weight
    /// (<c>310n</c>, <c>320n</c>) and a date (<c>11</c>, <c>13</c>, <c>15</c>, <c>17</c>), or by its price (<c>392n</c>, <c>393n</c>).
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="elements"/> is empty, contains <see langword="null"/>, or does not fit in a symbol.</exception>
    public static Barcode CreateGs1DataBarExpanded(params ReadOnlySpan<Gs1Element> elements)
    {
        if (elements.IsEmpty)
        {
            throw new ArgumentException("The elements cannot be empty.", nameof(elements));
        }

        return DataBarExpandedEncoder.Encode(elements, segmentsPerRow: 0);
    }

    /// <summary>
    /// Creates a GS1 DataBar Expanded Stacked barcode from GS1 element strings: GS1 DataBar Expanded in rows of
    /// <paramref name="segmentsPerRow"/> segments.
    /// </summary>
    /// <param name="segmentsPerRow">The number of segments (symbol characters) per row: an even number from 2 to 20. The last row may have fewer.</param>
    /// <param name="elements">The element strings, in the order they are encoded.</param>
    /// <returns>A new stacked <see cref="Barcode"/> instance: rows 34 modules high, separated by 3 rows of separator patterns.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="segmentsPerRow"/> is not an even number from 2 to 20.</exception>
    /// <exception cref="ArgumentException"><paramref name="elements"/> is empty, contains <see langword="null"/>, or does not fit in a symbol.</exception>
    public static Barcode CreateGs1DataBarExpandedStacked(int segmentsPerRow, params ReadOnlySpan<Gs1Element> elements)
    {
        if (segmentsPerRow is < 2 or > 20 || segmentsPerRow % 2 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentsPerRow), segmentsPerRow, "The number of segments per row must be an even number from 2 to 20.");
        }

        if (elements.IsEmpty)
        {
            throw new ArgumentException("The elements cannot be empty.", nameof(elements));
        }

        return DataBarExpandedEncoder.Encode(elements, segmentsPerRow);
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
