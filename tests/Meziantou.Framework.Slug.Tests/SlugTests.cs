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

    private sealed class NoVowelSlugOptions : SlugOptions
    {
        public override bool IsAllowed(Rune character)
        {
            return base.IsAllowed(character) && !"aeiou".Contains(character.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    [InlineData(1, "")]
    [InlineData(2, "\u00E9")]
    [InlineData(3, "\u00E9\u00E9")]
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

        var slug = Slug.Create("\u00E9\u00E9\u00E9\u00E9", options);

        Assert.DoesNotContain('e', slug);
        Assert.Equal("\u00E9\u00E9\u00E9", slug);
    }

    [Fact]
    public void Slug_MaximumLength_IsNeverExceededOnComposingInput()
    {
        string[] inputs =
        [
            "\u00E9\u00E9\u00E9\u00E9",
            "caf\u00E9 cr\u00E8me br\u00FBl\u00E9e",
            "\uAC00\uAC01\uAC02",
            "  \u00E9 \u00E9  ",
            "\U0001F600\u00E9\U0001F600",
        ];

        foreach (var input in inputs)
        {
            for (var maximumLength = 1; maximumLength <= 24; maximumLength++)
            {
                foreach (var separator in new[] { "-", "__", "" })
                {
                    var options = new SlugOptions { MaximumLength = maximumLength, Separator = separator };
                    options.AllowedRanges.Clear();

                    var slug = Slug.Create(input, options);

                    Assert.HasCountLessThanOrEqual(maximumLength, slug, $"[{input}] with limit {maximumLength} and separator '{separator}'");
                    Assert.Equal(slug, slug.Normalize(NormalizationForm.FormC));
                }
            }
        }
    }

    [Fact]
    public void Slug_MaximumLength_RaisingTheLimitNeverShortensTheSlug()
    {
        var previousLength = 0;
        for (var maximumLength = 1; maximumLength <= 24; maximumLength++)
        {
            var options = new SlugOptions { MaximumLength = maximumLength };
            options.AllowedRanges.Clear();

            var slug = Slug.Create("caf\u00E9 cr\u00E8me br\u00FBl\u00E9e", options);

            Assert.HasCountGreaterThanOrEqual(previousLength, slug, $"limit {maximumLength} produced fewer characters than limit {maximumLength - 1}");
            previousLength = slug.Length;
        }
    }
}
