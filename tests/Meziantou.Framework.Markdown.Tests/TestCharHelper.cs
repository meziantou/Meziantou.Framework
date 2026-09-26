using System.Globalization;

using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.Framework.Markdown.Tests;

public class TestCharHelper
{
    // An ASCII punctuation character is
    // !, ", #, $, %, &, ', (, ), *, +, ,, -, ., / (U+0021–2F),
    // :, ;, <, =, >, ?, @ (U+003A–0040),
    // [, \, ], ^, _, ` (U+005B–0060),
    // {, |, }, or ~ (U+007B–007E).
    private static readonly HashSet<char> AsciiPunctuation = new()
    {
        '!', '"', '#', '$', '%', '&', '\'', '(', ')', '*', '+', ',', '-', '.', '/',
        ':', ';', '<', '=', '>', '?', '@',
        '[', '\\', ']', '^', '_', '`',
        '{', '|', '}', '~'
    };

    // A Unicode punctuation character is a character in the Unicode P (punctuation) or S (symbol) general categories.
    private static readonly HashSet<UnicodeCategory> PunctuationCategories =
    [
        UnicodeCategory.ConnectorPunctuation,
        UnicodeCategory.DashPunctuation,
        UnicodeCategory.OpenPunctuation,
        UnicodeCategory.ClosePunctuation,
        UnicodeCategory.InitialQuotePunctuation,
        UnicodeCategory.FinalQuotePunctuation,
        UnicodeCategory.OtherPunctuation,
        UnicodeCategory.MathSymbol,
        UnicodeCategory.CurrencySymbol,
        UnicodeCategory.ModifierSymbol,
        UnicodeCategory.OtherSymbol,
    ];

    private static readonly HashSet<UnicodeCategory> PunctuationWithoutSymbolsCategories =
    [
        UnicodeCategory.ConnectorPunctuation,
        UnicodeCategory.DashPunctuation,
        UnicodeCategory.OpenPunctuation,
        UnicodeCategory.ClosePunctuation,
        UnicodeCategory.InitialQuotePunctuation,
        UnicodeCategory.FinalQuotePunctuation,
        UnicodeCategory.OtherPunctuation,
    ];

    private static bool ExpectedIsPunctuation(char c)
    {
        return c <= 127
            ? AsciiPunctuation.Contains(c)
            : PunctuationCategories.Contains(CharUnicodeInfo.GetUnicodeCategory(c));
    }

    private static bool ExpectedIsPunctuationWithoutSymbols(char c)
    {
        return c <= 127
            ? AsciiPunctuation.Contains(c)
            : PunctuationWithoutSymbolsCategories.Contains(CharUnicodeInfo.GetUnicodeCategory(c));
    }

    private static bool ExpectedIsWhitespace(char c)
    {
        // A Unicode whitespace character is any code point in the Unicode Zs general category,
        // or a tab (U+0009), line feed (U+000A), form feed (U+000C), or carriage return (U+000D).
        return c == '\t' || c == '\n' || c == '\f' || c == '\r' ||
            CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;
    }

    [Fact]
    public void IsAcrossTab()
    {
        Assert.False(CharHelper.IsAcrossTab(0));
        Assert.True(CharHelper.IsAcrossTab(1));
        Assert.True(CharHelper.IsAcrossTab(2));
        Assert.True(CharHelper.IsAcrossTab(3));
        Assert.False(CharHelper.IsAcrossTab(4));
    }

    [Fact]
    public void AddTab()
    {
        Assert.Equal(4, CharHelper.AddTab(0));
        Assert.Equal(4, CharHelper.AddTab(1));
        Assert.Equal(4, CharHelper.AddTab(2));
        Assert.Equal(4, CharHelper.AddTab(3));
        Assert.Equal(8, CharHelper.AddTab(4));
        Assert.Equal(8, CharHelper.AddTab(5));
    }

    [Fact]
    public void IsWhitespace()
    {
        Test(
            ExpectedIsWhitespace,
            CharHelper.IsWhitespace);

        Test(
            ExpectedIsWhitespace,
            CharHelper.WhitespaceChars.Contains);
    }

    [Fact]
    public void IsWhiteSpaceOrZero()
    {
        Test(
            c => ExpectedIsWhitespace(c) || c == 0,
            CharHelper.IsWhiteSpaceOrZero);
    }

