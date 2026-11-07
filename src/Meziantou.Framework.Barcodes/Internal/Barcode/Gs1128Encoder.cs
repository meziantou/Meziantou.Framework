namespace Meziantou.Framework.Barcodes.Internal.Linear;

// GS1 General Specifications, section 5.4 (GS1-128) and section 7.8 (element strings)
internal static class Gs1128Encoder
{
    // The data characters of a GS1-128 symbol, application identifiers and FNC1 separators included, leading FNC1 excluded
    private const int MaxDataCharacters = 48;

    public static Barcode Encode(ReadOnlySpan<Gs1Element> elements)
    {
        var data = new StringBuilder();
        var text = new StringBuilder();
        data.Append(Code128Encoder.Fnc1);
        for (var i = 0; i < elements.Length; i++)
        {
            var element = elements[i];
            if (element is null)
                throw new ArgumentException("The elements cannot contain null.", nameof(elements));

            data.Append(element.ApplicationIdentifier).Append(element.Value);
            text.Append(element.ToString());

            // A reader knows where an element of predefined length ends; the others end with a FNC1, except the last one
            if (i < elements.Length - 1 && GetPredefinedValueLength(element.ApplicationIdentifier) < 0)
            {
                data.Append(Code128Encoder.Fnc1);
            }
        }

        if (data.Length - 1 > MaxDataCharacters)
            throw new ArgumentException($"A GS1-128 symbol holds at most {MaxDataCharacters} characters, application identifiers and separators included; the elements need {data.Length - 1}.", nameof(elements));

        return Code128Encoder.Encode(data.ToString(), BarcodeType.Gs1128, text.ToString());
    }

    public static void Validate(string applicationIdentifier, string value)
    {
        if (applicationIdentifier.Length is < 2 or > 4 || !applicationIdentifier.All(char.IsAsciiDigit))
            throw new ArgumentException($"The application identifier '{applicationIdentifier}' must be 2 to 4 digits.", nameof(applicationIdentifier));

        var expectedIdentifierLength = GetPredefinedIdentifierLength(applicationIdentifier);
        if (expectedIdentifierLength > 0 && applicationIdentifier.Length != expectedIdentifierLength)
            throw new ArgumentException($"The application identifiers starting with '{applicationIdentifier[..2]}' have {expectedIdentifierLength} digits.", nameof(applicationIdentifier));

        if (value.Length == 0)
            throw new ArgumentException("The data of an element string cannot be empty.", nameof(value));

        foreach (var character in value)
        {
            if (!IsCharacterSet82(character))
                throw new ArgumentException($"The character '{character}' is not in the GS1 AI encodable character set 82.", nameof(value));
        }

        var expectedValueLength = GetPredefinedValueLength(applicationIdentifier);
        if (expectedValueLength > 0 && (value.Length != expectedValueLength || !value.All(char.IsAsciiDigit)))
            throw new ArgumentException($"The data of the application identifier ({applicationIdentifier}) must be {expectedValueLength} digits.", nameof(value));
    }

    // The application identifiers whose element strings have a predefined length (GS1 General Specifications,
    // Figure 7.8.5-2): a reader knows where they end, so no FNC1 follows them. Returns -1 for the other identifiers.
    private static int GetPredefinedValueLength(string applicationIdentifier)
    {
        return applicationIdentifier[..2] switch
        {
            "00" => 18,
            "01" or "02" or "03" => 14,
            "04" => 16,
            "11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" => 6,
            "20" => 2,
            "31" or "32" or "33" or "34" or "35" or "36" => 6,
            "41" => 13,
            _ => -1,
        };
    }

    private static int GetPredefinedIdentifierLength(string applicationIdentifier)
    {
        return applicationIdentifier[..2] switch
        {
            "00" or "01" or "02" or "03" or "04" or "11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" => 2,
            "31" or "32" or "33" or "34" or "35" or "36" => 4,
            "41" => 3,
            _ => -1,
        };
    }

    // GS1 General Specifications, Figure 7.11-1
    private static bool IsCharacterSet82(char character)
    {
        return character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
            or '!' or '"' or '%' or '&' or '\'' or '(' or ')' or '*' or '+' or ',' or '-' or '.' or '/'
            or ':' or ';' or '<' or '=' or '>' or '?' or '_';
    }
}
