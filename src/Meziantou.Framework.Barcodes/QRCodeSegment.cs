using Meziantou.Framework.Barcodes.Internal;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Represents a run of data encoded in one mode, with its own mode indicator and character count indicator, to choose
/// the segments of a QR code yourself.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="QRCode.Create(string, ErrorCorrectionLevel, QRCodeOptions)"/> splits a text into segments by itself. Pass
/// segments to <see cref="QRCode.Create(IEnumerable{QRCodeSegment}, ErrorCorrectionLevel, QRCodeOptions)"/> to choose
/// them: to encode a text that the automatic segmentation keeps in one byte segment, to control the bytes of a byte
/// segment, or to declare their encoding with an ECI designator.
/// </para>
/// <para>
/// Each segment costs a mode indicator and a character count indicator: 4 + 8 to 4 + 16 bits in a standard QR code,
/// fewer in Micro QR and rMQR codes. A short segment can take more bits than the characters it saves.
/// </para>
/// </remarks>
public sealed class QRCodeSegment
{
    private readonly string? _text;
    private readonly byte[]? _bytes;
    private readonly int _eciAssignmentNumber;

    private QRCodeSegment(EncodingMode mode, string? text, byte[]? bytes, int eciAssignmentNumber)
    {
        EncodingMode = mode;
        _text = text;
        _bytes = bytes;
        _eciAssignmentNumber = eciAssignmentNumber;
    }

    /// <summary>Gets the mode of the segment.</summary>
    public QRCodeSegmentMode Mode => EncodingMode switch
    {
        EncodingMode.Numeric => QRCodeSegmentMode.Numeric,
        EncodingMode.Alphanumeric => QRCodeSegmentMode.Alphanumeric,
        EncodingMode.Byte => QRCodeSegmentMode.Byte,
        EncodingMode.Kanji => QRCodeSegmentMode.Kanji,
        _ => QRCodeSegmentMode.Eci,
    };

    internal EncodingMode EncodingMode { get; }

    /// <summary>Gets the number of units of the segment: its characters, its bytes in byte mode, or 0 for an ECI designator.</summary>
    internal int UnitCount => EncodingMode switch
    {
        EncodingMode.Byte => _bytes!.Length,
        EncodingMode.Eci => 0,
        _ => _text!.Length,
    };

    /// <summary>
    /// Creates a segment of digits, in numeric mode.
    /// </summary>
    /// <param name="digits">The digits, from <c>0</c> to <c>9</c>.</param>
    /// <returns>A new <see cref="QRCodeSegment"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="digits"/> is empty or has a character that is not a digit.</exception>
    public static QRCodeSegment CreateNumeric(string digits)
    {
        ValidateText(digits, nameof(digits));
        foreach (var c in digits)
        {
            if (c is < '0' or > '9')
                throw new ArgumentException($"The character '{c}' is not a digit: numeric mode encodes the digits 0 to 9 only.", nameof(digits));
        }

        return new QRCodeSegment(EncodingMode.Numeric, digits, bytes: null, eciAssignmentNumber: 0);
    }

    /// <summary>
    /// Creates a segment of digits, uppercase letters, and the characters <c> $%*+-./:</c>, in alphanumeric mode.
    /// </summary>
    /// <param name="text">The characters.</param>
    /// <returns>A new <see cref="QRCodeSegment"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty or has a character that alphanumeric mode cannot encode.</exception>
    public static QRCodeSegment CreateAlphanumeric(string text)
    {
        ValidateText(text, nameof(text));
        foreach (var c in text)
        {
            if (!DataAnalyzer.IsAlphanumeric(c))
                throw new ArgumentException($"The character '{c}' cannot be encoded in alphanumeric mode, which encodes the digits, the uppercase letters A to Z, and \" $%*+-./:\".", nameof(text));
        }

        return new QRCodeSegment(EncodingMode.Alphanumeric, text, bytes: null, eciAssignmentNumber: 0);
    }

    /// <summary>
    /// Creates a segment of a text encoded as UTF-8, in byte mode.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A new <see cref="QRCodeSegment"/> instance.</returns>
    /// <remarks>
    /// Readers guess the encoding of the bytes unless an ECI designator declares it: precede the segment with
    /// <c>CreateEci(QRCodeOptions.Utf8EciAssignmentNumber)</c> when the text is not ASCII.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty.</exception>
    public static QRCodeSegment CreateByte(string text)
    {
        ValidateText(text, nameof(text));
        return new QRCodeSegment(EncodingMode.Byte, text: null, Encoding.UTF8.GetBytes(text), eciAssignmentNumber: 0);
    }

    /// <summary>
    /// Creates a segment of bytes, in byte mode.
    /// </summary>
    /// <param name="data">The bytes, copied.</param>
    /// <returns>A new <see cref="QRCodeSegment"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty.</exception>
    public static QRCodeSegment CreateByte(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            throw new ArgumentException("The data cannot be empty.", nameof(data));

        return new QRCodeSegment(EncodingMode.Byte, text: null, data.ToArray(), eciAssignmentNumber: 0);
    }

