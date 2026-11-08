using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Framework.Barcodes.Internal.Gs1;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Barcodes.Fuzz;

/// <summary>
/// The GS1 linters must report an error inside the data, agree with each other (a date with a day is a date that may have
/// day 00, a valid check digit becomes invalid when it changes), and the element strings checked against the dictionary
/// must also follow the syntax rules, report positions inside the data, and encode as GS1-128 with the same text.
/// </summary>
internal static partial class Gs1FuzzTargets
{
    private static readonly string[] LinterNames =
    [
        "csetnumeric", "cset82", "cset39", "cset64", "csum", "csumalpha", "gcppos1", "gcppos2", "couponcode", "couponposoffer",
        "hasnondigit", "hh", "mi", "ss", "hhmi", "hyphen", "iban", "importeridx", "iso3166", "iso3166999", "iso3166alpha2",
        "iso4217", "iso5218", "latitude", "longitude", "mediatype", "nonzero", "nozeroprefix", "packagetype", "pcenc",
        "pieceoftotal", "posinseqslash", "winding", "yesno", "yymmd0", "yymmdd", "yyyymmd0", "yyyymmdd", "zero",
    ];

    private static readonly string[] Tokens =
    [
        "00", "01", "02", "10", "11", "17", "21", "250", "253", "3103", "3932", "37", "421", "4307", "4309", "4330", "7003", "7258",
        "8001", "8003", "8006", "8007", "8008", "8010", "8011", "8013", "8030", "8110", "8112", "91",
        "0123456789", "250131", "251200", "240229", "1259", "2360", "/", "%2F", "%", "=", "==", "#", "-", "999", "250", "978", "FR", "GB82WEST",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("barcodes-gs1-linters", LintersTarget) { Seeds = GetLinterSeeds, Dictionary = Tokens },
        new("barcodes-gs1-element", ElementTarget) { Seeds = GetElementSeeds, Dictionary = Tokens },
    ];

    private static void LintersTarget(ReadOnlySpan<byte> data)
    {
        var name = FuzzInput.Pick(LinterNames, FuzzInput.ReadByte(ref data));
        var text = FuzzInput.ReadText(data);
        var result = Gs1Linters.Lint(name, text);

        FuzzAssert.Equal(result.Error is Gs1LintError.None, result.IsValid, "IsValid is the absence of error");
        if (!result.IsValid)
        {
            FuzzAssert.True(result.Position >= 0 && result.Length >= 0 && result.Position + result.Length <= text.Length, $"The error {result} is outside the data of {text.Length} characters");
        }

        FuzzAssert.Equal(result, Gs1Linters.Lint(name, text), "The linters are deterministic");

        switch (name)
        {
            case "csum" when result.IsValid:
                var changed = string.Concat(text.AsSpan(0, text.Length - 1), ((char)('0' + ((text[^1] - '0' + 1) % 10))).ToString());
                FuzzAssert.Equal(Gs1LintError.IncorrectCheckDigit, Gs1Linters.Lint(name, changed).Error, "Another check digit is incorrect");
                break;

            case "yymmdd" when result.IsValid:
                FuzzAssert.True(Gs1Linters.Lint("yymmd0", text).IsValid, "A date with a day may have day 00");
                break;

            case "yyyymmdd" when result.IsValid:
                FuzzAssert.True(Gs1Linters.Lint("yyyymmd0", text).IsValid, "A date with a day may have day 00");
                break;

            case "iso3166" when result.IsValid:
                FuzzAssert.True(Gs1Linters.Lint("iso3166999", text).IsValid, "A country code is a country code or 999");
                break;

            case "hhmi" when text.Length == 4:
                FuzzAssert.Equal(Gs1Linters.Lint("hh", text.AsSpan(0, 2)).IsValid && Gs1Linters.Lint("mi", text.AsSpan(2)).IsValid, result.IsValid, "HHMI is HH then MI");
                break;
        }
    }

    private static void ElementTarget(ReadOnlySpan<byte> data)
    {
        var applicationIdentifier = FuzzInput.ReadText(FuzzInput.ReadUntil(ref data, 0));
        var value = FuzzInput.ReadText(data);

        Gs1Element? syntax = null;
        try
        {
            syntax = new Gs1Element(applicationIdentifier, value);
        }
        catch (ArgumentException)
        {
        }

        Gs1Element element;
        try
        {
            element = new Gs1Element(applicationIdentifier, value, Gs1ValidationMode.Dictionary);
        }
        catch (ArgumentException ex)
        {
            // An error of the data names its position inside the data
            if (syntax is not null && ex.ParamName == "value")
            {
                var match = PositionRegex().Match(ex.Message);
                FuzzAssert.True(match.Success, $"The message does not give the position: {ex.Message}");
                var position = int.Parse(match.Groups["position"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);
                FuzzAssert.True(position <= value.Length, $"The position {position} is outside the data of {value.Length} characters");
            }

            return;
        }

        FuzzAssert.True(syntax is not null, "The element follows the dictionary, but not the syntax rules");

        try
        {
            var barcode = Barcode.CreateGs1128(element);
            FuzzAssert.Equal(element.ToString(), barcode.Text, "The human-readable interpretation");
        }
        catch (ArgumentException)
        {
            // Too long for a GS1-128 symbol
        }

        // An element repeated with the same data does not change the associations
        bool valid;
        try
        {
            Gs1Element.ValidateAssociations(element);
            valid = true;
        }
        catch (ArgumentException)
        {
            valid = false;
        }

        try
        {
            Gs1Element.ValidateAssociations(element, syntax);
            FuzzAssert.True(valid, "Repeating the element makes the associations valid");
        }
        catch (ArgumentException)
        {
            FuzzAssert.False(valid, "Repeating the element makes the associations invalid");
        }
    }

    private static List<FuzzSeed> GetLinterSeeds()
    {
        using var document = FuzzResources.ReadJson(typeof(Gs1FuzzTargets).Assembly, "Seeds/gs1-linter-tests.json");
        var seeds = new List<FuzzSeed>();
        var index = 0;
        foreach (var testCase in document.RootElement.EnumerateArray())
        {
            var linter = Array.IndexOf(LinterNames, testCase.GetProperty("linter").GetString());
            if (linter < 0)
                throw new InvalidOperationException($"The linter '{testCase.GetProperty("linter").GetString()}' is not in the list.");

            seeds.Add(FuzzSeed.FromText($"gs1-linter-{index++}", [(byte)linter], testCase.GetProperty("input").GetString()!));
        }

        return seeds;
    }

    private static IEnumerable<FuzzSeed> GetElementSeeds()
    {
        var index = 0;
        foreach (var file in (string[])["Gs1DictionaryTests.cs", "BarCode128Tests.cs"])
        {
            var text = FuzzResources.ReadText(typeof(Gs1FuzzTargets).Assembly, "Seeds/" + file);
            foreach (Match match in ElementRegex().Matches(text))
            {
                yield return new FuzzSeed($"gs1-element-{index++}", (byte[])[.. FuzzText.Encode(match.Groups["ai"].Value), 0, .. FuzzText.Encode(Regex.Unescape(match.Groups["value"].Value))]);
            }
        }
    }

    [GeneratedRegex(@"at position (?<position>\d+):", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PositionRegex();

    [GeneratedRegex(@"(?:InlineData|Gs1Element)\(""(?<ai>\d{1,5})"", ""(?<value>(?:[^""\\]|\\.)*)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ElementRegex();
}
