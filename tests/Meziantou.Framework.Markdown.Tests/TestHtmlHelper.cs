// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.
using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.Framework.Markdown.Tests;

public class TestHtmlHelper
{
    [Fact]
    public void TestParseHtmlTagSimple()
    {
        var inputTag = "<a>";
        var text = new StringSlice(inputTag);
        Assert.True(HtmlHelper.TryParseHtmlTag(ref text, out string? outputTag));
        Assert.Equal(inputTag, outputTag);
    }

    [Fact]
    public void TestParseHtmlTagSimpleWithAttribute()
    {
        var inputTag = "<a href='http://google.com'>";
        var text = new StringSlice(inputTag);
        Assert.True(HtmlHelper.TryParseHtmlTag(ref text, out string? outputTag));
        Assert.Equal(inputTag, outputTag);
    }

    [Theory]
    [InlineData("\\\\&#9;_", "\\\t_")]
    [InlineData("\\\\\\*", "\\*")]
    [InlineData("\\*\0", "*\0")]
    public void UnescapeKeepsEscapedCharacters(string text, string expected)
    {
        // An escaped backslash escaped the next character, and a null character in the text disabled the unescaping
        Assert.Equal(expected, HtmlHelper.Unescape(text));
    }
}
