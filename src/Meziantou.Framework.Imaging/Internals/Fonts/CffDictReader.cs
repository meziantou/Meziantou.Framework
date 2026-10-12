namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the entries of a DICT of a CFF table: operands followed by an operator. Adobe Technical Note #5176, section 4.
/// </summary>
internal ref struct CffDictReader
{
    /// <summary>The largest number of operands of an operator: section 4, "An operator may be preceded by up to a maximum of 48 operands".</summary>
    public const int MaxOperands = 48;

    /// <summary>The value added to the second byte of a two-byte operator, which follows the escape byte 12.</summary>
    public const int Escape = 0x0C00;

    /// <summary>The operator of a CFF2 DICT that replaces operands and their deltas by the values of the default instance.</summary>
    private const int BlendOperator = 23;

    private readonly ReadOnlySpan<byte> _data;
    private readonly bool _isCff2;
    private int _position;

    public CffDictReader(ReadOnlySpan<byte> data, bool isCff2 = false)
    {
        _data = data;
        _isCff2 = isCff2;
        _position = 0;
    }

    /// <summary>Reads the next entry.</summary>
    /// <param name="operands">Receives the operands; at least <see cref="MaxOperands"/> values.</param>
    /// <param name="count">The number of operands.</param>
    /// <param name="operator">The operator; a two-byte operator is <see cref="Escape"/> plus its second byte.</param>
    /// <returns><see langword="false"/> at the end of the DICT.</returns>
    public bool TryRead(scoped Span<double> operands, out int count, out int @operator)
    {
        count = 0;
        while (_position < _data.Length)
        {
            var b0 = _data[_position++];
            if (b0 <= 21 || (_isCff2 && b0 <= 24))
            {
                if (_isCff2 && b0 == BlendOperator)
                {
                    // The blended values are hints, which are not read: the operands of the entry start again.
                    count = 0;
                    continue;
                }

                @operator = b0 == 12 ? Escape + FontBinary.UInt8(_data, _position++) : b0;
                return true;
            }

            var operand = ReadOperand(b0);
            if (count < operands.Length)
            {
                operands[count++] = operand;
            }
            else if (!_isCff2)
            {
                throw FontErrors.InvalidTable(FontTags.Cff, "has a DICT entry with too many operands.");
            }
        }

        if (count != 0)
            throw FontErrors.InvalidTable(FontTags.Cff, "has a DICT that ends with operands without operator.");

        @operator = 0;
        return false;
    }

    /// <summary>Reads an operand: table 3, "Operand Encoding", and table 5 for the real numbers.</summary>
    private double ReadOperand(int b0)
    {
        switch (b0)
        {
            case >= 32 and <= 246:
                return b0 - 139;

            case >= 247 and <= 250:
                return ((b0 - 247) * 256) + FontBinary.UInt8(_data, _position++) + 108;

            case >= 251 and <= 254:
                return (-(b0 - 251) * 256) - FontBinary.UInt8(_data, _position++) - 108;

            case 28:
                _position += 2;
                return FontBinary.Int16(_data, _position - 2);

            case 29:
                _position += 4;
                return FontBinary.Int32(_data, _position - 4);

            case 30:
                return ReadReal();

            default:
                throw FontErrors.InvalidTable(FontTags.Cff, $"has a DICT with the reserved byte {b0}.");
        }
    }

    /// <summary>Reads a real number: a sequence of 4-bit digits and marks, ended by the value 15.</summary>
    private double ReadReal()
    {
        Span<char> text = stackalloc char[64];
        var length = 0;
        while (true)
        {
            var value = FontBinary.UInt8(_data, _position++);
            for (var shift = 4; shift >= 0; shift -= 4)
            {
                var nibble = (value >> shift) & 0x0F;
                if (nibble == 0x0F)
                {
                    if (double.TryParse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result))
                        return result;

                    throw FontErrors.InvalidTable(FontTags.Cff, "has a DICT with a malformed real number.");
                }

                if (length > text.Length - 2)
                    throw FontErrors.InvalidTable(FontTags.Cff, "has a DICT with a real number that is too long.");

                switch (nibble)
                {
                    case <= 9:
                        text[length++] = (char)('0' + nibble);
                        break;

                    case 0x0A:
                        text[length++] = '.';
                        break;

                    case 0x0B:
                        text[length++] = 'E';
                        break;

                    case 0x0C:
                        text[length++] = 'E';
                        text[length++] = '-';
                        break;

                    case 0x0E:
                        text[length++] = '-';
                        break;

                    default:
                        throw FontErrors.InvalidTable(FontTags.Cff, "has a DICT with a malformed real number.");
                }
            }
        }
    }
}
