using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.UnicodeFuzz;

public static partial class UnicodeFuzzTargets
{
    /// <summary>
    /// <para>
    /// The input is an option byte (each bit is a flag of <see cref="UnicodeIdnaOptions"/>, set when the bit is clear so
    /// that 0 selects the default options), then the domain name. The conversions never throw. With the options of the
    /// input and with the default options:
    /// </para>
    /// <list type="bullet">
    /// <item><description>A name that <c>TryToAscii</c> accepts becomes ASCII, and converting the result again gives the same result.</description></item>
    /// <item><description>
    /// ToASCII is ToUnicode followed by the Punycode encoding of the non-ASCII labels (UTS #46, section 4.2): the
    /// processing of the two is the same, ToUnicode also reports an empty label other than the root label (X4_2), and
    /// ToASCII also checks the lengths when VerifyDnsLength is set (A4_1, A4_2), which ToUnicode does not read. So
    /// without VerifyDnsLength, <c>TryToAscii</c> succeeds exactly when ToUnicode reports no error other than an empty
    /// label and every label can be encoded (when there is an empty label, only the result of a success is checked), and
    /// its result is the encoding of the labels of ToUnicode; with VerifyDnsLength, it succeeds exactly when ToUnicode
    /// reports no error and that encoding also has DNS lengths.
    /// </description></item>
    /// </list>
    /// <para>
    /// Punycode decodes what it encodes, and does not encode a text with unpaired surrogates. It also encodes what it
    /// decodes, for a text without uppercase letters: the decoding is unique, which lets ToASCII reject a long ACE label
    /// without decoding it.
    /// </para>
    /// </summary>
    private static void Idna(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var text = FuzzText.Decode(data);
        var options = new UnicodeIdnaOptions
        {
            CheckHyphens = (flags & 0x01) == 0,
            UseStd3AsciiRules = (flags & 0x02) == 0,
            VerifyDnsLength = (flags & 0x04) == 0,
            CheckBidi = (flags & 0x08) == 0,
            CheckJoiners = (flags & 0x10) == 0,
            TransitionalProcessing = (flags & 0x20) != 0,
            IgnoreInvalidPunycode = (flags & 0x40) != 0,
        };

        CheckIdna(text, options);
        if (flags != 0)
        {
            CheckIdna(text, UnicodeIdnaOptions.Default);
        }

        if (UnicodeIdna.TryEncodePunycode(text, out var encoded))
        {
            FuzzAssert.True(GetLoneSurrogates(text).Length == 0, $"TryEncodePunycode accepts unpaired surrogates: {FuzzAssert.Format(text)}");
            FuzzAssert.True(Ascii.IsValid(encoded), $"TryEncodePunycode returns a non-ASCII result: {FuzzAssert.Format(encoded)}");
            FuzzAssert.True(UnicodeIdna.TryDecodePunycode(encoded, out var decoded), $"TryDecodePunycode rejects {FuzzAssert.Format(encoded)}");
            FuzzAssert.Equal(text, decoded, "Punycode does not round-trip");
        }
        else
        {
            FuzzAssert.False(GetLoneSurrogates(text).Length == 0 && text.Length < 1000, $"TryEncodePunycode rejects {FuzzAssert.Format(text)}");
        }

        if (UnicodeIdna.TryDecodePunycode(text, out var decodedText) && !text.AsSpan().ContainsAnyInRange('A', 'Z'))
        {
            FuzzAssert.True(UnicodeIdna.TryEncodePunycode(decodedText, out var reencoded), $"TryEncodePunycode rejects the decoding of {FuzzAssert.Format(text)}");
            FuzzAssert.Equal(text, reencoded, "Punycode does not encode what it decodes");
        }
    }

