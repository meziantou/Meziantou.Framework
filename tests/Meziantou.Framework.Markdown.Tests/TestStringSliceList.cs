using System.Collections;
using System.Text;

using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.Framework.Markdown.Tests;

public class TestStringSliceList
{
    // TODO: Add more tests for StringLineGroup

    [Fact]
    public void TestStringLineGroupSimple()
    {
        var text = new StringLineGroup(4)
        {
            new StringSlice("ABC", NewLine.LineFeed),
            new StringSlice("E", NewLine.LineFeed),
            new StringSlice("F")
        };

        var iterator = text.ToCharIterator();
        Assert.Equal("ABC\nE\nF".Length, iterator.End - iterator.Start + 1);

        var chars = ToString(text.ToCharIterator());
        TextAssert.AreEqual("ABC\nE\nF", chars.ToString());

        TextAssert.AreEqual("ABC\nE\nF", text.ToString());
    }

    [Fact]
    public void TestStringLineGroupWithSlices()
    {
        var text = new StringLineGroup(4)
        {
            new StringSlice("XABC", NewLine.LineFeed) { Start = 1},
            new StringSlice("YYE", NewLine.LineFeed) { Start = 2},
            new StringSlice("ZZZF") { Start = 3 }
        };

        var chars = ToString(text.ToCharIterator());
        TextAssert.AreEqual("ABC\nE\nF", chars.ToString());
    }


    private static string ToString(StringLineGroup.Iterator text)
    {
        var chars = new StringBuilder();
        while (text.CurrentChar != '\0')
        {
            chars.Append(text.CurrentChar);
            text.NextChar();
        }
        return chars.ToString();
    }

    [Fact]
    public void TestStringLineGroupSaveAndRestore()
    {
        var text = new StringLineGroup(4)
        {
            new StringSlice("ABCD", NewLine.LineFeed),
            new StringSlice("EF"),
        }.ToCharIterator();

        Assert.Equal('A', text.CurrentChar);
        Assert.Equal(0, text.SliceIndex);

        text.NextChar(); // B

        text.NextChar(); // C
        text.NextChar(); // D
        text.NextChar(); // \n
        text.NextChar();
        Assert.Equal('E', text.CurrentChar);
        Assert.Equal(1, text.SliceIndex);
    }

    [Fact]
    public void TestSkipWhitespaces()
    {
        var text = new StringLineGroup("             ABC").ToCharIterator();
        Assert.False(text.TrimStart());
        Assert.Equal('A', text.CurrentChar);

        text = new StringLineGroup("        ").ToCharIterator();
        Assert.True(text.TrimStart());
        Assert.Equal('\0', text.CurrentChar);

        var slice = new StringSlice("             ABC");
        Assert.False(slice.TrimStart());

        slice = new StringSlice("        ");
        Assert.True(slice.TrimStart());
    }

    [Fact]
    public void TestStringLineGroupWithModifiedStart()
    {
        var line1 = new StringSlice("  ABC", NewLine.LineFeed);
        line1.NextChar();
        line1.NextChar();

        var line2 = new StringSlice("  DEF ");
        line2.Trim();

        var text = new StringLineGroup(4) {line1, line2};

        var result = ToString(text.ToCharIterator());
        TextAssert.AreEqual("ABC\nDEF", result);
    }

    [Fact]
    public void TestStringLineGroupWithTrim()
    {
        var line1 = new StringSlice("  ABC  ", NewLine.LineFeed);
        line1.NextChar();
        line1.NextChar();

        var line2 = new StringSlice("  DEF ");

        var text = new StringLineGroup(4) { line1, line2}.ToCharIterator();
        text.TrimStart();

        var result = ToString(text);
        TextAssert.AreEqual("ABC  \n  DEF ", result);
    }

