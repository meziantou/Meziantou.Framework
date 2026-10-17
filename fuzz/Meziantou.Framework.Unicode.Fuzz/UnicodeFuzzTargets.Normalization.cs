using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.UnicodeFuzz;

public static partial class UnicodeFuzzTargets
{
    private static readonly NormalizationForm[] NormalizationForms = [NormalizationForm.FormC, NormalizationForm.FormD, NormalizationForm.FormKC, NormalizationForm.FormKD];

    /// <summary>
    /// Each form is idempotent, <c>IsNormalized</c>, <c>GetNormalizedLength</c> and <c>TryNormalize</c> agree with
    /// <c>Normalize</c>, which returns its argument when it is normalized, and the forms compose as UAX #15 specifies
    /// (NFC(NFD(x)) = NFC(x), ...). NFD and NFC are also computed directly from the canonical decompositions, the
    /// combining classes and the primary composites that the library exposes, which checks the quick checks and the
    /// buffers of the main algorithm. The unpaired surrogates are kept, in order.
    /// </summary>
    private static void Normalization(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var results = new Dictionary<NormalizationForm, string>();
        foreach (var form in NormalizationForms)
        {
            var normalized = UnicodeNormalization.Normalize(text, form);
            results[form] = normalized;
            var isNormalized = UnicodeNormalization.IsNormalized(text, form);
            FuzzAssert.Equal(isNormalized, string.Equals(normalized, text, StringComparison.Ordinal), $"IsNormalized({form}) does not tell whether Normalize changes the text");
            FuzzAssert.Equal(isNormalized, ReferenceEquals(normalized, text), $"Normalize({form}) returns a new string for a normalized text, or the same instance for a text it changes");
            FuzzAssert.True(UnicodeNormalization.IsNormalized(normalized, form), $"The result of Normalize({form}) is not normalized: {FuzzAssert.Format(normalized)}");
            FuzzAssert.True(ReferenceEquals(normalized, UnicodeNormalization.Normalize(normalized, form)), $"Normalize({form}) is not idempotent: {FuzzAssert.Format(normalized)}");
            FuzzAssert.Equal(normalized.Length, UnicodeNormalization.GetNormalizedLength(text, form), $"GetNormalizedLength({form}) does not match Normalize");
            FuzzAssert.Equal(GetLoneSurrogates(text), GetLoneSurrogates(normalized), $"Normalize({form}) does not keep the unpaired surrogates");

            var buffer = new char[normalized.Length + 1];
            FuzzAssert.True(UnicodeNormalization.TryNormalize(text, buffer.AsSpan(0, normalized.Length), out var written, form), $"TryNormalize({form}) fails with a buffer of the normalized length");
            FuzzAssert.Equal(normalized, new string(buffer, 0, written), $"TryNormalize({form}) does not write the result of Normalize");
            if (normalized.Length > 0)
            {
                FuzzAssert.False(UnicodeNormalization.TryNormalize(text, buffer.AsSpan(0, normalized.Length - 1), out written, form), $"TryNormalize({form}) succeeds with a buffer shorter than the normalized length");
                FuzzAssert.Equal(0, written, $"TryNormalize({form}) reports characters written when it fails");
            }
        }

        var nfc = results[NormalizationForm.FormC];
        var nfd = results[NormalizationForm.FormD];
        var nfkc = results[NormalizationForm.FormKC];
        var nfkd = results[NormalizationForm.FormKD];
        FuzzAssert.Equal(nfc, UnicodeNormalization.Normalize(nfd, NormalizationForm.FormC), "NFC(NFD(x)) != NFC(x)");
        FuzzAssert.Equal(nfd, UnicodeNormalization.Normalize(nfc, NormalizationForm.FormD), "NFD(NFC(x)) != NFD(x)");
        FuzzAssert.Equal(nfkc, UnicodeNormalization.Normalize(nfkd, NormalizationForm.FormKC), "NFKC(NFKD(x)) != NFKC(x)");
        FuzzAssert.Equal(nfkd, UnicodeNormalization.Normalize(nfkc, NormalizationForm.FormKD), "NFKD(NFKC(x)) != NFKD(x)");
        FuzzAssert.Equal(nfkc, UnicodeNormalization.Normalize(nfc, NormalizationForm.FormKC), "NFKC(NFC(x)) != NFKC(x)");
        FuzzAssert.Equal(nfkd, UnicodeNormalization.Normalize(nfd, NormalizationForm.FormKD), "NFKD(NFD(x)) != NFKD(x)");
        FuzzAssert.True(UnicodeNormalization.IsNormalized(nfkc, NormalizationForm.FormC), "NFKC(x) is not in NFC");
        FuzzAssert.True(UnicodeNormalization.IsNormalized(nfkd, NormalizationForm.FormD), "NFKD(x) is not in NFD");

        var decomposed = ReferenceDecompose(text);
        FuzzAssert.Equal(Encode(decomposed), nfd, "NFD differs from the canonical decomposition of each code point followed by the canonical ordering");
        FuzzAssert.Equal(Encode(ReferenceCompose(decomposed)), nfc, "NFC differs from the canonical composition of NFD");
    }