    /// <summary>
    /// Creates a segment of kanji and kana, in Kanji mode (their Shift JIS codes).
    /// </summary>
    /// <param name="text">The characters.</param>
    /// <returns>A new <see cref="QRCodeSegment"/> instance.</returns>
    /// <remarks>
    /// Kanji mode encodes the characters of the JIS X 0208 character set that every reader decodes the same way. It does
    /// not encode the half-width katakana, nor the characters that the variants of Shift JIS decode differently, such as
    /// <c>～</c> and <c>－</c>: encode them in byte mode.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty or has a character that Kanji mode cannot encode.</exception>
    public static QRCodeSegment CreateKanji(string text)
    {
        ValidateText(text, nameof(text));
        foreach (var c in text)
        {
            if (!DataAnalyzer.IsKanji(c))
                throw new ArgumentException($"The character '{c}' (U+{(int)c:X4}) cannot be encoded in Kanji mode.", nameof(text));
        }

        return new QRCodeSegment(EncodingMode.Kanji, text, bytes: null, eciAssignmentNumber: 0);
    }

    /// <summary>
    /// Creates an ECI (Extended Channel Interpretation) designator, which declares the character encoding of the byte
    /// segments that follow it, such as <see cref="QRCodeOptions.Utf8EciAssignmentNumber"/> for UTF-8.
    /// </summary>
    /// <param name="assignmentNumber">The ECI assignment number, from 0 to 999999.</param>
    /// <returns>A new <see cref="QRCodeSegment"/> instance.</returns>
    /// <remarks>Only standard QR codes have ECI designators: Micro QR and rMQR codes reject them.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="assignmentNumber"/> is negative or greater than 999999.</exception>
    public static QRCodeSegment CreateEci(int assignmentNumber)
    {
        if (assignmentNumber is < 0 or > 999999)
            throw new ArgumentOutOfRangeException(nameof(assignmentNumber), assignmentNumber, "The ECI assignment number must be between 0 and 999999.");

        return new QRCodeSegment(EncodingMode.Eci, text: null, bytes: null, assignmentNumber);
    }

    /// <summary>Creates a segment from characters that the caller has checked.</summary>
    internal static QRCodeSegment FromValidatedText(EncodingMode mode, string text)
    {
        return mode is EncodingMode.Byte
            ? new QRCodeSegment(mode, text: null, Encoding.UTF8.GetBytes(text), eciAssignmentNumber: 0)
            : new QRCodeSegment(mode, text, bytes: null, eciAssignmentNumber: 0);
    }

    /// <summary>Gets the units from <paramref name="start"/>, as a segment of the same mode.</summary>
    internal QRCodeSegment Slice(int start, int length)
    {
        if (start is 0 && length == UnitCount)
            return this;

        return EncodingMode is EncodingMode.Byte
            ? new QRCodeSegment(EncodingMode, text: null, _bytes.AsSpan(start, length).ToArray(), eciAssignmentNumber: 0)
            : new QRCodeSegment(EncodingMode, _text!.Substring(start, length), bytes: null, eciAssignmentNumber: 0);
    }

    /// <summary>
    /// Gets the number of bits of the segment with its mode and character count indicators, or -1 when the symbol does
    /// not support its mode or its count does not fit in its indicator.
    /// </summary>
    internal int GetBitCount(QRCodeType type, int version)
    {
        if (EncodingMode is EncodingMode.Eci)
            return type is QRCodeType.Standard ? DataEncoder.GetEciBitCount(_eciAssignmentNumber) : -1;

        var countBits = QRSymbolFormat.GetCharacterCountBits(type, version, EncodingMode);
        if (countBits < 0 || UnitCount >= (1 << countBits))
            return -1;

        var unitCount = UnitCount;
        var dataBits = EncodingMode switch
        {
            EncodingMode.Numeric => (unitCount / 3 * 10) + ((unitCount % 3) switch { 2 => 7, 1 => 4, _ => 0 }),
            EncodingMode.Alphanumeric => (unitCount / 2 * 11) + (unitCount % 2 * 6),
            EncodingMode.Byte => unitCount * 8,
            _ => unitCount * 13,
        };

        return QRSymbolFormat.GetModeIndicatorBits(type, version) + countBits + dataBits;
    }

    internal void Write(BitBuffer buffer, QRCodeType type, int version)
    {
        if (EncodingMode is EncodingMode.Eci)
        {
            DataEncoder.AppendEci(buffer, _eciAssignmentNumber);
            return;
        }

        buffer.Append(QRSymbolFormat.GetModeIndicatorValue(type, version, EncodingMode), QRSymbolFormat.GetModeIndicatorBits(type, version));
        buffer.Append(UnitCount, QRSymbolFormat.GetCharacterCountBits(type, version, EncodingMode));
        if (EncodingMode is EncodingMode.Byte)
        {
            foreach (var b in _bytes!)
            {
                buffer.Append(b, 8);
            }
        }
        else
        {
            DataEncoderHelper.EncodeData(buffer, _text!, EncodingMode);
        }
    }

    /// <summary>Gets the XOR of the bytes of the data, for the parity of a structured append sequence (ISO/IEC 18004, 8.3).</summary>
    internal byte GetParity()
    {
        var parity = 0;
        switch (EncodingMode)
        {
            case EncodingMode.Byte:
                foreach (var b in _bytes!)
                {
                    parity ^= b;
                }

                break;

            case EncodingMode.Kanji:
                foreach (var b in DataAnalyzer.GetShiftJisBytes(_text!))
                {
                    parity ^= b;
                }

                break;

            case EncodingMode.Numeric or EncodingMode.Alphanumeric:
                foreach (var c in _text!)
                {
                    parity ^= c;
                }

                break;
        }

        return (byte)parity;
    }

    private static void ValidateText(string text, string paramName)
    {
        ArgumentNullException.ThrowIfNull(text, paramName);
        if (text.Length == 0)
            throw new ArgumentException("The data cannot be empty.", paramName);
    }
}
