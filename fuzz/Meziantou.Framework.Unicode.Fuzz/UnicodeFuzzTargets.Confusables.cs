using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.UnicodeFuzz;

public static partial class UnicodeFuzzTargets
{
    /// <summary>
    /// The input is two strings. The skeleton of a well-formed string is in NFD and, when it has no right-to-left text, is its own skeleton; the skeleton of
    /// an ill-formed string throws <see cref="ArgumentException"/>. <c>AreConfusable</c> is reflexive (even for
    /// ill-formed strings), symmetric, compares the skeletons, and throws <see cref="ArgumentException"/> only when
    /// the strings differ and one of them is ill-formed.
    /// </summary>
    private static void Confusables(ReadOnlySpan<byte> data)
    {
        var a = FuzzText.Decode(FuzzInput.ReadUntil(ref data, PartSeparator));
        var b = FuzzText.Decode(data);

        _ = Unicode.ReplaceConfusablesCharacters(a);
        FuzzAssert.Equal(!Unicode.IsSingleScript(a), Unicode.IsMixedScript(a), "IsMixedScript is not the opposite of IsSingleScript");
        FuzzAssert.True(Unicode.AreConfusable(a, a), "AreConfusable is not reflexive");

        var skeletonA = GetSkeleton(a);
        var skeletonB = GetSkeleton(b);
        bool areConfusable;
        try
        {
            areConfusable = Unicode.AreConfusable(a, b);
        }
        catch (ArgumentException)
        {
            FuzzAssert.True(skeletonA is null || skeletonB is null, "AreConfusable throws for two well-formed strings");
            FuzzAssert.False(string.Equals(a, b, StringComparison.Ordinal), "AreConfusable throws for two equal strings");
            return;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            FuzzAssert.True(areConfusable, "AreConfusable is not reflexive");
            return;
        }

        FuzzAssert.True(skeletonA is not null && skeletonB is not null, "AreConfusable does not throw for different strings when one is ill-formed");
        FuzzAssert.Equal(string.Equals(skeletonA, skeletonB, StringComparison.Ordinal), areConfusable, $"AreConfusable does not compare the skeletons {FuzzAssert.Format(skeletonA)} and {FuzzAssert.Format(skeletonB)}");
        FuzzAssert.Equal(areConfusable, Unicode.AreConfusable(b, a), "AreConfusable is not symmetric");
    }

    /// <summary>Gets the skeleton of a string and checks its properties, or returns <see langword="null"/> when the string is ill-formed (and the method throws, as documented).</summary>
    private static string? GetSkeleton(string value)
    {
        string skeleton;
        try
        {
            skeleton = Unicode.GetConfusableSkeleton(value);
        }
        catch (ArgumentException)
        {
            FuzzAssert.False(IsWellFormed(value), "GetConfusableSkeleton throws for a well-formed string");
            return null;
        }

        FuzzAssert.True(IsWellFormed(value), "GetConfusableSkeleton does not throw for a string with an unpaired surrogate");
        FuzzAssert.True(IsWellFormed(skeleton), $"The skeleton {FuzzAssert.Format(skeleton)} is not well-formed");
        FuzzAssert.True(UnicodeNormalization.IsNormalized(skeleton, NormalizationForm.FormD), $"The skeleton {FuzzAssert.Format(skeleton)} is not in NFD");

        // The bidi skeleton puts the text in display order, so a skeleton with right-to-left text is reordered again.
        if (!UnicodeBidi.MayRequireBidi(skeleton))
        {
            FuzzAssert.Equal(skeleton, Unicode.GetConfusableSkeleton(skeleton), "The skeleton of a skeleton is not itself");
        }

        return skeleton;
    }

    private static IEnumerable<FuzzSeed> GetConfusableSeeds()
    {
        (string Name, string A, string B)[] pairs =
        [
            ("paypal", "paypal", "p\u0430yp\u0430l"),
            ("rn-m", "rn", "m"),
            ("vv-w", "vv", "w"),
            ("digits", "l0O1", "I01l"),
            ("composed", "\u00E9", "e\u0301"),
            ("cyrillic", "\u0441\u043E\u043C", "com"),
            ("greek", "\u0391\u0392\u0395", "ABE"),
            ("fullwidth", "\uFF41\uFF42\uFF43", "abc"),
            ("math", "\U0001D41A\U0001D41B", "ab"),
            ("ligature", "\uFB01", "fi"),
            ("roman", "\u217C\u217D", "lc"),
            ("japanese", "\u30A8\u30CB", "\u5DE5\u4E8C"),
            ("empty", "", ""),
            ("lone-surrogate", "a\uD800", "a\uD800"),
            ("lone-surrogate-different", "a\uD800", "a"),
        ];

        foreach (var (name, a, b) in pairs)
        {
            yield return new FuzzSeed(name, (byte[])[.. FuzzText.Encode(a), PartSeparator, .. FuzzText.Encode(b)]);
        }

        foreach (var (name, text) in HandWrittenTexts)
        {
            var encoded = FuzzText.Encode(text);
            yield return new FuzzSeed(name, (byte[])[.. encoded, PartSeparator, .. Encoding.UTF8.GetBytes(Unicode.ReplaceConfusablesCharacters(IsWellFormed(text) ? text : "x"))]);
        }
    }
}