    private static string GetLoneSurrogates(string text)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < text.Length;)
        {
            var codePoint = CodePointAt(text, index, out var length);
            if (length == 1 && char.IsSurrogate((char)codePoint))
            {
                builder.Append((char)codePoint);
            }

            index += length;
        }

        return builder.ToString();
    }

    private static bool IsLoneSurrogate(int codePoint) => codePoint is >= 0xD800 and <= 0xDFFF;

    // An unpaired surrogate is not a character: it is a starter that never combines (see the remarks of UnicodeNormalization).
    private static int GetCombiningClass(int codePoint) => IsLoneSurrogate(codePoint) ? 0 : UnicodeProperties.GetCanonicalCombiningClass(codePoint);

    /// <summary>The full canonical decomposition of each code point, then the canonical ordering (a stable sort of each run of non-starters by combining class).</summary>
    private static List<int> ReferenceDecompose(string text)
    {
        var result = new List<int>(text.Length);
        Span<int> decomposition = stackalloc int[UnicodeNormalization.MaxCanonicalDecompositionLength];
        for (var index = 0; index < text.Length;)
        {
            var codePoint = CodePointAt(text, index, out var length);
            index += length;
            if (IsLoneSurrogate(codePoint))
            {
                result.Add(codePoint);
                continue;
            }

            var count = UnicodeNormalization.GetCanonicalDecomposition(codePoint, decomposition);
            for (var i = 0; i < count; i++)
            {
                result.Add(decomposition[i]);
            }
        }

        for (var start = 0; start < result.Count;)
        {
            if (GetCombiningClass(result[start]) == 0)
            {
                start++;
                continue;
            }

            var end = start;
            while (end < result.Count && GetCombiningClass(result[end]) != 0)
            {
                end++;
            }

            // OrderBy is a stable sort
            var sorted = result.GetRange(start, end - start).OrderBy(GetCombiningClass).ToArray();
            for (var i = 0; i < sorted.Length; i++)
            {
                result[start + i] = sorted[i];
            }

            start = end;
        }

        return result;
    }

    /// <summary>The canonical composition algorithm of UAX #15 (the reference implementation of the annex), applied to a decomposed text.</summary>
    private static List<int> ReferenceCompose(List<int> decomposed)
    {
        var result = new List<int>(decomposed.Count);
        if (decomposed.Count == 0)
            return result;

        var starterIndex = 0;
        var starter = decomposed[0];
        var lastClass = GetCombiningClass(starter);
        if (lastClass != 0)
        {
            // A text that starts with a non-starter: nothing combines with it
            lastClass = 256;
        }

        result.Add(starter);
        for (var i = 1; i < decomposed.Count; i++)
        {
            var codePoint = decomposed[i];
            var combiningClass = GetCombiningClass(codePoint);
            if ((lastClass < combiningClass || lastClass == 0) && !IsLoneSurrogate(starter) && !IsLoneSurrogate(codePoint) && UnicodeNormalization.TryComposeCanonical(starter, codePoint, out var composite))
            {
                result[starterIndex] = composite;
                starter = composite;
                continue;
            }

            if (combiningClass == 0)
            {
                starterIndex = result.Count;
                starter = codePoint;
            }

            lastClass = combiningClass;
            result.Add(codePoint);
        }

        return result;
    }

    private static string Encode(List<int> codePoints)
    {
        var builder = new StringBuilder(codePoints.Count);
        foreach (var codePoint in codePoints)
        {
            AppendCodePoint(builder, codePoint);
        }

        return builder.ToString();
    }

    private static IEnumerable<FuzzSeed> GetNormalizationSeeds()
    {
        foreach (var (index, line) in Sample(ReadConformanceLines("NormalizationTest.txt.gz"), 300))
        {
            // The source and its four normal forms: the source alone, it is the one that is not normalized
            yield return FuzzSeed.FromText($"NormalizationTest-{index}", ParseCodePoints(line.Split(';')[0]));
        }

        foreach (var (name, text) in HandWrittenTexts)
        {
            yield return FuzzSeed.FromText(name, text);
        }

        yield return FuzzSeed.FromText("long-mark-run", "a" + string.Concat(Enumerable.Repeat("\u0301\u0323\u0345\u05B0\u0315", 12)) + "\u1100\u1161\u11A8");
    }
}
