using System.Text.Json;
using Meziantou.Framework.Barcodes.Internal.Gs1;

namespace Meziantou.Framework.Barcodes.Tests;

public class Gs1DictionaryTests
{
    [Fact]
    public void Gs1Element_Dictionary()
    {
        #region Gs1Element_Dictionary
        // The syntax rules accept any data of 14 digits for a GTIN
        var element = new Gs1Element("01", "09521234543214");
        Assert.Equal("(01)09521234543214", element.ToString());

        // The GS1 dictionary also checks the check digit, the dates, the country codes, ...
        var exception = Assert.Throws<ArgumentException>("value", () => new Gs1Element("01", "09521234543214", Gs1ValidationMode.Dictionary));
        Assert.Contains("The numeric check digit is incorrect.", exception.Message);

        var gtin = new Gs1Element("01", "09521234543213", Gs1ValidationMode.Dictionary);
        var expiry = new Gs1Element("17", "250100", Gs1ValidationMode.Dictionary); // day 00: the end of the month
        var barcode = Barcode.CreateGs1128(gtin, expiry);
        Assert.Equal("(01)09521234543213(17)250100", barcode.Text);
        #endregion
    }

    [Fact]
    public void Gs1Element_ValidateAssociations()
    {
        #region Gs1Element_ValidateAssociations
        var gtin = new Gs1Element("01", "09521234543213", Gs1ValidationMode.Dictionary);
        var batch = new Gs1Element("10", "ABC123", Gs1ValidationMode.Dictionary);
        Gs1Element.ValidateAssociations(gtin, batch);

        // A batch number qualifies a trade item: it requires a GTIN
        Assert.Throws<ArgumentException>("elements", () => Gs1Element.ValidateAssociations(batch));
        #endregion
    }

    [Theory]
    [InlineData("00", "123456789012345675")]
    [InlineData("01", "09521234543213")]
    [InlineData("02", "09521234543213")]
    [InlineData("10", "ABC-123/4")]
    [InlineData("17", "240229")] // 2024 is a leap year
    [InlineData("17", "251200")]
    [InlineData("21", "12345678901234567890")]
    [InlineData("253", "4000000000006")] // optional serial component missing
    [InlineData("253", "4000000000006ABC")]
    [InlineData("3103", "000123")]
    [InlineData("3922", "1234")]
    [InlineData("3932", "978123")] // ISO 4217 currency code 978 (euro)
    [InlineData("421", "250ABC")] // ISO 3166 country code 250 (France)
    [InlineData("4307", "FR")]
    [InlineData("4309", "18000000003600000000")]
    [InlineData("4321", "1")]
    [InlineData("4324", "2501312359")]
    [InlineData("4330", "000123-")]
    [InlineData("7003", "2501311200")]
    [InlineData("7007", "250101250131")]
    [InlineData("7240", "PROTOCOL")]
    [InlineData("7258", "1/2")]
    [InlineData("8001", "12341234512319")]
    [InlineData("8003", "012345678901280")]
    [InlineData("8006", "095212345432130102")]
    [InlineData("8007", "GB82WEST12345698765432")]
    [InlineData("8008", "25013112")]
    [InlineData("8008", "2501311259")]
    [InlineData("8008", "250131125959")]
    [InlineData("8010", "1234#ABC-/")] // CSET 39
    [InlineData("8011", "123")]
    [InlineData("8013", "1987654Ad4X4bL5ttr2310c2K")]
    [InlineData("8030", "QUJDREVGR0g=")]
    [InlineData("8110", "012345612345611110123")]
    [InlineData("91", "anything")]
    public void Gs1Element_Dictionary_ValidValue(string applicationIdentifier, string value)
    {
        var element = new Gs1Element(applicationIdentifier, value, Gs1ValidationMode.Dictionary);

        Assert.Equal(applicationIdentifier, element.ApplicationIdentifier);
        Assert.Equal(value, element.Value);
    }