    [Fact]
    public void TestStringLineGroupIteratorPeekChar()
    {
        var iterator = new StringLineGroup(4)
        {
            new StringSlice("ABC", NewLine.LineFeed),
            new StringSlice("E", NewLine.LineFeed),
            new StringSlice("F")
        }.ToCharIterator();

        Assert.Equal('A', iterator.CurrentChar);
        Assert.Equal('A', iterator.PeekChar(0));
        Assert.Equal('B', iterator.PeekChar());
        Assert.Equal('B', iterator.PeekChar(1));
        Assert.Equal('C', iterator.PeekChar(2));
        Assert.Equal('\n', iterator.PeekChar(3));
        Assert.Equal('E', iterator.PeekChar(4));
        Assert.Equal('\n', iterator.PeekChar(5));
        Assert.Equal('F', iterator.PeekChar(6));
        Assert.Equal('\0', iterator.PeekChar(7)); // There is no \n appended to the last line
        Assert.Equal('\0', iterator.PeekChar(8));
        Assert.Equal('\0', iterator.PeekChar(100));

        Assert.Throws<ArgumentOutOfRangeException>(() => iterator.PeekChar(-1));
    }

    [Fact]
    public void TestIteratorSkipChar()
    {
        var lineGroup = new StringLineGroup(4)
        {
            new StringSlice("ABC", NewLine.LineFeed),
            new StringSlice("E", NewLine.LineFeed)
        };

        Test(lineGroup.ToCharIterator());

        Test(new StringSlice("ABC\nE\n"));

        Test(new StringSlice("Foo\nABC\nE\n", 4, 9));

        static void Test<T>(T iterator) where T : ICharIterator
        {
            Assert.Equal('A', iterator.CurrentChar); iterator.SkipChar();
            Assert.Equal('B', iterator.CurrentChar); iterator.SkipChar();
            Assert.Equal('C', iterator.CurrentChar); iterator.SkipChar();
            Assert.Equal('\n', iterator.CurrentChar); iterator.SkipChar();
            Assert.Equal('E', iterator.CurrentChar); iterator.SkipChar();
            Assert.Equal('\n', iterator.CurrentChar); iterator.SkipChar();
            Assert.Equal('\0', iterator.CurrentChar); iterator.SkipChar();
            Assert.Equal('\0', iterator.CurrentChar); iterator.SkipChar();
        }
    }

    [Fact]
    public void TestStringLineGroupCharIteratorAtCapacity()
    {
        string str = "ABCDEFGHI";
        var text = new StringLineGroup(1)
        {
            // Will store the following line at capacity
            new StringSlice(str, NewLine.CarriageReturnLineFeed) { Start = 0, End = 2 },
        };

        var iterator = text.ToCharIterator();
        var chars = ToString(iterator);
        TextAssert.AreEqual("ABC\r\n", chars.ToString());
        TextAssert.AreEqual("ABC", text.ToString());
    }

    [Fact]
    public void TestStringLineGroupCharIteratorForcingIncreaseCapacity()
    {
        string str = "ABCDEFGHI";
        var text = new StringLineGroup(1)
        {
            // Will store the following line at capacity
            new StringSlice(str, NewLine.CarriageReturnLineFeed) { Start = 0, End = 2 },

            // Will force increase capacity to 2 and store the line at capacity
            new StringSlice(str, NewLine.CarriageReturnLineFeed) { Start = 3, End = 3 },
        };

        var iterator = text.ToCharIterator();
        var chars = ToString(iterator);
        TextAssert.AreEqual("ABC\r\nD\r\n", chars.ToString());
        TextAssert.AreEqual("ABC\r\nD", text.ToString());
    }

    [Fact]
    public void TestStringLineGroup_EnumeratorReturnsRealLines()
    {
        string str = "A\r\n";
        var text = new StringLineGroup(4)
        {
            new StringSlice(str, NewLine.CarriageReturnLineFeed) { Start = 0, End = 0 }
        };

        var enumerator = ((IEnumerable)text).GetEnumerator();
        Assert.True(enumerator.MoveNext());
        StringLine currentLine = (StringLine)enumerator.Current;
        TextAssert.AreEqual("A", currentLine.ToString());
        Assert.False(enumerator.MoveNext());

        var nonBoxedEnumerator = text.GetEnumerator();

        Assert.True(nonBoxedEnumerator.MoveNext());
        currentLine = (StringLine)nonBoxedEnumerator.Current;
        TextAssert.AreEqual("A", currentLine.ToString());
        Assert.False(nonBoxedEnumerator.MoveNext());
    }
}
