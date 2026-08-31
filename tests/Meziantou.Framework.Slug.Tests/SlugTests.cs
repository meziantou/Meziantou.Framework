using System.Text.Unicode;
using Meziantou.Xunit;

namespace Meziantou.Framework.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("a", "a")]
    [InlineData("z", "z")]
    [InlineData("A", "A")]
    [InlineData("Z", "Z")]
    [InlineData("0", "0")]
    [InlineData("9", "9")]
    [InlineData("test", "test")]
    [InlineData("TeSt", "TeSt")]
    [InlineData("teste\u0301", "teste")]
    [InlineData("TeSt test", "TeSt-test")]
    [InlineData("TeSt test ", "TeSt-test")]
    [InlineData("TeSt:test ", "TeSt-test")]
    [InlineData(" test", "test")]
    [InlineData(":test", "test")]
    [InlineData("  ::  a b", "a-b")]
    [InlineData("a\u093Eb", "ab")]
    [InlineData("a\u20DDb", "ab")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("!!!", "")]
    public void Slug_WithDefaultOptions(string text, string expected)
    {
        var slug = Slug.Create(text);
        Assert.Equal(expected, slug);
    }

    [Theory]
    [InlineData("test", "test")]
    [InlineData("TeSt", "test")]
    public void Slug_Lowercase(string text, string expected)
    {
        var options = new SlugOptions
        {
            CasingTransformation = CasingTransformation.ToLowerCase,
        };
        var slug = Slug.Create(text, options);
        Assert.Equal(expected, slug);
    }

    [Theory]
    [InlineData(1, "a")]
    [InlineData(2, "a")]
    [InlineData(3, "a\U0001F600")]
    [InlineData(4, "a\U0001F600b")]
    public void Slug_MaximumLength_DoesNotSplitSurrogatePairs(int maximumLength, string expected)
    {
        var options = new SlugOptions { MaximumLength = maximumLength };
        options.AllowedRanges.Clear();

        var slug = Slug.Create("a\U0001F600b", options);

        Assert.Equal(expected, slug);
    }

    [Theory]
    [InlineData(2, "ab")]
    [InlineData(3, "ab")]
    [InlineData(4, "ab")]
    [InlineData(5, "ab__c")]
    [InlineData(6, "ab__cd")]
    public void Slug_MaximumLength_DoesNotSplitMultiCharacterSeparator(int maximumLength, string expected)
    {
        var options = new SlugOptions { Separator = "__", MaximumLength = maximumLength };
        var slug = Slug.Create("ab cdef", options);
        Assert.Equal(expected, slug);
    }

    [Fact]
    public void Slug_MaximumLength_IsNeverExceeded()
    {
        var options = new SlugOptions { MaximumLength = 5 };
        var slug = Slug.Create("hello world this is long", options);
        Assert.Equal("hello", slug);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Slug_MaximumLength_ZeroOrNegativeMeansUnlimited(int maximumLength)
    {
        var options = new SlugOptions { MaximumLength = maximumLength };
        var slug = Slug.Create("hello world this is long", options);
        Assert.Equal("hello-world-this-is-long", slug);
    }

    [Theory]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    [InlineData(1, "\uAC00")]
    [InlineData(2, "\uAC00\uAC01")]
    [InlineData(3, "\uAC00\uAC01\uAC02")]
    [InlineData(4, "\uAC00\uAC01\uAC02")]
    [InlineData(5, "\uAC00\uAC01\uAC02")]
    public void Slug_MaximumLength_DoesNotSplitHangulSyllables(int maximumLength, string expected)
    {
        var options = new SlugOptions { MaximumLength = maximumLength };
        options.AllowedRanges.Clear();

        var slug = Slug.Create("\uAC00\uAC01\uAC02", options);

        Assert.Equal(expected, slug);
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Slug_MaximumLength_NeverLeavesALoneHangulJamo()
    {
        string[] texts = ["\uD55C\uAD6D\uC5B4 \uC81C\uBAA9", "\uAC00\uAC01\uAC02", "a\uAC00b\uAC01", "\uD55C a \uAD6D"];
        foreach (var text in texts)
        {
            foreach (var separator in new[] { "-", "__", "" })
            {
                for (var maximumLength = 1; maximumLength <= 20; maximumLength++)
                {
                    var options = new SlugOptions { MaximumLength = maximumLength, Separator = separator };
                    options.AllowedRanges.Clear();

                    var slug = Slug.Create(text, options);

                    Assert.DoesNotContain(slug, c => c is >= '\u1100' and <= '\u11FF');
                }
            }
        }
    }

    [Theory]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    [InlineData("!\u0301b", "b")]
    [InlineData("\u0301abc", "abc")]
    [InlineData("!\u0301", "")]
    [InlineData("\u20AC\u0301x", "x")]
    [InlineData("a\u0301b", "\u00E1b")]
    public void Slug_AllowedCombiningMark_WithNoBaseCharacter_IsDropped(string text, string expected)
    {
        var options = new SlugOptions();
        options.AllowedRanges.Add(UnicodeRange.Create('\u0300', '\u036F'));

        var slug = Slug.Create(text, options);

        Assert.Equal(expected, slug);
    }

    [Fact]
    public void Slug_Separator_CannotBeSetToNull()
    {
        var options = new SlugOptions();
        Assert.Throws<ArgumentNullException>(() => options.Separator = null!);
    }

    [Fact]
    public void Slug_EmptySeparator_JoinsWords()
    {
        var options = new SlugOptions { Separator = "" };
        var slug = Slug.Create("a b", options);
        Assert.Equal("ab", slug);
    }

    [Theory]
    [InlineData("Ajax", "x", "Ajax")]
    [InlineData("version 10", "0", "version010")]
    [InlineData("ax b", "x", "axxb")]
    public void Slug_SeparatorCharacterFromTheInput_IsContentAndIsNotTrimmed(string text, string separator, string expected)
    {
        var options = new SlugOptions { Separator = separator };
        var slug = Slug.Create(text, options);
        Assert.Equal(expected, slug);
    }

    [Theory]
    [InlineData("hello-world-", "hello-world-")]
    [InlineData("end-", "end-")]
    [InlineData("a-", "a-")]
    [InlineData("wrap-up -", "wrap-up--")]
    public void Slug_AllowedSeparatorCharacter_IsKeptEvenWhenItEndsTheSlug(string text, string expected)
    {
        var options = new SlugOptions();
        options.AllowedRanges.Add(UnicodeRange.Create('-', '-'));
        var slug = Slug.Create(text, options);
        Assert.Equal(expected, slug);
    }

    [Theory]
    [InlineData("\u1100__")]
    [InlineData("_b\u00C1\u11A8--__a!")]
    public void Slug_MaximumLength_RaisingTheLimitNeverShortensTheSlug_WhenTheInputContainsTheSeparator(string text)
    {
        var previousLength = 0;
        for (var maximumLength = 1; maximumLength <= 16; maximumLength++)
        {
            var options = new SlugOptions { Separator = "__", MaximumLength = maximumLength };
            options.AllowedRanges.Clear();

            var slug = Slug.Create(text, options);

            Assert.HasCountGreaterThanOrEqual(previousLength, slug, $"limit {maximumLength} produced fewer characters than limit {maximumLength - 1}");
            previousLength = slug.Length;
        }
    }

    [Fact]
    public void Slug_CanEndWithSeparator_KeepsTrailingSeparator()
    {
        var options = new SlugOptions { CanEndWithSeparator = true };
        var slug = Slug.Create("a b ", options);
        Assert.Equal("a-b-", slug);
    }

    [Fact]
    public void Slug_Uppercase()
    {
        var options = new SlugOptions { CasingTransformation = CasingTransformation.ToUpperCase };
        var slug = Slug.Create("Hello World", options);
        Assert.Equal("HELLO-WORLD", slug);
    }

    [Fact]
    public void Slug_EmptyAllowedRanges_AllowsEveryCharacter()
    {
        var options = new SlugOptions();
        options.AllowedRanges.Clear();
        var slug = Slug.Create("a b", options);
        Assert.Equal("a b", slug);
    }

    [Fact]
    public void Slug_NullText_ReturnsNull()
    {
        Assert.Null(Slug.Create(text: null));
    }

    [Theory]
    [InlineData("a\uD800b", "a-b")]
    [InlineData("a\uDC00b", "a-b")]
    [InlineData("\uD800", "")]
    [InlineData("\uDFFF\uD800", "")]
    [InlineData("My great post \uD83C", "My-great-post")]
    [InlineData("a\uFFFEb", "a-b")]
    [InlineData("a\uFFFFb", "a-b")]
    [InlineData("a\uFDD0b", "a-b")]
    public void Slug_IllFormedText_IsAcceptedAsReplacementCharacters(string text, string expected)
    {
        var slug = Slug.Create(text);
        Assert.Equal(expected, slug);
    }

    // string.Normalize only rejects ill-formed text when ICU is available, so the substitution the separator
    // relies on does not happen in invariant globalization mode.
    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Slug_IllFormedSeparator_DoesNotThrow()
    {
        var options = new SlugOptions { Separator = "\uD800" };
        var slug = Slug.Create("a!b", options);
        Assert.Equal("a\uFFFDb", slug);
    }

    [Fact]
    public void Slug_OverriddenReplace_IsUsed()
    {
        var options = new UpperCaseVowelSlugOptions();
        var slug = Slug.Create("hello world", options);
        Assert.Equal("hEllO-wOrld", slug);
    }

    [Fact]
    public void Slug_OverriddenReplace_IsUsedWhenItReturnsMultipleCharacters()
    {
        var options = new ExpandingSlugOptions();
        var slug = Slug.Create("ab c", options);
        Assert.Equal("aabb-cc", slug);
    }

    [Theory]
    [InlineData(1, "")]
    [InlineData(2, "aa")]
    [InlineData(3, "aa")]
    [InlineData(4, "aabb")]
    [InlineData(5, "aabb")]
    [InlineData(6, "aabb")]
    [InlineData(7, "aabb-cc")]
    [InlineData(8, "aabb-cc")]
    public void Slug_OverriddenReplace_IsNeverSplitByMaximumLength(int maximumLength, string expected)
    {
        var options = new ExpandingSlugOptions { MaximumLength = maximumLength };
        var slug = Slug.Create("ab c", options);
        Assert.Equal(expected, slug);
    }

    [Fact]
    public void Slug_OverriddenIsAllowed_IsUsed()
    {
        var options = new NoVowelSlugOptions();
        var slug = Slug.Create("hello world", options);
        Assert.Equal("h-ll-w-rld", slug);
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Slug_Culture_IsUsedForCasing()
    {
        var options = new SlugOptions
        {
            CasingTransformation = CasingTransformation.ToLowerCase,
            Culture = CultureInfo.GetCultureInfo("tr-TR"),
        };
        var slug = Slug.Create("II", options);
        Assert.Equal("\u0131\u0131", slug);
    }

    [Fact]
    public void Slug_Replace_DefaultImplementationAppliesCasing()
    {
        var options = new SlugOptions { CasingTransformation = CasingTransformation.ToUpperCase };
        Assert.Equal("A", options.Replace(new Rune('a')));
    }

    private sealed class UpperCaseVowelSlugOptions : SlugOptions
    {
        public override string Replace(Rune rune)
        {
            return "aeiou".Contains(rune.ToString(), StringComparison.Ordinal) ? rune.ToString().ToUpperInvariant() : base.Replace(rune);
        }
    }

    private sealed class ExpandingSlugOptions : SlugOptions
    {
        public override string Replace(Rune rune)
        {
            return new string((char)rune.Value, count: 2);
        }
    }

    [Theory]
    [InlineData(CasingTransformation.PreserveCase)]
    [InlineData(CasingTransformation.ToLowerCase)]
    [InlineData(CasingTransformation.ToUpperCase)]
    public void Slug_SubclassNotOverridingReplace_MatchesTheBaseClass(CasingTransformation casingTransformation)
    {
        string[] texts = ["Hello World", "Caf\u00E9 cr\u00E8me br\u00FBl\u00E9e", "a\U0001F600b", "TeSt:test ", "!!!"];
        foreach (var text in texts)
        {
            foreach (var maximumLength in new[] { 0, 1, 3, 8, 80 })
            {
                var expected = Slug.Create(text, new SlugOptions { CasingTransformation = casingTransformation, MaximumLength = maximumLength });
                var actual = Slug.Create(text, new PassThroughSlugOptions { CasingTransformation = casingTransformation, MaximumLength = maximumLength });
                Assert.Equal(expected, actual);
            }
        }
    }

    private sealed class PassThroughSlugOptions : SlugOptions
    {
        public override bool IsAllowed(Rune character)
        {
            return base.IsAllowed(character);
        }
    }

    private sealed class NoVowelSlugOptions : SlugOptions
    {
        public override bool IsAllowed(Rune character)
        {
            return base.IsAllowed(character) && !"aeiou".Contains(character.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    [InlineData(1, "\u00E9")]
    [InlineData(2, "\u00E9\u00E9")]
    [InlineData(3, "\u00E9\u00E9\u00E9")]
    [InlineData(5, "\u00E9\u00E9\u00E9\u00E9")]
    [InlineData(9, "\u00E9\u00E9\u00E9\u00E9")]
    public void Slug_MaximumLength_AppliesToTheComposedSlug(int maximumLength, string expected)
    {
        var options = new SlugOptions { MaximumLength = maximumLength };
        options.AllowedRanges.Clear();

        var slug = Slug.Create("\u00E9\u00E9\u00E9\u00E9", options);

        Assert.Equal(expected, slug);
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Slug_MaximumLength_FillsTheLimitWhenComposingFreesRoom()
    {
        var options = new SlugOptions { MaximumLength = 17 };
        options.AllowedRanges.Clear();

        var slug = Slug.Create("caf\u00E9 cr\u00E8me br\u00FBl\u00E9e", options);

        Assert.Equal("caf\u00E9 cr\u00E8me br\u00FBl\u00E9e", slug);
        Assert.HasCount(17, slug);
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Slug_MaximumLength_NeverStripsCombiningMarksFromTheLastCharacter()
    {
        var options = new SlugOptions { MaximumLength = 4 };
        options.AllowedRanges.Clear();

        var slug = Slug.Create("\u00E9\u00E9\u00E9\u00E9\u00E9", options);

        Assert.DoesNotContain('e', slug);
        Assert.Equal("\u00E9\u00E9\u00E9\u00E9", slug);
    }

    [Theory]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    [InlineData("\u00E9\u00E9\u00E9\u00E9", "\u00E9\u00E9\u00E9\u00E9")]
    [InlineData("caf\u00E9 cr\u00E8me br\u00FBl\u00E9e", "caf\u00E9 cr\u00E8me br\u00FBl\u00E9e")]
    [InlineData("\uAC00\uAC01\uAC02", "\uAC00\uAC01\uAC02")]
    [InlineData("a\u0301\u0302\u0303\u0304b", "\u00E1\u0302\u0303\u0304b")]
    [InlineData("  \u00E9 \u00E9  ", "  \u00E9 \u00E9  ")]
    [InlineData("\U0001F600\u00E9\U0001F600", "\U0001F600\u00E9\U0001F600")]
    [InlineData("Ti\u1EBFng Vi\u1EC7t", "Ti\u1EBFng Vi\u1EC7t")]
    [InlineData("a-b--c", "a-b--c")]
    public void Slug_MaximumLength_Unlimited_KeepsEveryAllowedCharacter(string text, string expected)
    {
        var options = new SlugOptions { MaximumLength = 0 };
        options.AllowedRanges.Clear();

        var slug = Slug.Create(text, options);

        Assert.Equal(expected, slug);
    }

    [Fact]
    public void Slug_MaximumLength_Properties_WithoutNormalization()
    {
        // Normalization is a no-op under InvariantGlobalization, so these inputs keep the limit logic covered in
        // both modes. The cases that depend on composing are in Slug_MaximumLength_Properties.
        string[] texts = ["hello world this is long", "a-b--c", "a  b", "!!!", "A", "ab cdef"];

        foreach (var text in texts)
        {
            foreach (var separator in new[] { "-", "__", "" })
            {
                foreach (var canEndWithSeparator in new[] { true, false })
                {
                    SlugOptions CreateOptions(int maximumLength)
                        => new() { MaximumLength = maximumLength, Separator = separator, CanEndWithSeparator = canEndWithSeparator };

                    var context = $"[{text}] separator '{separator}' canEndWithSeparator={canEndWithSeparator}";
                    var unlimited = Slug.Create(text, CreateOptions(0));
                    var previous = "";

                    for (var maximumLength = 1; maximumLength <= 24; maximumLength++)
                    {
                        var slug = Slug.Create(text, CreateOptions(maximumLength));

                        Assert.HasCountLessThanOrEqual(maximumLength, slug, $"{context} with limit {maximumLength} exceeded the limit");
                        Assert.StartsWith(previous, slug, message: $"{context}: limit {maximumLength} does not extend the slug from limit {maximumLength - 1}");

                        if (maximumLength >= unlimited.Length)
                        {
                            Assert.Equal(unlimited, slug);
                        }

                        previous = slug;
                    }
                }
            }
        }
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Slug_MaximumLength_Properties()
    {
        string[] texts =
        [
            "\u00E9\u00E9\u00E9\u00E9",
            "caf\u00E9 cr\u00E8me br\u00FBl\u00E9e",
            "\uAC00\uAC01\uAC02",
            "a\u0301\u0302\u0303\u0304b",
            "  \u00E9 \u00E9  ",
            "\U0001F600\u00E9\U0001F600",
            "Ti\u1EBFng Vi\u1EC7t",
            "a-b--c",
            "_b\u00C1\u11A8--__a!",
        ];

        foreach (var text in texts)
        {
            foreach (var separator in new[] { "-", "__", "" })
            {
                foreach (var clearAllowedRanges in new[] { true, false })
                {
                    foreach (var canEndWithSeparator in new[] { true, false })
                    {
                        SlugOptions CreateOptions(int maximumLength)
                        {
                            var options = new SlugOptions { MaximumLength = maximumLength, Separator = separator, CanEndWithSeparator = canEndWithSeparator };
                            if (clearAllowedRanges)
                            {
                                options.AllowedRanges.Clear();
                            }

                            return options;
                        }

                        var context = $"[{text}] separator '{separator}' cleared={clearAllowedRanges} canEndWithSeparator={canEndWithSeparator}";
                        var unlimited = Slug.Create(text, CreateOptions(0));
                        var previous = "";

                        for (var maximumLength = 1; maximumLength <= 24; maximumLength++)
                        {
                            var slug = Slug.Create(text, CreateOptions(maximumLength));

                            Assert.HasCountLessThanOrEqual(maximumLength, slug, $"{context} with limit {maximumLength} exceeded the limit");
                            Assert.Equal(slug, slug.Normalize(NormalizationForm.FormC));

                            // Raising the limit only ever appends, so the shorter slug is a prefix of the longer one.
                            Assert.StartsWith(previous, slug, message: $"{context}: limit {maximumLength} does not extend the slug from limit {maximumLength - 1}");

                            // A limit that can hold the whole slug produces exactly it, so truncation is the only
                            // thing the limit ever does.
                            if (maximumLength >= unlimited.Length)
                            {
                                Assert.Equal(unlimited, slug);
                            }

                            previous = slug;
                        }
                    }
                }
            }
        }
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Slug_MaximumLength_IsFilled_NotMerelyRespected()
    {
        string[] texts =
        [
            "\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9",
            "\u1EC7\u1EC7\u1EC7\u1EC7",
            "Ti\u1EBFng Vi\u1EC7t",
            "\uAC00\uAC01\uAC02",
            "caf\u00E9 cr\u00E8me br\u00FBl\u00E9e",
        ];

        foreach (var text in texts)
        {
            for (var maximumLength = 1; maximumLength <= 20; maximumLength++)
            {
                var options = new SlugOptions { MaximumLength = maximumLength };
                options.AllowedRanges.Clear();

                var slug = Slug.Create(text, options);

                // The longest prefix of the input whose composed form still fits the limit.
                var longestThatFits = "";
                for (var take = 1; take <= text.Length; take++)
                {
                    var candidate = text[..take].Normalize(NormalizationForm.FormC);
                    if (candidate.Length > maximumLength)
                        break;

                    longestThatFits = candidate;
                }

                Assert.HasCountLessThanOrEqual(maximumLength, slug, $"[{text}] with limit {maximumLength} exceeded the limit");
                Assert.Equal(longestThatFits, slug);
            }
        }
    }

}
