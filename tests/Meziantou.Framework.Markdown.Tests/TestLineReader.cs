// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.Framework.Markdown.Tests;

/// <summary>
/// Test for <see cref="LineReader"/>.
/// </summary>

public class TestLineReader
{
    [Fact]
    public void TestEmpty()
    {
        var lineReader = new LineReader("");
        Assert.Null(lineReader.ReadLine().Text);
    }

    [Fact]
    public void TestLinesOnlyLf()
    {
        var lineReader = new LineReader("\n\n\n");
        Assert.Equal(string.Empty, lineReader.ReadLine().ToString());
        Assert.Equal(1, lineReader.SourcePosition);
        Assert.Equal(string.Empty, lineReader.ReadLine().ToString());
        Assert.Equal(2, lineReader.SourcePosition);
        Assert.Equal(string.Empty, lineReader.ReadLine().ToString());
        Assert.Null(lineReader.ReadLine().Text);
    }

    [Fact]
    public void TestLinesOnlyCr()
    {
        var lineReader = new LineReader("\r\r\r");
        Assert.Equal(string.Empty, lineReader.ReadLine().ToString());
        Assert.Equal(1, lineReader.SourcePosition);
        Assert.Equal(string.Empty, lineReader.ReadLine().ToString());
        Assert.Equal(2, lineReader.SourcePosition);
        Assert.Equal(string.Empty, lineReader.ReadLine().ToString());
        Assert.Null(lineReader.ReadLine().Text);
    }

    [Fact]
    public void TestLinesOnlyCrLf()
    {
        var lineReader = new LineReader("\r\n\r\n\r\n");
        Assert.Equal(string.Empty, lineReader.ReadLine().ToString());
        Assert.Equal(2, lineReader.SourcePosition);
        Assert.Equal(string.Empty, lineReader.ReadLine().ToString());
        Assert.Equal(4, lineReader.SourcePosition);
        Assert.Equal(string.Empty, lineReader.ReadLine().ToString());
        Assert.Null(lineReader.ReadLine().Text);
    }

    [Fact]
    public void TestNoEndOfLine()
    {
        var lineReader = new LineReader("123");
        Assert.Equal("123", lineReader.ReadLine().ToString());
        Assert.Null(lineReader.ReadLine().Text);
    }

    [Fact]
    public void TestLf()
    {
        var lineReader = new LineReader("123\n");
        Assert.Equal("123", lineReader.ReadLine().ToString());
        Assert.Equal(4, lineReader.SourcePosition);
        Assert.Null(lineReader.ReadLine().Text);
    }

    [Fact]
    public void TestLf2()
    {
        // When limited == true, we limit the internal buffer exactly after the first new line char '\n'
        var lineReader = new LineReader("123\n456");
        Assert.Equal("123", lineReader.ReadLine().ToString());
        Assert.Equal(4, lineReader.SourcePosition);
        Assert.Equal("456", lineReader.ReadLine().ToString());
        Assert.Null(lineReader.ReadLine().Text);
    }

    [Fact]
    public void TestCr()
    {
        var lineReader = new LineReader("123\r");
        Assert.Equal("123", lineReader.ReadLine().ToString());
        Assert.Equal(4, lineReader.SourcePosition);
        Assert.Null(lineReader.ReadLine().Text);
    }

    [Fact]
    public void TestCr2()
    {
        var lineReader = new LineReader("123\r456");
        Assert.Equal("123", lineReader.ReadLine().ToString());
        Assert.Equal(4, lineReader.SourcePosition);
        Assert.Equal("456", lineReader.ReadLine().ToString());
        Assert.Null(lineReader.ReadLine().Text);
    }

    [Fact]
    public void TestCrLf()
    {
        // When limited == true, we limit the internal buffer exactly after the first new line char '\r'
        // and we check that we don't get a new line for `\n`
        var lineReader = new LineReader("123\r\n");
        Assert.Equal("123", lineReader.ReadLine().ToString());
        Assert.Equal(5, lineReader.SourcePosition);
        Assert.Null(lineReader.ReadLine().Text);
    }

    [Fact]
    public void TestCrLf2()
    {
        var lineReader = new LineReader("123\r\n456");
        Assert.Equal("123", lineReader.ReadLine().ToString());
        Assert.Equal(5, lineReader.SourcePosition);
        Assert.Equal("456", lineReader.ReadLine().ToString());
        Assert.Null(lineReader.ReadLine().Text);
    }
}