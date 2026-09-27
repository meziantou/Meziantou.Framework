// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Xunit;

namespace Meziantou.Framework.Markdown.Tests;

public class TestLinkHelper
{
    [Fact]
    public void TestUrlSimple()
    {
        var text = new StringSlice("toto tutu");
        Assert.True(LinkHelper.TryParseUrl(ref text, out string? link, out _));
        Assert.Equal("toto", link);
        Assert.Equal(' ', text.CurrentChar);
    }

    [Fact]
    public void TestUrlUrl()
    {
        var text = new StringSlice("http://google.com)");
        Assert.True(LinkHelper.TryParseUrl(ref text, out string? link, out _));
        Assert.Equal("http://google.com", link);
        Assert.Equal(')', text.CurrentChar);
    }

    [Theory]
    [InlineData("http://google.com.")]
    [InlineData("http://google.com. ")]
    public void TestUrlTrailingFullStop(string uri)
    {
        var text = new StringSlice(uri);
        Assert.True(LinkHelper.TryParseUrl(ref text, out string? link, out _, true));
        Assert.Equal("http://google.com", link);
        Assert.Equal('.', text.CurrentChar);
    }

    [Fact]
    public void TestUrlNestedParenthesis()
    {
        var text = new StringSlice("(toto)tutu(tata) nooo");
        Assert.True(LinkHelper.TryParseUrl(ref text, out string? link, out _));
        Assert.Equal("(toto)tutu(tata)", link);
        Assert.Equal(' ', text.CurrentChar);
    }

    [Fact]
    public void TestUrlAlternate()
    {
        var text = new StringSlice("<toto_tata_tutu> nooo");
        Assert.True(LinkHelper.TryParseUrl(ref text, out string? link, out _));
        Assert.Equal("toto_tata_tutu", link);
        Assert.Equal(' ', text.CurrentChar);
    }

    [Fact]
    public void TestUrlAlternateInvalid()
    {
        var text = new StringSlice("<toto_tata_tutu");
        Assert.False(LinkHelper.TryParseUrl(ref text, out _, out _));
    }

    [Fact]
    public void TestTitleSimple()
    {
        var text = new StringSlice(@"'tata\tutu\''");
        Assert.True(LinkHelper.TryParseTitle(ref text, out string? title, out _));
        Assert.Equal(@"tata\tutu'", title);
    }

