using System.Globalization;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Represents the place of a QR code in a structured append sequence: data split across up to 16 symbols, which readers
/// that support structured append put back together in order.
/// </summary>
/// <seealso cref="QRCode.CreateStructuredAppend(string, ErrorCorrectionLevel, QRCodeOptions)"/>
public sealed class QRCodeStructuredAppend
{
    internal QRCodeStructuredAppend(int index, int count, byte parity)
    {
        Index = index;
        Count = count;
        Parity = parity;
    }

    /// <summary>Gets the position of the symbol in the sequence, from 0 to <see cref="Count"/> - 1.</summary>
    public int Index { get; }

    /// <summary>Gets the number of symbols of the sequence, from 1 to 16.</summary>
    public int Count { get; }

    /// <summary>
    /// Gets the parity of the data: the XOR of all its bytes, the same in every symbol of the sequence, so that readers
    /// do not mix the symbols of two sequences.
    /// </summary>
    public byte Parity { get; }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Index + 1}/{Count}");
}
