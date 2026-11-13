namespace Meziantou.Framework.Barcodes.Internal.Linear;

/// <summary>
/// Decodes the data bits of a GS1 DataBar Expanded symbol (ISO/IEC 24724, 7.2.5): the encodation method and its
/// compressed fields (GTIN, weight, date, price), then the general-purpose field in numeric, alphanumeric and ISO/IEC 646
/// modes. Returns the human-readable interpretation of the element strings.
/// </summary>
internal static class DataBarExpandedBitDecoder
{
    private const char Fnc1 = '\u001D';

    // The ISO/IEC 646 characters of the 8-bit codes 232 to 252
    private const string Iso646Punctuation = "!\"%&'()*+,-./:;<=>?_ ";

    public static string? Decode(List<bool> bits, int symbolCharacters)
    {
        // The variable length field of the methods that have one: the parity of the number of symbol characters, and
        // whether there are more than 14 (ISO/IEC 24724, 7.2.5.4.1)
        bool HasValidLengthField(int position) => Peek(bits, position, 2) == (((symbolCharacters % 2) << 1) | (symbolCharacters > 14 ? 1 : 0));

        var position = 1; // the linkage flag
        var data = new StringBuilder();
        if (Read(bits, ref position, 1) is 1)
        {
            // Method 1: (01), the variable length field, the indicator digit, then the 12 next digits in groups of 3
            if (!HasValidLengthField(position))
                return null;

            position += 2;
            var indicator = Read(bits, ref position, 4);
            if (indicator is < 0 or > 9 || !TryAppendGtin(bits, ref position, (char)('0' + indicator), data))
                return null;
        }
        else if (Peek(bits, position, 1) is 0)
        {
            // Method 00: general-purpose data only, after the variable length field
            if (!HasValidLengthField(position + 1))
                return null;

            position += 3;
        }
        else
        {
            position--;
            var method4 = Peek(bits, position, 4);
            var method5 = Peek(bits, position, 5);
            if (method4 is 0b0100 or 0b0101)
            {
                // (01) of a variable measure trade item and its weight in kilograms (3103) or pounds (3202, 3203): 60 bits,
                // 5 data characters
                if (symbolCharacters != 6)
                    return null;

                position += 4;
                if (!TryAppendGtin(bits, ref position, '9', data))
                    return null;

                var weight = Read(bits, ref position, 15);
                if (weight < 0)
                    return null;

                if (method4 is 0b0100)
                {
                    data.Append("3103").Append(weight.ToString("D6", CultureInfo.InvariantCulture));
                }
                else
                {
                    data.Append(weight < 10000 ? "3202" : "3203").Append((weight < 10000 ? weight : weight - 10000).ToString("D6", CultureInfo.InvariantCulture));
                }
            }
            else if (method5 is 0b01100 or 0b01101)
            {
                // (01) and a price (392n), or a price with its ISO 4217 currency code (393n): the price is general-purpose data
                if (!HasValidLengthField(position + 5))
                    return null;

                position += 5 + 2;
                if (!TryAppendGtin(bits, ref position, '9', data))
                    return null;

                var decimals = Read(bits, ref position, 2);
                if (decimals < 0)
                    return null;

                data.Append(method5 is 0b01100 ? "392" : "393").Append((char)('0' + decimals));
                if (method5 is 0b01101)
                {
                    var currency = Read(bits, ref position, 10);
                    if (currency is < 0 or > 999)
                        return null;

                    data.Append(currency.ToString("D3", CultureInfo.InvariantCulture));
                }
            }
            else if (Peek(bits, position, 4) is 0b0111)
            {
                // (01), a weight (310n or 320n) and a date (11, 13, 15, 17): methods 0111000 to 0111111, 84 bits, 7 data characters
                if (symbolCharacters != 8)
                    return null;

                position += 4;
                var dateCode = Read(bits, ref position, 2);
                var isPounds = Read(bits, ref position, 1) is 1;
                if (dateCode < 0 || !TryAppendGtin(bits, ref position, '9', data))
                    return null;

                var weightField = Read(bits, ref position, 20);
                var date = Read(bits, ref position, 16);
                if (weightField < 0 || date < 0 || weightField / 100000 > 9)
                    return null;

                data.Append(isPounds ? "320" : "310").Append((char)('0' + (weightField / 100000))).Append((weightField % 100000).ToString("D6", CultureInfo.InvariantCulture));
                if (date != 38400)
                {
                    var year = date / 384;
                    var month = ((date % 384) / 32) + 1;
                    var day = date % 32;
                    if (year > 99 || month > 12)
                        return null;

                    data.Append(dateCode switch { 0 => "11", 1 => "13", 2 => "15", _ => "17" })
                        .Append(year.ToString("D2", CultureInfo.InvariantCulture))
                        .Append(month.ToString("D2", CultureInfo.InvariantCulture))
                        .Append(day.ToString("D2", CultureInfo.InvariantCulture));
                }
            }
            else
            {
                return null;
            }
        }

        if (!TryDecodeGeneralPurpose(bits, position, data))
            return null;

        var raw = data.ToString().TrimEnd(Fnc1);
        if (raw.Length is 0)
            return null;

        return Gs1ElementStrings.FormatHumanReadable(raw) ?? raw;
    }

