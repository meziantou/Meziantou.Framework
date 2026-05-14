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
}
