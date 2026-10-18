using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.UnicodeFuzz;

public static partial class UnicodeFuzzTargets
{
    private static readonly UnicodeIdnaOptions LenientIdnaOptions = new() { CheckHyphens = false, UseStd3AsciiRules = false, VerifyDnsLength = false };

    /// <summary>
    /// The conversions never throw. A name that <c>TryToAscii</c> accepts becomes ASCII, and converting the result again
    /// gives the same result (nontransitional processing). Punycode decodes what it encodes, for a text without unpaired
    /// surrogates.
    /// </summary>
    private static void Idna(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        foreach (var options in new[] { UnicodeIdnaOptions.Default, LenientIdnaOptions })
        {
            if (UnicodeIdna.TryToAscii(text, options, out var ascii))
            {
                FuzzAssert.True(Ascii.IsValid(ascii), $"TryToAscii returns a non-ASCII result: {FuzzAssert.Format(ascii)}");
                FuzzAssert.True(UnicodeIdna.TryToAscii(ascii, options, out var again), $"TryToAscii rejects its own result: {FuzzAssert.Format(ascii)}");
                FuzzAssert.Equal(ascii, again, "TryToAscii is not idempotent");
            }

            UnicodeIdna.ToUnicode(text, options, out _);
        }

        if (GetLoneSurrogates(text).Length == 0 && UnicodeIdna.TryEncodePunycode(text, out var encoded))
        {
            FuzzAssert.True(Ascii.IsValid(encoded), $"TryEncodePunycode returns a non-ASCII result: {FuzzAssert.Format(encoded)}");
            FuzzAssert.True(UnicodeIdna.TryDecodePunycode(encoded, out var decoded), $"TryDecodePunycode rejects {FuzzAssert.Format(encoded)}");
            FuzzAssert.Equal(text, decoded, "Punycode does not round-trip");
        }

        UnicodeIdna.TryDecodePunycode(text, out _);
    }

    private static IEnumerable<FuzzSeed> GetIdnaSeeds()
    {
        foreach (var (index, line) in Sample(ReadConformanceLines("IdnaTestV2.txt.gz"), 300))
        {
            // The source column, with its escapes (\uXXXX) as they are
            yield return FuzzSeed.FromText($"IdnaTestV2-{index}", line.Split(';')[0].Trim());
        }

        yield return FuzzSeed.FromText("ascii", "www.example.com");
        yield return FuzzSeed.FromText("unicode", "B\u00FCcher.example");
        yield return FuzzSeed.FromText("punycode", "xn--bcher-kva.example");
        yield return FuzzSeed.FromText("bidi", "\u05D0\u05D1.\u0627\u0661");
        yield return FuzzSeed.FromText("joiners", "\u0915\u094D\u200D\u0937.\u0628\u200C\u0628");
        yield return FuzzSeed.FromText("long", string.Concat(Enumerable.Repeat("\u00E9a.", 100)));
    }
}