    private static void CheckIdna(string text, UnicodeIdnaOptions options)
    {
        var description = $"CheckHyphens={options.CheckHyphens}, UseStd3AsciiRules={options.UseStd3AsciiRules}, VerifyDnsLength={options.VerifyDnsLength}, CheckBidi={options.CheckBidi}, CheckJoiners={options.CheckJoiners}, TransitionalProcessing={options.TransitionalProcessing}, IgnoreInvalidPunycode={options.IgnoreInvalidPunycode}";
        var succeeds = UnicodeIdna.TryToAscii(text, options, out var ascii);
        if (ascii is not null)
        {
            FuzzAssert.True(Ascii.IsValid(ascii), $"TryToAscii returns a non-ASCII result: {FuzzAssert.Format(ascii)} ({description})");
            FuzzAssert.True(UnicodeIdna.TryToAscii(ascii, options, out var again), $"TryToAscii rejects its own result: {FuzzAssert.Format(ascii)} ({description})");
            FuzzAssert.Equal(ascii, again, $"TryToAscii is not idempotent ({description})");
        }

        var unicode = UnicodeIdna.ToUnicode(text, options, out var hasErrors);
        var labels = unicode.Split('.');
        var hasEmptyLabel = labels.Where((label, index) => label.Length == 0 && (index < labels.Length - 1 || labels.Length == 1)).Any();
        if (succeeds)
        {
            FuzzAssert.Equal(hasEmptyLabel, hasErrors, $"TryToAscii accepts {FuzzAssert.Format(text)} but ToUnicode reports an error other than an empty label ({description})");
        }

        if (hasErrors)
        {
            if (!hasEmptyLabel || options.VerifyDnsLength)
            {
                FuzzAssert.False(succeeds, $"ToUnicode reports an error for {FuzzAssert.Format(text)} but TryToAscii accepts it ({description})");
                return;
            }

            // The empty label hides whether ToUnicode would report another error: only the result of a success is checked
            if (!succeeds)
                return;
        }

        string? expected = null;
        if (labels.All(label => Ascii.IsValid(label) || UnicodeIdna.TryEncodePunycode(label, out _)))
        {
            expected = string.Join('.', labels.Select(label => Ascii.IsValid(label) ? label : "xn--" + (UnicodeIdna.TryEncodePunycode(label, out var encoded) ? encoded : "")));
            if (options.VerifyDnsLength && !HasDnsLengths(expected))
            {
                expected = null;
            }
        }

        FuzzAssert.Equal(expected, succeeds ? ascii : null, $"TryToAscii of {FuzzAssert.Format(text)} is not the encoding of the labels of ToUnicode {FuzzAssert.Format(unicode)} ({description})");
    }

    /// <summary>A4_1 and A4_2: at most 253 characters, and labels of 1 to 63 characters, the root label included.</summary>
    private static bool HasDnsLengths(string domainName) => domainName.Length <= 253 && domainName.Split('.').All(label => label.Length is >= 1 and <= 63);

    private static IEnumerable<FuzzSeed> GetIdnaSeeds()
    {
        foreach (var (index, line) in Sample(ReadConformanceLines("IdnaTestV2.txt.gz"), 300))
        {
            // The source column, with its escapes (\uXXXX) as they are
            yield return IdnaSeed($"IdnaTestV2-{index}", (byte)(index % 128), line.Split(';')[0].Trim());
        }

        yield return IdnaSeed("ascii", 0, "www.example.com");
        yield return IdnaSeed("unicode", 0, "B\u00FCcher.example");
        yield return IdnaSeed("punycode", 0, "xn--bcher-kva.example");
        yield return IdnaSeed("bidi", 0, "\u05D0\u05D1.\u0627\u0661");
        yield return IdnaSeed("bidi-unchecked", 0x08, "\u05D0\u05D1.\u0627\u0661.1a\u05D0");
        yield return IdnaSeed("joiners", 0, "\u0915\u094D\u200D\u0937.\u0628\u200C\u0628");
        yield return IdnaSeed("joiners-unchecked", 0x10, "a\u200Db.a\u200Cb");
        yield return IdnaSeed("long", 0, string.Concat(Enumerable.Repeat("\u00E9a.", 100)));
        yield return IdnaSeed("long-lenient", 0x07, string.Concat(Enumerable.Repeat("\u00E9a.", 100)));
        yield return IdnaSeed("transitional", 0x20, "fa\u00DF.de.\u03C2.\u1E9E.a\u200Db");
        yield return IdnaSeed("invalid-punycode", 0x40, "xn--a.xn--zz-.xn--.xn--bcher-kva");
        yield return IdnaSeed("empty-labels", 0x07, "a..b.");
        yield return IdnaSeed("hyphens", 0x01, "-a-.ab--c.xn--abc");
        yield return IdnaSeed("std3", 0x02, "a_b.A B.$");
        yield return IdnaSeed("all-flags", 0x7F, "Fa\u00DF.xn--zz.\u05D0-.a\u200C");
    }

    private static FuzzSeed IdnaSeed(string name, byte flags, string domainName) => new(name, (byte[])[flags, .. FuzzText.Encode(domainName)]);
}
