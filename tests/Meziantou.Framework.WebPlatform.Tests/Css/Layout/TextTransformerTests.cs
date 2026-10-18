using System.Text;
using Meziantou.Framework.WebPlatform.Css.Layout;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The text transformations of <c>text-transform</c> (<see cref="TextTransformer"/>): the case mappings that depend on the context and on the content language.</summary>
public sealed class TextTransformerTests
{
    private static string Transform(string text, TextTransform transform, string? language = null)
    {
        var result = new StringBuilder();
        Span<char> buffer = stackalloc char[TextTransformer.MaxLength];
        var caseLanguage = TextTransformer.GetCaseLanguage(language);
        var inWord = false;
        for (var i = 0; i < text.Length;)
        {
            var count = TextTransformer.Transform(text, i, transform, caseLanguage, wordStart: !inWord, buffer, out var consumed);
            result.Append(buffer[..count]);
            inWord = TextTransformer.IsWordCharacter(text, i);
            i += consumed;
        }

        return result.ToString();
    }

    [Theory]
    [InlineData("abc \U000000E9", null, "ABC \U000000C9")]

    // The characters outside of the BMP (Deseret), the long s
    [InlineData("\U00010428", null, "\U00010400")]
    [InlineData("\U0000017F", null, "S")]

    // Greek: the accents are removed, the dialytika is kept (or added to the second vowel of a diphthong whose first vowel loses
    // its accent), the disjunctive eta keeps its tonos
    [InlineData("\U000003BA\U000003B1\U000003BB\U000003B7\U000003BC\U000003AD\U000003C1\U000003B1", "el", "\U0000039A\U00000391\U0000039B\U00000397\U0000039C\U00000395\U000003A1\U00000391")]
    [InlineData("\U000003B8\U000003B5\U000003CA\U000003BA\U000003CC", "el", "\U00000398\U00000395\U000003AA\U0000039A\U0000039F")]
    [InlineData("\U0000039D\U000003B5\U000003C1\U000003AC\U000003B9\U000003B4\U000003B1", "el", "\U0000039D\U00000395\U000003A1\U00000391\U000003AA\U00000394\U00000391")]
    [InlineData("\U000003AE\U000003C3\U000003BF\U000003C5\U000003BD \U000003AE \U000003B5\U000003B3\U000003CE", "el", "\U00000397\U000003A3\U0000039F\U000003A5\U0000039D \U00000389 \U00000395\U00000393\U000003A9")]
    [InlineData("\U000003AD", null, "\U00000388")]

    // Lithuanian: the dot above of the soft-dotted letters is removed; not in the other languages, nor after the other letters
    [InlineData("i\U00000307\U00000300 x\U00000307", "lt", "I\U00000300 X\U00000307")]
    [InlineData("i\U00000307", null, "I\U00000307")]

    // Turkish and Azeri: the dotted capital I, unless the script subtag is not Latin
    [InlineData("i", "tr", "\U00000130")]
    [InlineData("i", "az-Latn", "\U00000130")]
    [InlineData("i", "tr-Cyrl", "I")]
    public void Uppercase(string text, string? language, string expected) => Assert.Equal(expected, Transform(text, TextTransform.Uppercase, language));

    [Theory]

    // The final sigma
    [InlineData("\U0000039F\U00000394\U000003A5\U000003A3\U000003A3\U00000395\U000003A5\U000003A3 \U000003A3", null, "\U000003BF\U000003B4\U000003C5\U000003C3\U000003C3\U000003B5\U000003C5\U000003C2 \U000003C3")]
    [InlineData("\U00010400", null, "\U00010428")]

    // Lithuanian: the dot above is kept under the accents
    [InlineData("\U000000CC I\U00000300 J", "lt", "i\U00000307\U00000300 i\U00000307\U00000300 j")]
    [InlineData("I\U00000300", null, "i\U00000300")]

    // Turkish: the dotless i, and the capital I with a dot above
    [InlineData("I \U00000130 I\U00000307", "tr", "\U00000131 i i")]
    [InlineData("I", null, "i")]
    public void Lowercase(string text, string? language, string expected) => Assert.Equal(expected, Transform(text, TextTransform.Lowercase, language));

    [Theory]
    [InlineData("hello world", null, "Hello World")]

    // The punctuation inside a word and the combining marks do not start a word
    [InlineData("cancel\U000000B7lar e\U00000301cole", null, "Cancel\U000000B7lar E\U00000301cole")]

    // The Dutch digraph
    [InlineData("ijsland", "nl", "IJsland")]
    [InlineData("ijsland", "NL", "IJsland")]
    [InlineData("ijsland", "en", "Ijsland")]
    [InlineData("\U00010428\U00010429", null, "\U00010400\U00010429")]
    [InlineData("\U000001C6", null, "\U000001C5")]
    public void Capitalize(string text, string? language, string expected) => Assert.Equal(expected, Transform(text, TextTransform.Capitalize, language));

    [Theory]
    [InlineData("a 1", "\U0000FF41\U00003000\U0000FF11")]

    // The halfwidth katakana (the voiced sound mark is the combining one), Hangul letters and symbols
    [InlineData("\U0000FF76\U0000FF9E\U0000FF61", "\U000030AB\U00003099\U00003002")]
    [InlineData("\U0000FFA1\U0000FFDC", "\U00003131\U00003163")]
    [InlineData("\U000000A5\U00002985\U0000FFE9", "\U0000FFE5\U0000FF5F\U00002190")]
    [InlineData("\U00006C34", "\U00006C34")]
    public void FullWidth(string text, string expected) => Assert.Equal(expected, Transform(text, TextTransform.FullWidth));

    [Fact]
    public void FullSizeKana_OutsideOfTheBmp() => Assert.Equal("\U00003053\U00003042", Transform("\U0001B132\U00003041", TextTransform.FullSizeKana));
}
