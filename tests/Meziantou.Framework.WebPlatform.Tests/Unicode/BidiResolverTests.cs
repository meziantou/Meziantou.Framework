using System.Text;
using Meziantou.Framework.WebPlatform.Css.Layout;
using CssUnicodeBidi = Meziantou.Framework.WebPlatform.Css.Layout.UnicodeBidi;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Tests.Unicode;

/// <summary>The bidi resolution of the layout (UAX #9 with the CSS bidi control codes, https://drafts.csswg.org/css-writing-modes-4/#text-direction).</summary>
public sealed class BidiResolverTests
{
    // Uppercase ASCII letters stand for Hebrew letters (strong right-to-left characters)
    private static string Text(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c is >= 'A' and <= 'Z' ? (char)(0x05D0 + (c - 'A')) : c);
        }

        return builder.ToString();
    }

    // Returns the line in visual order (from the line-left edge), with the Hebrew letters as uppercase ASCII letters and without
    // the bidi control codes
    private static string Visual(string text, BidiParagraphDirection direction)
    {
        var resolver = new BidiResolver();
        resolver.Resolve(text, direction);
        var levels = new byte[text.Length];
        resolver.GetLineLevels(0, text.Length, levels);
        var order = new int[text.Length];
        BidiResolver.ReorderVisual(levels, order);
        var builder = new StringBuilder();
        foreach (var index in order)
        {
            var c = text[index];
            if (c is >= (char)0x202A and <= (char)0x202E or >= (char)0x2066 and <= (char)0x2069)
                continue;

            builder.Append(c is >= (char)0x05D0 and <= (char)0x05EA ? (char)('A' + (c - 0x05D0)) : c);
        }

        return builder.ToString();
    }

    [Fact]
    public void MixedText()
    {
        Assert.Equal("car means RAC.", Visual(Text("car means CAR."), BidiParagraphDirection.LeftToRight));
        Assert.Equal(".RAC SNAEM car", Visual(Text("car MEANS CAR."), BidiParagraphDirection.RightToLeft));
        Assert.Equal("he said \"456 ,123 SEULAV EHT\".", Visual(Text("he said \"THE VALUES 123, 456\"."), BidiParagraphDirection.LeftToRight));
    }

    [Fact]
    public void LeftToRightText()
    {
        // The characters that can be R, AL or AN, or an explicit formatting character, and the paragraph separators are LF, CR,
        // U+001C to U+001E, U+0085 and U+2029
        for (var c = 0; c <= char.MaxValue; c++)
        {
            if (BidiResolver.MayBeRightToLeft((char)c))
                continue;

            var type = CharacterProperties.GetBidiClass(c);
            Assert.False(type is BidiClass.R or BidiClass.AL or BidiClass.AN or BidiClass.LRE or BidiClass.RLE or BidiClass.LRO or BidiClass.RLO or BidiClass.PDF or BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI, $"U+{c:X4} is {type}");
            Assert.Equal(c is '\n' or '\r' or 0x1C or 0x1D or 0x1E or 0x85 or 0x2029, type == BidiClass.B);
        }

        // The levels of a left-to-right text are the ones of the resolution from the classes
        var text = "Hello, world! 123 + 4.5 = caf\u00E9 na\u0308ive \u0391\u03B2\u03B3 \u0430\u0431\r\nnext\u0085(paragraph)\u001C\t\u00A0\u201Cend\u201D \u2013 \u4E2D\u6587\u2029x ";
        var classes = new BidiClass[text.Length];
        BidiResolver.GetClasses(text, classes);
        foreach (var direction in (ReadOnlySpan<BidiParagraphDirection>)[BidiParagraphDirection.LeftToRight, BidiParagraphDirection.AutoLeftToRight])
        {
            var resolver = new BidiResolver();
            resolver.Resolve(text, direction);
            var reference = new BidiResolver();
            reference.Resolve(text, classes, direction);
            Assert.Equal(reference.Paragraphs.ToArray(), resolver.Paragraphs.ToArray());
            Assert.Equal(reference.Levels.ToArray(), resolver.Levels.ToArray());
            var expected = new byte[text.Length];
            reference.GetLineLevels(0, text.Length, expected);
            Assert.Equal(expected, BidiResolver.GetLevels(text, direction));
            Assert.Equal(6, resolver.Paragraphs.Count);
        }
    }

    [Fact]
    public void AutoDirection()
    {
        var resolver = new BidiResolver();
        resolver.Resolve(Text("123 ABC"), BidiParagraphDirection.AutoLeftToRight);
        Assert.Equal(1, resolver.Paragraphs[0].Level);

        resolver.Resolve("123 !", BidiParagraphDirection.AutoLeftToRight);
        Assert.Equal(0, resolver.Paragraphs[0].Level);

        resolver.Resolve("123 !", BidiParagraphDirection.AutoRightToLeft);
        Assert.Equal(1, resolver.Paragraphs[0].Level);

        // P2 skips the isolates
        resolver.Resolve(BidiControls.LeftToRightIsolate + "abc" + BidiControls.PopDirectionalIsolate + Text("ABC"), BidiParagraphDirection.AutoLeftToRight);
        Assert.Equal(1, resolver.Paragraphs[0].Level);
    }

    [Fact]
    public void Paragraphs()
    {
        // P1: each paragraph has its own level with rules P2 and P3; the separator ends the paragraph
        var resolver = new BidiResolver();
        var text = "abc" + (char)0x2029 + Text("ABC") + "\ndef";
        resolver.Resolve(text, BidiParagraphDirection.AutoLeftToRight);
        Assert.Equal([new BidiParagraph(0, 4, 0), new BidiParagraph(4, 8, 1), new BidiParagraph(8, 11, 0)], resolver.Paragraphs);
        Assert.Equal(1, resolver.GetParagraphIndex(7));
        Assert.Equal(1, resolver.Levels[7]);
    }

    [Fact]
    public void Paragraphs_IsDefaultLevel()
    {
        // The level of a paragraph without strong character (outside of its isolates) is the default of rule P3
        var resolver = new BidiResolver();
        resolver.Resolve("!\n" + Text("ABC") + "\n1.\n" + BidiControls.LeftToRightIsolate + "abc" + BidiControls.PopDirectionalIsolate, BidiParagraphDirection.AutoRightToLeft);
        Assert.Equal([true, false, true, true], resolver.Paragraphs.Select(paragraph => paragraph.IsDefaultLevel));
        Assert.Equal([1, 1, 1, 1], resolver.Paragraphs.Select(paragraph => (int)paragraph.Level));

        // Without right-to-left character
        resolver.Resolve("!\nabc", BidiParagraphDirection.AutoLeftToRight);
        Assert.Equal([true, false], resolver.Paragraphs.Select(paragraph => paragraph.IsDefaultLevel));

        // The level is set by the higher-level protocol
        resolver.Resolve("!\n" + Text("ABC"), BidiParagraphDirection.LeftToRight);
        Assert.Equal([false, false], resolver.Paragraphs.Select(paragraph => paragraph.IsDefaultLevel));
    }

    [Fact]
    public void CssIsolateAndOverride()
    {
        // <p dir=ltr>abc <span dir=rtl>DEF ghi</span> jkl</p>: the span is an isolate (unicode-bidi: isolate)
        var text = Text("abc ") + BidiControls.GetInlineStart(CssUnicodeBidi.Isolate, isRightToLeft: true) + Text("DEF ghi") + BidiControls.GetInlineEnd(CssUnicodeBidi.Isolate) + " jkl";
        Assert.Equal("abc ghi FED jkl", Visual(text, BidiParagraphDirection.LeftToRight));

        // <bdo dir=rtl>abc</bdo> (unicode-bidi: isolate-override)
        text = "x " + BidiControls.GetInlineStart(CssUnicodeBidi.IsolateOverride, isRightToLeft: true) + "abc" + BidiControls.GetInlineEnd(CssUnicodeBidi.IsolateOverride) + " y";
        Assert.Equal("x cba y", Visual(text, BidiParagraphDirection.LeftToRight));

        // unicode-bidi: bidi-override on a block container
        text = BidiControls.GetBlockContainerStart(CssUnicodeBidi.BidiOverride, isRightToLeft: true) + "abc def" + BidiControls.GetBlockContainerEnd(CssUnicodeBidi.BidiOverride);
        Assert.Equal("fed cba", Visual(text, BidiControls.GetParagraphDirection(CssUnicodeBidi.BidiOverride, isRightToLeft: true)));

        // unicode-bidi: plaintext on a block container: rules P2 and P3, whatever the direction of the block container (a
        // paragraph without strong character is left-to-right)
        Assert.Equal(BidiParagraphDirection.AutoLeftToRight, BidiControls.GetParagraphDirection(CssUnicodeBidi.Plaintext, isRightToLeft: true));
        Assert.Equal("CBA abc", Visual(Text("ABC abc"), BidiParagraphDirection.LeftToRight));
        Assert.Equal("abc CBA", Visual(Text("ABC abc"), BidiControls.GetParagraphDirection(CssUnicodeBidi.Plaintext, isRightToLeft: false)));
    }

    [Fact]
    public void ControlCodes()
    {
        Assert.Equal("", BidiControls.GetInlineStart(CssUnicodeBidi.Normal, isRightToLeft: true));
        Assert.Equal(new string([BidiControls.RightToLeftEmbedding]), BidiControls.GetInlineStart(CssUnicodeBidi.Embed, isRightToLeft: true));
        Assert.Equal(new string([BidiControls.FirstStrongIsolate, BidiControls.LeftToRightOverride]), BidiControls.GetInlineStart(CssUnicodeBidi.IsolateOverride, isRightToLeft: false));
        Assert.Equal(new string([BidiControls.PopDirectionalFormatting, BidiControls.PopDirectionalIsolate]), BidiControls.GetInlineEnd(CssUnicodeBidi.IsolateOverride));
        Assert.Equal(new string([BidiControls.FirstStrongIsolate]), BidiControls.GetInlineStart(CssUnicodeBidi.Plaintext, isRightToLeft: true));
        Assert.Equal("", BidiControls.GetBlockContainerStart(CssUnicodeBidi.Isolate, isRightToLeft: true));

        Assert.Equal(BidiClass.ON, BidiControls.GetAtomicInlineClass(isReplaced: false, CssUnicodeBidi.BidiOverride, isRightToLeft: true));
        Assert.Equal(BidiClass.ON, BidiControls.GetAtomicInlineClass(isReplaced: true, CssUnicodeBidi.Isolate, isRightToLeft: true));
        Assert.Equal(BidiClass.R, BidiControls.GetAtomicInlineClass(isReplaced: true, CssUnicodeBidi.Embed, isRightToLeft: true));
        Assert.Equal(BidiClass.L, BidiControls.GetAtomicInlineClass(isReplaced: true, CssUnicodeBidi.BidiOverride, isRightToLeft: false));
    }

    [Fact]
    public void AtomicInlineClass()
    {
        // An atomic inline between Hebrew words is neutral, except a replaced element with unicode-bidi: embed
        var text = Text("AB ") + BidiControls.ObjectReplacementCharacter + Text(" CD");
        var classes = new BidiClass[text.Length];
        BidiResolver.GetClasses(text, classes);
        Assert.Equal(BidiClass.ON, classes[3]);

        var resolver = new BidiResolver();
        resolver.Resolve(text, classes, BidiParagraphDirection.LeftToRight);
        Assert.Equal(1, resolver.Levels[3]);
        Assert.Equal(1, resolver.Levels[2]);

        classes[3] = BidiControls.GetAtomicInlineClass(isReplaced: true, CssUnicodeBidi.Embed, isRightToLeft: false);
        resolver.Resolve(text, classes, BidiParagraphDirection.LeftToRight);
        Assert.Equal(0, resolver.Levels[3]);
        Assert.Equal(0, resolver.Levels[2]);
        Assert.Equal(0, resolver.Levels[4]);
    }

    [Fact]
    public void LineLevels()
    {
        // L1: the trailing whitespace of each line and the segment separators get the paragraph level
        var text = Text("ABC DEF\tGHI ");
        var resolver = new BidiResolver();
        resolver.Resolve(text, BidiParagraphDirection.LeftToRight);
        Assert.Equal(1, resolver.Levels[3]);

        var levels = new byte[4];
        resolver.GetLineLevels(0, 4, levels);
        Assert.Equal([1, 1, 1, 0], levels);

        levels = new byte[8];
        resolver.GetLineLevels(4, 12, levels);
        Assert.Equal([1, 1, 1, 0, 1, 1, 1, 0], levels);
    }

    [Fact]
    public void SurrogatePairs()
    {
        // U+10900 PHOENICIAN LETTER ALF (R), U+1D7CE MATHEMATICAL BOLD DIGIT ZERO (EN)
        var text = "a " + char.ConvertFromUtf32(0x10900) + char.ConvertFromUtf32(0x1D7CE) + " b";
        Assert.Equal([0, 0, 1, 1, 2, 2, 0, 0], BidiResolver.GetLevels(text, BidiParagraphDirection.LeftToRight));
    }

    [Fact]
    public void Brackets()
    {
        // N0: the brackets take the direction of their content (the glyphs of the brackets at an odd level are mirrored by L4)
        Assert.Equal(")FED( CBA xyz", Visual(Text("ABC (DEF) xyz"), BidiParagraphDirection.LeftToRight));
        Assert.Equal("xyz )abc( CBA", Visual(Text("ABC (abc) xyz"), BidiParagraphDirection.RightToLeft));
    }

    [Fact]
    public void VisualRuns()
    {
        var levels = BidiResolver.GetLevels(Text("ab CD ef"), BidiParagraphDirection.LeftToRight);
        var runs = new List<BidiRun>();
        BidiResolver.GetVisualRuns(levels, runs);
        Assert.Equal([new BidiRun(0, 3, 0), new BidiRun(3, 5, 1), new BidiRun(5, 8, 0)], runs);

        runs.Clear();
        levels = BidiResolver.GetLevels(Text("AB cd 12 EF"), BidiParagraphDirection.RightToLeft);
        BidiResolver.GetVisualRuns(levels, runs);
        Assert.Equal([new BidiRun(8, 11, 1), new BidiRun(3, 8, 2), new BidiRun(0, 3, 1)], runs);

        runs.Clear();
        levels = BidiResolver.GetLevels(Text("AB 12 EF"), BidiParagraphDirection.RightToLeft);
        BidiResolver.GetVisualRuns(levels, runs);
        Assert.Equal([new BidiRun(5, 8, 1), new BidiRun(3, 5, 2), new BidiRun(0, 3, 1)], runs);
    }

    [Fact]
    public void Properties()
    {
        Assert.Equal(BidiClass.R, CharacterProperties.GetBidiClass(0x05D0));
        Assert.Equal(BidiClass.AL, CharacterProperties.GetBidiClass(0x0627));
        Assert.Equal(BidiClass.AN, CharacterProperties.GetBidiClass(0x0661));
        Assert.Equal(BidiClass.FSI, CharacterProperties.GetBidiClass(0x2068));
        Assert.Equal(BidiClass.BN, CharacterProperties.GetBidiClass(0x200D));

        Assert.Equal(UnicodeBidiPairedBracketType.Open, UnicodeProperties.GetBidiPairedBracketType('['));
        Assert.Equal(UnicodeBidiPairedBracketType.Close, UnicodeProperties.GetBidiPairedBracketType(')'));
        Assert.Equal(UnicodeBidiPairedBracketType.None, UnicodeProperties.GetBidiPairedBracketType('<'));
        Assert.Equal(']', CharacterProperties.GetBidiPairedBracket('['));
        Assert.Equal('a', CharacterProperties.GetBidiPairedBracket('a'));

        // L4
        Assert.Equal(')', CharacterProperties.GetBidiMirroringGlyph('('));
        Assert.Equal('<', CharacterProperties.GetBidiMirroringGlyph('>'));
        Assert.Equal(-1, CharacterProperties.GetBidiMirroringGlyph('a'));
        Assert.True(UnicodeProperties.IsBidiMirrored(0x2211));
        Assert.Equal(-1, CharacterProperties.GetBidiMirroringGlyph(0x2211));
        Assert.False(UnicodeProperties.IsBidiMirrored('a'));
    }
}
