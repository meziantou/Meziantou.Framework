using Meziantou.Framework.Internal.Barcodes;

namespace Meziantou.Framework;

/// <summary>
/// Represents a generated barcode as a boolean matrix of modules.
/// </summary>
public sealed class Barcode
{
    private readonly bool[,] _modules;

    internal Barcode(bool[,] modules, BarcodeType type)
    {
        _modules = modules;
        Type = type;
        Height = modules.GetLength(0);
        Width = modules.GetLength(1);
    }

    /// <summary>Gets the type of barcode.</summary>
    public BarcodeType Type { get; }

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
    /// <param name="data">The data to encode.</param>
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