    [Theory]
    [InlineData("00", "123456789012345670", "The numeric check digit is incorrect.")]
    [InlineData("01", "09521234543214", "The numeric check digit is incorrect.")]
    [InlineData("11", "251301", "The date contains an illegal month of the year.")]
    [InlineData("17", "250229", "The date contains an illegal day of the month.")]
    [InlineData("17", "250431", "The date contains an illegal day of the month.")]
    [InlineData("10", "123456789012345678901", "The data is too long")]
    [InlineData("242", "1234567", "The data is too long")]
    [InlineData("242", "12345A", "A non-digit character was found where a digit is expected.")]
    [InlineData("421", "25", "The data is too short")]
    [InlineData("421", "999ABC", "A valid ISO 3166 three-digit country code is required.")]
    [InlineData("3932", "000123", "A valid ISO 4217 three-digit currency code is required.")]
    [InlineData("4307", "XX", "A valid ISO 3166 two-character country code is required.")]
    [InlineData("4309", "18000000013600000000", "The latitude is outside of the range")]
    [InlineData("4321", "2", "A \"0\" or \"1\" is required.")]
    [InlineData("4324", "2501312460", "The time contains an illegal hour.")]
    [InlineData("4330", "000123+", "Only hyphens are permitted.")]
    [InlineData("7258", "3/2", "The position number must not exceed the end number.")]
    [InlineData("8001", "00001234512319", "A non-zero value is required.")]
    [InlineData("8003", "112345678901280", "A zero is required.")]
    [InlineData("8006", "095212345432130302", "The piece number must not exceed the piece total.")]
    [InlineData("8007", "GB82WEST12345698765431", "The IBAN is invalid since the check characters are incorrect.")]
    [InlineData("8010", "1234abc", "A non-CSET 39 character")]
    [InlineData("8011", "0123", "A zero prefix is not permitted.")]
    [InlineData("8013", "1987654Ad4X4bL5ttr2310c2L", "The alphanumeric check-character pair are incorrect.")]
    [InlineData("8030", "QUJDREVGR0g==", "Incorrect number of CSET 64 pad characters.")]
    [InlineData("8110", "0123456123456111101237", "The coupon contains excess data")]
    public void Gs1Element_Dictionary_InvalidValue_ThrowsArgumentException(string applicationIdentifier, string value, string message)
    {
        var exception = Assert.Throws<ArgumentException>(nameof(value), () => new Gs1Element(applicationIdentifier, value, Gs1ValidationMode.Dictionary));

        Assert.Contains(message, exception.Message);
        Assert.Contains("(" + applicationIdentifier + ")", exception.Message);

        // The syntax rules do not check the format of the data
        _ = new Gs1Element(applicationIdentifier, value);
    }

    [Fact]
    public void Gs1Element_Dictionary_ReportsThePositionOfTheError()
    {
        var exception = Assert.Throws<ArgumentException>("value", () => new Gs1Element("8008", "2501311260", Gs1ValidationMode.Dictionary));

        Assert.StartsWith("The data of the application identifier (8008) PROD TIME does not match the format N6 N2 [N2] [N2] at position 8: The time contains an illegal minute.", exception.Message);
    }

    [Theory]
    [InlineData("23")]
    [InlineData("3106")] // net weight in kilograms has 0 to 5 decimals
    [InlineData("9")]
    public void Gs1Element_Dictionary_UnknownApplicationIdentifier_ThrowsArgumentException(string applicationIdentifier)
    {
        Assert.Throws<ArgumentException>(nameof(applicationIdentifier), () => new Gs1Element(applicationIdentifier, "000123", Gs1ValidationMode.Dictionary));
    }