    // The GTIN: the indicator digit, 12 digits in 4 groups of 10 bits, and its check digit, which the symbol omits
    private static bool TryAppendGtin(List<bool> bits, ref int position, char indicator, StringBuilder data)
    {
        Span<char> gtin = stackalloc char[14];
        gtin[0] = indicator;
        for (var i = 0; i < 4; i++)
        {
            var value = Read(bits, ref position, 10);
            if (value is < 0 or > 999)
                return false;

            gtin[1 + (i * 3)] = (char)('0' + (value / 100));
            gtin[2 + (i * 3)] = (char)('0' + (value / 10 % 10));
            gtin[3 + (i * 3)] = (char)('0' + (value % 10));
        }

        gtin[13] = (char)('0' + DataBarHelper.ComputeGtinCheckDigit(gtin[..13]));
        data.Append("01").Append(gtin);
        return true;
    }

    private enum Mode
    {
        Numeric,
        Alphanumeric,
        Iso646,
    }

    // Section 7.2.5.5. The data ends when the bits left do not make a complete value: the padding is a latch to the
    // alphanumeric mode, then latches between the alphanumeric and ISO/IEC 646 modes.
    private static bool TryDecodeGeneralPurpose(List<bool> bits, int position, StringBuilder data)
    {
        var mode = Mode.Numeric;
        while (position < bits.Count)
        {
            var remaining = bits.Count - position;
            switch (mode)
            {
                case Mode.Numeric:
                    if (remaining < 4)
                        return true;

                    if (Peek(bits, position, 4) is 0)
                    {
                        position += 4;
                        mode = Mode.Alphanumeric;
                        continue;
                    }

                    if (remaining < 7)
                    {
                        // A last digit in 4 bits
                        var digit = Peek(bits, position, 4) - 1;
                        if (digit is >= 0 and <= 9)
                        {
                            data.Append((char)('0' + digit));
                        }

                        return true;
                    }

                    var value = Read(bits, ref position, 7) - 8;
                    if (value is < 0 or > 120)
                        return false;

                    AppendNumeric(data, value / 11);
                    AppendNumeric(data, value % 11);
                    break;

                case Mode.Alphanumeric:
                case Mode.Iso646:
                    if (remaining < 3)
                        return true;

                    if (Peek(bits, position, 3) is 0)
                    {
                        position += 3;
                        mode = Mode.Numeric;
                        continue;
                    }

                    if (remaining < 5)
                        return true;

                    var value5 = Peek(bits, position, 5);
                    if (value5 is 0b00100)
                    {
                        position += 5;
                        mode = mode is Mode.Alphanumeric ? Mode.Iso646 : Mode.Alphanumeric;
                        continue;
                    }

                    if (value5 is 0b01111)
                    {
                        position += 5;
                        data.Append(Fnc1);
                        mode = Mode.Numeric;
                        continue;
                    }

                    if (value5 is >= 5 and <= 14)
                    {
                        position += 5;
                        data.Append((char)('0' + value5 - 5));
                        continue;
                    }

                    if (mode is Mode.Alphanumeric)
                    {
                        if (remaining < 6)
                            return true;

                        var value6 = Read(bits, ref position, 6);
                        switch (value6)
                        {
                            case >= 32 and <= 57:
                                data.Append((char)('A' + value6 - 32));
                                break;
                            case >= 58 and <= 62:
                                data.Append("*,-./"[value6 - 58]);
                                break;
                            default:
                                return false;
                        }

                        continue;
                    }

                    if (remaining < 7)
                        return true;

                    var value7 = Peek(bits, position, 7);
                    if (value7 is >= 64 and <= 115)
                    {
                        position += 7;
                        data.Append((char)(value7 < 90 ? 'A' + value7 - 64 : 'a' + value7 - 90));
                        continue;
                    }

                    if (remaining < 8)
                        return true;

                    var value8 = Read(bits, ref position, 8);
                    if (value8 is < 232 or > 252)
                        return false;

                    data.Append(Iso646Punctuation[value8 - 232]);
                    break;
            }
        }

        return true;
    }

    private static void AppendNumeric(StringBuilder data, int value)
    {
        data.Append(value is 10 ? Fnc1 : (char)('0' + value));
    }

    private static int Peek(List<bool> bits, int position, int count)
    {
        if (position + count > bits.Count)
            return -1;

        var value = 0;
        for (var i = 0; i < count; i++)
        {
            value = (value << 1) | (bits[position + i] ? 1 : 0);
        }

        return value;
    }

    private static int Read(List<bool> bits, ref int position, int count)
    {
        var value = Peek(bits, position, count);
        if (value >= 0)
        {
            position += count;
        }

        return value;
    }
}