    [Fact]
    public void TestTitleSimpleAlternate()
    {
        var text = new StringSlice(@"""tata\tutu\"""" ");
        Assert.True(LinkHelper.TryParseTitle(ref text, out string? title, out _));
        Assert.Equal(@"tata\tutu""", title);
        Assert.Equal(' ', text.CurrentChar);
    }

    [Fact]
    public void TestTitleMultiline()
    {
        var text = new StringSlice("'this\ris\r\na\ntitle'");
        Assert.True(LinkHelper.TryParseTitle(ref text, out string? title, out _));
        Assert.Equal("this\ris\r\na\ntitle", title);
    }

    [Fact]
    public void TestTitleMultilineWithSpaceAndBackslash()
    {
        var text = new StringSlice("'a\n\\ \\\ntitle'");
        Assert.True(LinkHelper.TryParseTitle(ref text, out string? title, out _));
        Assert.Equal("a\n\\ \\\ntitle", title);
    }

    [Fact]
    public void TestUrlAndTitle()
    {
        //                           0         1         2         3
        //                           0123456789012345678901234567890123456789
        var text = new StringSlice(@"(http://google.com 'this is a title')ABC");
        Assert.True(LinkHelper.TryParseInlineLink(ref text, out string? link, out string? title, out SourceSpan linkSpan, out SourceSpan titleSpan));
        Assert.Equal("http://google.com", link);
        Assert.Equal("this is a title", title);
        Assert.Equal(new SourceSpan(1, 17), linkSpan);
        Assert.Equal(new SourceSpan(19, 35), titleSpan);
        Assert.Equal('A', text.CurrentChar);
    }

    [Fact]
    public void TestUrlEmptyAndTitleNull()
    {
        //                           01234
        var text = new StringSlice(@"(<>)A");
        Assert.True(LinkHelper.TryParseInlineLink(ref text, out string? link, out string? title, out SourceSpan linkSpan, out SourceSpan titleSpan));
        Assert.Equal(string.Empty, link);
        Assert.Equal(null, title);
        Assert.Equal(new SourceSpan(1, 2), linkSpan);
        Assert.Equal(SourceSpan.Empty, titleSpan);
        Assert.Equal('A', text.CurrentChar);
    }

    [Fact]
    public void TestUrlEmptyAndTitleNull2()
    {
        //                           012345
        var text = new StringSlice(@"( <> )A");
        Assert.True(LinkHelper.TryParseInlineLink(ref text, out string? link, out string? title, out SourceSpan linkSpan, out SourceSpan titleSpan));
        Assert.Equal(string.Empty, link);
        Assert.Equal(null, title);
        Assert.Equal(new SourceSpan(2, 3), linkSpan);
        Assert.Equal(SourceSpan.Empty, titleSpan);
        Assert.Equal('A', text.CurrentChar);
    }


    [Fact]
    public void TestUrlEmptyWithTitleWithMultipleSpaces()
    {
        //                           0         1         2
        //                           0123456789012345678901234567
        var text = new StringSlice(@"(   <>      'toto'       )A");
        Assert.True(LinkHelper.TryParseInlineLink(ref text, out string? link, out string? title, out SourceSpan linkSpan, out SourceSpan titleSpan));
        Assert.Equal(string.Empty, link);
        Assert.Equal("toto", title);
        Assert.Equal(new SourceSpan(4, 5), linkSpan);
        Assert.Equal(new SourceSpan(12, 17), titleSpan);
        Assert.Equal('A', text.CurrentChar);
    }

    [Fact]
    public void TestUrlEmpty()
    {
        var text = new StringSlice(@"()A");
        Assert.True(LinkHelper.TryParseInlineLink(ref text, out string? link, out string? title, out SourceSpan linkSpan, out SourceSpan titleSpan));
        Assert.Equal(string.Empty, link);
        Assert.Equal(null, title);
        Assert.Equal(SourceSpan.Empty, linkSpan);
        Assert.Equal(SourceSpan.Empty, titleSpan);
        Assert.Equal('A', text.CurrentChar);
    }

    [Fact]
    public void TestMultipleLines()
    {
        //                          0          1         2          3
        //                          01 2345678901234567890 1234567890123456789
        var text = new StringSlice("(\n<http://google.com>\n    'toto' )A");
        Assert.True(LinkHelper.TryParseInlineLink(ref text, out string? link, out string? title, out SourceSpan linkSpan, out SourceSpan titleSpan));
        Assert.Equal("http://google.com", link);
        Assert.Equal("toto", title);
        Assert.Equal(new SourceSpan(2, 20), linkSpan);
        Assert.Equal(new SourceSpan(26, 31), titleSpan);
        Assert.Equal('A', text.CurrentChar);
    }

    [Fact]
    public void TestLabelSimple()
    {
        //                          01234
        var text = new StringSlice("[foo]");
        Assert.True(LinkHelper.TryParseLabel(ref text, out string? label, out SourceSpan labelSpan));
        Assert.Equal(new SourceSpan(1, 3), labelSpan);
        Assert.Equal("foo", label);
    }

    [Fact]
    public void TestLabelEscape()
    {
        //                           012345678
        var text = new StringSlice(@"[fo\[\]o]");
        Assert.True(LinkHelper.TryParseLabel(ref text, out string? label, out SourceSpan labelSpan));
        Assert.Equal(new SourceSpan(1, 7), labelSpan);
        Assert.Equal(@"fo[]o", label);
    }

    [Fact]
    public void TestLabelEscape2()
    {
        //                           0123
        var text = new StringSlice(@"[\]]");
        Assert.True(LinkHelper.TryParseLabel(ref text, out string? label, out SourceSpan labelSpan));
        Assert.Equal(new SourceSpan(1, 2), labelSpan);
        Assert.Equal(@"]", label);
    }

    [Fact]
    public void TestLabelInvalids()
    {
        Assert.False(LinkHelper.TryParseLabel(new StringSlice(@"a"), out _));
        Assert.False(LinkHelper.TryParseLabel(new StringSlice(@"["), out _));
        Assert.False(LinkHelper.TryParseLabel(new StringSlice(@"[\x]"), out _));
        Assert.False(LinkHelper.TryParseLabel(new StringSlice(@"[[]"), out _));
        Assert.False(LinkHelper.TryParseLabel(new StringSlice(@"[     ]"), out _));
        Assert.False(LinkHelper.TryParseLabel(new StringSlice(@"[  \t \n  ]"), out _));
    }

    [Fact]
    public void TestLabelWhitespaceCollapsedAndTrim()
    {
        //                           0         1         2         3
        //                           0123456789012345678901234567890123456789
        var text = new StringSlice(@"[     fo    o    z     ]");
        Assert.True(LinkHelper.TryParseLabel(ref text, out string? label, out SourceSpan labelSpan));
        Assert.Equal(new SourceSpan(6, 17), labelSpan);
        Assert.Equal(@"fo o z", label);
    }

    [Fact]
    public void TestlLinkReferenceDefinitionSimple()
    {
        //                           0         1         2         3
        //                           0123456789012345678901234567890123456789
        var text = new StringSlice(@"[foo]: /toto 'title'");
        Assert.True(LinkHelper.TryParseLinkReferenceDefinition(ref text, out string? label, out string? url, out string? title, out SourceSpan labelSpan, out SourceSpan urlSpan, out SourceSpan titleSpan));
        Assert.Equal(@"foo", label);
        Assert.Equal(@"/toto", url);
        Assert.Equal(@"title", title);
        Assert.Equal(new SourceSpan(1, 3), labelSpan);
        Assert.Equal(new SourceSpan(7, 11), urlSpan);
        Assert.Equal(new SourceSpan(13, 19), titleSpan);

    }

    [Theory]
    [InlineData("[foo]: /url\n\"title")]
    [InlineData("[foo]: /url\n'Tis the season.")]
    [InlineData("[foo]: /url \n (see")]
    [InlineData("[foo]: /url\n\"title\n\nnext")]
    public void TestLinkReferenceDefinitionEndsBeforeALineThatIsNotATitle(string markdown)
    {
        var text = new StringSlice(markdown);
        Assert.True(LinkHelper.TryParseLinkReferenceDefinition(ref text, out string? label, out string? url, out string? title, out _, out _, out SourceSpan titleSpan));
        Assert.Equal("foo", label);
        Assert.Equal("/url", url);
        Assert.Null(title);
        Assert.Equal(SourceSpan.Empty, titleSpan);
        Assert.Equal(markdown.IndexOfAny(['"', '\'', '(']), text.Start);

        text = new StringSlice(markdown);
        Assert.True(LinkHelper.TryParseLinkReferenceDefinitionTrivia(ref text, out _, out label, out _, out _, out url, out _, out _, out _, out title, out _, out _, out _, out _, out _, out _, out titleSpan));
        Assert.Equal("foo", label);
        Assert.Equal("/url", url);
        Assert.Null(title);
        Assert.Equal(SourceSpan.Empty, titleSpan);
    }

    [Fact]
    public void TestlLinkReferenceDefinitionInvalid()
    {
        var text = new StringSlice("[foo]: /url (title) x\n");
        Assert.False(LinkHelper.TryParseLinkReferenceDefinition(ref text, out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void TestAutoLinkUrlSimple()
    {
        var text = new StringSlice(@"<http://google.com>");
        Assert.True(LinkHelper.TryParseAutolink(ref text, out string? url, out bool isEmail));
        Assert.False(isEmail);
        Assert.Equal("http://google.com", url);
    }

    [Fact]
    public void TestAutoLinkEmailSimple()
    {
        var text = new StringSlice(@"<user@host.com>");
        Assert.True(LinkHelper.TryParseAutolink(ref text, out string? email, out bool isEmail));
        Assert.True(isEmail);
        Assert.Equal("user@host.com", email);
    }

    [Fact]
    public void TestAutolinkInvalid()
    {
        Assert.False(LinkHelper.TryParseAutolink(new StringSlice(@""), out _, out _));
        Assert.False(LinkHelper.TryParseAutolink(new StringSlice(@"<"), out _, out _));
        Assert.False(LinkHelper.TryParseAutolink(new StringSlice(@"<ab"), out _, out _));
        Assert.False(LinkHelper.TryParseAutolink(new StringSlice(@"<user@>"), out _, out _));
    }

    [Theory]
    [InlineData("Header identifiers in HTML", "header-identifiers-in-html")]
    [InlineData("* Dogs*?--in *my* house?", "dogs-in-my-house")] // Not Pandoc equivalent: dogs--in...
    [InlineData("[HTML], [S5], or [RTF]?", "html-s5-or-rtf")]
    [InlineData("3. Applications", "applications")]
    [InlineData("33", "")]
    public void TestUrilizeNonAscii_Pandoc(string input, string expectedResult)
    {
        Assert.Equal(expectedResult, LinkHelper.Urilize(input, false));
    }

    [Theory]
    [InlineData("Header identifiers in HTML", "header-identifiers-in-html")]
    [InlineData("* Dogs*?--in *my* house?", "-dogs--in-my-house")]
    [InlineData("[HTML], [S5], or [RTF]?", "html-s5-or-rtf")]
    [InlineData("3. Applications", "3-applications")]
    [InlineData("33", "33")]
    public void TestUrilizeGfm(string input, string expectedResult)
    {
        Assert.Equal(expectedResult, LinkHelper.UrilizeAsGfm(input));
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("a-c", "a-c")]
    [InlineData("a c", "a-c")]
    [InlineData("a_c", "a_c")]
    [InlineData("a.c", "a.c")]
    [InlineData("a,c", "ac")]
    [InlineData("a--", "a")] // Not Pandoc-equivalent: a--
    [InlineData("a__", "a")] // Not Pandoc-equivalent: a__
    [InlineData("a..", "a")] // Not Pandoc-equivalent: a..
    [InlineData("a??", "a")]
    [InlineData("a  ", "a")]
    [InlineData("a--d", "a-d")]
    [InlineData("a__d", "a_d")]
    [InlineData("a??d", "ad")]
    [InlineData("a  d", "a-d")]
    [InlineData("a..d", "a.d")]
    [InlineData("-bc", "bc")]
    [InlineData("_bc", "bc")]
    [InlineData(" bc", "bc")]
    [InlineData("?bc", "bc")]
    [InlineData(".bc", "bc")]
    [InlineData("a-.-", "a")] // Not Pandoc equivalent: a-.-
    public void TestUrilizeOnlyAscii_Simple(string input, string expectedResult)
    {
        Assert.Equal(expectedResult, LinkHelper.Urilize(input, true));
    }

    [Theory]
    [InlineData("bær", "baer")]
    [InlineData("bør", "boer")]
    [InlineData("bΘr", "br")]
    [InlineData("四五", "")]
    public void TestUrilizeOnlyAscii_NonAscii(string input, string expectedResult)
    {
        Assert.Equal(expectedResult, LinkHelper.Urilize(input, true));
    }

    [Theory]
    [InlineData("bár", "bar")]
    [InlineData("àrrivé", "arrive")]
    public void TestUrilizeOnlyAscii_Normalization(string input, string expectedResult)
    {
        Assert.Equal(expectedResult, LinkHelper.Urilize(input, true));
    }

    // Tests for NormalizeScandinavianOrGermanChar method mappings when allowOnlyAscii=true.
    // Note: NFD (Canonical Decomposition) is applied first:
    // - German umlauts ä,ö,ü decompose to base letter + combining mark (ü -> u + ¨)
    //   The combining mark is then stripped, leaving just the base letter (ü -> u)
    // - å decomposes similarly (å -> a + ˚ -> a)
    // - But ø, æ, ß, þ, ð do NOT decompose, so they use NormalizeScandinavianOrGermanChar
    [Theory]
    [InlineData("Straße", "strasse")]    // ß -> ss
    [InlineData("æble", "aeble")]        // æ -> ae
    [InlineData("Ærø", "aeroe")]         // Æ -> Ae, ø -> oe (then lowercase)
    [InlineData("København", "koebenhavn")] // ø -> oe
    [InlineData("Øresund", "oeresund")]  // Ø -> Oe (then lowercase)
    [InlineData("þing", "thing")]        // þ (thorn) -> th
    [InlineData("bað", "bad")]           // ð (eth) -> d
    [InlineData("øst-æble", "oest-aeble")] // ø->oe, æ->ae
    [InlineData("Þór Ðað", "thor-dad")]  // Þ -> Th, Ð -> D (then lowercase)
    [InlineData("ÆØß", "aeoess")]
    [InlineData("äöüÄÖÜåÅ", "aouaouaa")] // decomposed by NFD, not transliterated
    [InlineData("Ärger über Öl", "arger-uber-ol")]
    [InlineData("Åland på", "aland-pa")]
    public void TestUrilizeOnlyAscii_ScandinavianGermanChars(string input, string expectedResult)
    {
        Assert.Equal(expectedResult, LinkHelper.Urilize(input, true));
    }

    // Tests specific to allowOnlyAscii=true behavior
    // German umlauts (ä, ö, ü) and å decompose with NFD, so they become base letter only
    [Theory]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    [InlineData("schön", "schon")]       // ö decomposes to o (NFD strips combining mark)
    [InlineData("Mädchen", "madchen")]   // ä decomposes to a
    [InlineData("Übung", "ubung")]       // Ü decomposes to U (then lowercase to u)
    [InlineData("Düsseldorf", "dusseldorf")] // ü decomposes to u
    [InlineData("Käse", "kase")]         // ä decomposes to a
    [InlineData("gå", "ga")]             // å decomposes to a
    [InlineData("Ålesund", "alesund")]   // Å decomposes to A (then lowercase)
    [InlineData("grüßen", "grussen")]    // ü decomposes to u, ß -> ss
    [InlineData("Þór", "thor")]          // Þ -> Th, ó decomposes to o (then lowercase)
    [InlineData("Íslandsbanki", "islandsbanki")] // Í decomposes to I (then lowercase)
    public void TestUrilizeOnlyAscii_GermanUmlautsDecompose(string input, string expectedResult)
    {
        // With allowOnlyAscii=true, these characters decompose via NFD and lose their diacritics
        Assert.Equal(expectedResult, LinkHelper.Urilize(input, true));
    }

    // Tests specific to allowOnlyAscii=false behavior: non-ASCII letters are kept, not expanded to ASCII.
    [Theory]
    [InlineData("zeilenumbrüche", "zeilenumbrüche")]
    [InlineData("schön", "schön")]
    [InlineData("Mädchen", "mädchen")]
    [InlineData("Übung", "übung")]
    [InlineData("Düsseldorf", "düsseldorf")]
    [InlineData("Käse", "käse")]
    [InlineData("gå", "gå")]
    [InlineData("Ålesund", "ålesund")]
    [InlineData("grüßen", "grüßen")]
    [InlineData("Þór", "þór")]
    [InlineData("Straße", "straße")]
    [InlineData("æble", "æble")]
    [InlineData("Ærø", "ærø")]
    [InlineData("København", "københavn")]
    [InlineData("Øresund", "øresund")]
    [InlineData("þing", "þing")]
    [InlineData("bað", "bað")]
    [InlineData("øst-æble", "øst-æble")]
    [InlineData("Íslandsbanki", "íslandsbanki")] // í is kept as-is when allowOnlyAscii=false
    public void TestUrilizeNonAscii_KeepsNonAsciiLetters(string input, string expectedResult)
    {
        Assert.Equal(expectedResult, LinkHelper.Urilize(input, false));
    }

    [Theory]
    [InlineData("123", "")]
    [InlineData("1,-b", "b")]
    [InlineData("b1,-", "b1")] // Not Pandoc equivalent: b1-
    [InlineData("ab3", "ab3")]
    [InlineData("ab3de", "ab3de")]
    public void TestUrilizeOnlyAscii_Numeric(string input, string expectedResult)
    {
        Assert.Equal(expectedResult, LinkHelper.Urilize(input, true));
    }

    [Theory]
    [InlineData("一二三四五", "一二三四五")]
    [InlineData("一,-b", "一-b")]
    public void TestUrilizeNonAscii_NonAsciiNumeric(string input, string expectedResult)
    {
        Assert.Equal(expectedResult, LinkHelper.Urilize(input, false));
    }

    [Theory]
    [InlineData("bær", "bær")]
    [InlineData("æ5el", "æ5el")]
    [InlineData("-æ5el", "æ5el")]
    [InlineData("-frø-", "frø")]
    [InlineData("-fr-ø", "fr-ø")]
    public void TestUrilizeNonAscii_Simple(string input, string expectedResult)
    {
        Assert.Equal(expectedResult, LinkHelper.Urilize(input, false));
    }

    // Just to be sure, test for characters expressly forbidden in URI fragments:
    [Theory]
    [InlineData("b#r", "br")]
    [InlineData("b%r", "br")] // Invalid except as an escape character
    [InlineData("b^r", "br")]
    [InlineData("b[r", "br")]
    [InlineData("b]r", "br")]
    [InlineData("b{r", "br")]
    [InlineData("b}r", "br")]
    [InlineData("b<r", "br")]
    [InlineData("b>r", "br")]
    [InlineData(@"b\r", "br")]
    [InlineData(@"b""r", "br")]
    [InlineData(@"Requirement 😀", "requirement")]
    public void TestUrilizeNonAscii_NonValidCharactersForFragments(string input, string expectedResult)
    {
        Assert.Equal(expectedResult, LinkHelper.Urilize(input, false));
    }

    [Fact]
    public void TestUnicodeInDomainNameOfLinkReferenceDefinition()
    {
        TestParser.TestSpec("[Foo]\n\n[Foo]: http://ünicode.com", "<p><a href=\"http://xn--nicode-2ya.com\">Foo</a></p>");
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public void InlineLinkScanCacheGivesTheSameAnswersAsIndependentScans()
    {
        // The link parser tries the inline links of a leaf block from left to right, remembering the failed scans
        string[] atoms = ["](", "](", "(", ")", "a", " ", " ", "\"", "'", "\\", "<", ">", "\n", "\\(", "\\)", "((", "))", "\"t\"", "(t)"];
        var random = new Random(42);
        for (var n = 0; n < 5000; n++)
        {
            var text = string.Concat(Enumerable.Range(0, random.Next(1, 25)).Select(_ => atoms[random.Next(atoms.Length)]));
            var cache = new InlineLinkScanCache();
            for (var start = 1; start < text.Length; start++)
            {
                if (text[start] != '(' || text[start - 1] != ']')
                {
                    continue;
                }

                var slice = new StringSlice(text, start, text.Length - 1);
                cache.SetText(slice);
                var cached = slice;
                var independent = slice;
                var isCachedValid = LinkHelper.TryParseInlineLink(ref cached, out var cachedLink, out var cachedTitle, out var cachedLinkSpan, out var cachedTitleSpan, cache);
                var isValid = LinkHelper.TryParseInlineLink(ref independent, out var link, out var title, out var linkSpan, out var titleSpan, scanCache: null);

                var message = $"{text} {start}";
                Assert.Equal(isValid, isCachedValid, message);
                if (isValid)
                {
                    Assert.Equal(link, cachedLink, message);
                    Assert.Equal(title, cachedTitle, message);
                    Assert.Equal(linkSpan, cachedLinkSpan, message);
                    Assert.Equal(titleSpan, cachedTitleSpan, message);
                    Assert.Equal(independent.Start, cached.Start, message);
                }
            }
        }
    }
}