    [Fact]
    public void Gs1Element_InvalidValidationMode_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>("validationMode", () => new Gs1Element("01", "09521234543213", (Gs1ValidationMode)2));
    }

    [Fact]
    public void ValidateAssociations_Requirements()
    {
        var gtin = new Gs1Element("01", "09521234543213");
        var serial = new Gs1Element("21", "123");
        var secondarySerial = new Gs1Element("250", "XYZ");
        var count = new Gs1Element("37", "12");
        var sscc = new Gs1Element("00", "123456789012345675");
        var content = new Gs1Element("02", "09521234543213");

        // 250 requires (01) with (21), or (03) with (21), or (8006) with (21)
        Gs1Element.ValidateAssociations(gtin, serial, secondarySerial);
        var exception = Assert.Throws<ArgumentException>("elements", () => Gs1Element.ValidateAssociations(gtin, secondarySerial));
        Assert.Equal("The application identifier (250) requires (01) with (21), or (03) with (21), or (8006) with (21). (Parameter 'elements')", exception.Message);

        // 37 requires (00) with (02), or (00) with (8026); 02 requires 37
        Gs1Element.ValidateAssociations(sscc, content, count);
        Assert.Throws<ArgumentException>("elements", () => Gs1Element.ValidateAssociations(content, count));

        // The application identifiers that are not in the dictionary have no rules
        Gs1Element.ValidateAssociations(new Gs1Element("23", "1"));
        Gs1Element.ValidateAssociations();
    }

    [Fact]
    public void ValidateAssociations_Exclusions()
    {
        var gtin = new Gs1Element("01", "09521234543213");
        var content = new Gs1Element("02", "09521234543213");
        var count = new Gs1Element("37", "12");
        var sscc = new Gs1Element("00", "123456789012345675");
        var exception = Assert.Throws<ArgumentException>("elements", () => Gs1Element.ValidateAssociations(gtin, content, count, sscc));
        Assert.Contains("(01) and (37) cannot be used together", exception.Message);

        // ex=310n: two net weights in kilograms with different decimals
        Assert.Throws<ArgumentException>("elements", () => Gs1Element.ValidateAssociations(gtin, new Gs1Element("3102", "000123"), new Gs1Element("3103", "000123")));

        // An application identifier is not exclusive of itself
        Gs1Element.ValidateAssociations(gtin, new Gs1Element("3102", "000123"), new Gs1Element("3102", "000123"));
    }

    [Fact]
    public void ValidateAssociations_DuplicatesWithDifferentData_ThrowsArgumentException()
    {
        var gtin = new Gs1Element("01", "09521234543213");

        Gs1Element.ValidateAssociations(gtin, new Gs1Element("01", "09521234543213"));
        Assert.Throws<ArgumentException>("elements", () => Gs1Element.ValidateAssociations(gtin, new Gs1Element("01", "09521234543220")));
        Assert.Throws<ArgumentException>("elements", () => Gs1Element.ValidateAssociations(gtin, null!));
    }

    [Fact]
    public void SyntaxDictionary_ParsesEveryEntry()
    {
        var entries = Gs1SyntaxDictionary.Entries;

        Assert.Equal("N18", entries["00"].Format);
        Assert.True(entries["00"].PredefinedLength);
        Assert.Equal("SSCC", entries["00"].Title);
        Assert.Equal("X..20", entries["10"].Format);
        Assert.False(entries["10"].PredefinedLength);
        Assert.Equal("N3 [N3] [N3] [N3] [N3]", entries["423"].Format);
        Assert.Equal("N6 N2 [N2] [N2]", entries["8008"].Format);
        Assert.Equal(["N", "csum", "gcppos2"], [entries["01"].Components[0].Type.ToString(), .. entries["01"].Components[0].Linters]);

        // Ranges of application identifiers
        Assert.Contains("3100", entries.Keys);
        Assert.Contains("3105", entries.Keys);
        Assert.DoesNotContain("3106", entries.Keys);
        Assert.Contains("91", entries.Keys);
        Assert.Contains("99", entries.Keys);

        // The entries that the dictionary marks as predefined length are the ones the encoder does not end with a separator
        foreach (var entry in entries.Values)
        {
            var predefined = entry.PredefinedLength;
            var encoderPredefined = entry.ApplicationIdentifier[..2] is "00" or "01" or "02" or "03" or "04" or "11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" or "31" or "32" or "33" or "34" or "35" or "36" or "41";
            Assert.True(!predefined || encoderPredefined, $"({entry.ApplicationIdentifier}) has a predefined length in the dictionary");
        }
    }

    [Fact]
    public void Gs1Element_ComponentPartIdentifier_AcceptsHash()
    {
        var barcode = Barcode.CreateGs1128(new Gs1Element("8010", "AB#12"));
        Assert.Equal("(8010)AB#12", barcode.Text);

        Assert.Throws<ArgumentException>("value", () => new Gs1Element("10", "AB#12"));
    }

    [Theory]
    [InlineData("gcppos1", "0123", nameof(Gs1LintError.None), 0, 0)]
    [InlineData("gcppos1", "01234", nameof(Gs1LintError.None), 0, 0)]
    [InlineData("gcppos1", "0123A", nameof(Gs1LintError.None), 0, 0)]
    [InlineData("gcppos1", "", nameof(Gs1LintError.TooShortForGcp), 0, 0)]
    [InlineData("gcppos1", "012", nameof(Gs1LintError.TooShortForGcp), 0, 3)]
    [InlineData("gcppos1", "012A", nameof(Gs1LintError.InvalidGcpPrefix), 3, 1)]
    [InlineData("gcppos1", "A123", nameof(Gs1LintError.InvalidGcpPrefix), 0, 1)]
    [InlineData("gcppos2", "90123", nameof(Gs1LintError.None), 0, 0)]
    [InlineData("gcppos2", "9", nameof(Gs1LintError.TooShortForGcp), 0, 1)]
    [InlineData("gcppos2", "9012", nameof(Gs1LintError.TooShortForGcp), 1, 3)]
    [InlineData("gcppos2", "9012A", nameof(Gs1LintError.InvalidGcpPrefix), 4, 1)]
    [InlineData("cset39", "*", nameof(Gs1LintError.InvalidCset39Character), 0, 1)]
    [InlineData("cset64", "*", nameof(Gs1LintError.InvalidCset64Character), 0, 1)]
    [InlineData("importeridx", "*", nameof(Gs1LintError.InvalidImportIdxCharacter), 0, 1)]
    public void Linters_CasesTheReferenceGeneratesInLoops(string linter, string data, string error, int position, int length)
    {
        Assert.Equal(new Gs1LintResult(Enum.Parse<Gs1LintError>(error), position, length), Gs1Linters.Lint(linter, data));
    }

    // The unit tests of the reference linters of the GS1 Barcode Syntax Dictionary (see Gs1/README.md)
    [Fact]
    public void Linters_MatchTheReferenceImplementation()
    {
        using var stream = typeof(Gs1DictionaryTests).Assembly.GetManifestResourceStream("Meziantou.Framework.Barcodes.Tests.gs1-linter-tests.json")!;
        using var document = JsonDocument.Parse(stream);

        var failures = new List<string>();
        var count = 0;
        foreach (var testCase in document.RootElement.EnumerateArray())
        {
            count++;
            var linter = testCase.GetProperty("linter").GetString()!;
            var input = testCase.GetProperty("input").GetString()!;
            var expectedError = testCase.GetProperty("error").GetString() is { } name
                ? Enum.Parse<Gs1LintError>(name.Replace("_", "", StringComparison.Ordinal).Replace("EXIPIRATION", "EXPIRATION", StringComparison.Ordinal), ignoreCase: true)
                : Gs1LintError.None;

            var result = Gs1Linters.Lint(linter, input);
            var expected = expectedError is Gs1LintError.None
                ? Gs1LintResult.Ok
                : new Gs1LintResult(expectedError, testCase.GetProperty("position").GetInt32(), testCase.GetProperty("length").GetInt32());
            if (result != expected)
            {
                failures.Add($"{linter}(\"{input}\"): expected {expected}, got {result}");
            }
        }

        Assert.Empty(failures);
        Assert.True(count > 5000, $"Only {count} test cases were read");
    }
}