    [Fact]
    public void IsAsciiPunctuation()
    {
        Test(
            c => char.IsAscii(c) && ExpectedIsPunctuation(c),
            CharHelper.IsAsciiPunctuation);
    }

    [Fact]
    public void IsAsciiPunctuationOrZero()
    {
        Test(
            c => char.IsAscii(c) && (ExpectedIsPunctuation(c) || c == 0),
            CharHelper.IsAsciiPunctuationOrZero);
    }

    [Fact]
    public void IsSpaceOrPunctuationForGFMAutoLink()
    {
        Test(
            c => c == 0 || ExpectedIsWhitespace(c) || ExpectedIsPunctuationWithoutSymbols(c),
            CharHelper.IsSpaceOrPunctuationForGFMAutoLink);
    }

    [Fact]
    public void InvalidAutoLinkCharacters()
    {
        // 6.5 Autolinks - https://spec.commonmark.org/0.31.2/#autolinks
        // An absolute URI, for these purposes, consists of a scheme followed by a colon (:) followed by
        // zero or more characters other than ASCII control characters, space, <, and >.
        //
        // 2.1 Characters and lines
        // An ASCII control character is a character between U+0000–1F (both including) or U+007F.
        Test(
            c => c != 0 && c is < (char)0x20 or ' ' or '<' or '>' or '\u007F',
            CharHelper.InvalidAutoLinkCharacters.Contains);
    }

    [Fact]
    public void CheckUnicodeCategory()
    {
        for (int i = char.MinValue; i <= char.MaxValue; i++)
        {
            char c = (char)i;

            bool expectedSpace = c == 0 || ExpectedIsWhitespace(c);
            bool expectedPunctuation = c == 0 || ExpectedIsPunctuation(c);

            CharHelper.CheckUnicodeCategory(c, out bool spaceActual, out bool punctuationActual);

            Assert.Equal(expectedSpace, spaceActual);
            Assert.Equal(expectedPunctuation, punctuationActual);
        }
    }

    [Fact]
    public void IsControl()
    {
        Test(
            char.IsControl,
            CharHelper.IsControl);
    }

    [Fact]
    public void IsAlpha()
    {
        Test(
            c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'),
            CharHelper.IsAlpha);
    }

    [Fact]
    public void IsAlphaUpper()
    {
        Test(
            c => c >= 'A' && c <= 'Z',
            CharHelper.IsAlphaUpper);
    }

    [Fact]
    public void IsAlphaNumeric()
    {
        Test(
            c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'),
            CharHelper.IsAlphaNumeric);
    }

    [Fact]
    public void IsDigit()
    {
        Test(
            c => c >= '0' && c <= '9',
            CharHelper.IsDigit);
    }

    [Fact]
    public void IsNewLineOrLineFeed()
    {
        Test(
            c => c is '\r' or '\n',
            CharHelper.IsNewLineOrLineFeed);
    }

    [Fact]
    public void IsSpaceOrTab()
    {
        Test(
            c => c is ' ' or '\t',
            CharHelper.IsSpaceOrTab);
    }

    [Fact]
    public void IsEscapableSymbol()
    {
        Test(
            "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~•".Contains,
            CharHelper.IsEscapableSymbol);
    }

    [Fact]
    public void IsEmailUsernameSpecialChar()
    {
        Test(
            ".!#$%&'*+/=?^_`{|}~-+.~".Contains,
            CharHelper.IsEmailUsernameSpecialChar);
    }

    [Fact]
    public void IsEmailUsernameSpecialCharOrDigit()
    {
        Test(
            c => CharHelper.IsDigit(c) || ".!#$%&'*+/=?^_`{|}~-+.~".Contains(c, StringComparison.Ordinal),
            CharHelper.IsEmailUsernameSpecialCharOrDigit);
    }

    private static void Test(Func<char, bool> expected, Func<char, bool> actual)
    {
        for (int i = char.MinValue; i <= char.MaxValue; i++)
        {
            char c = (char)i;

            bool expectedResult = expected(c);
            bool actualResult = actual(c);

            if (expectedResult != actualResult)
            {
                Assert.Equal(expectedResult, actualResult, message: $"Char: '{c}' ({i})");
            }
        }
    }
}
