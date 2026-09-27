// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Tests;

public class TestPragmaLines
{
    [Fact]
    public void TestFindClosest()
    {
        var doc = MarkdownConverter.Parse(
"test1\n" +                      // 0
"\n" +                           // 1
"test2\n" +                      // 2
"\n" +                           // 3
"test3\n" +                      // 4
"\n" +                           // 5
"test4\n" +                      // 6
"\n" +                           // 7
"# Heading\n" +                  // 8
"\n" +                           // 9
"Long para\n" +                  // 10
"on multiple\n" +                // 11
"lines\n" +                      // 12
"to check that\n" +              // 13
"lines are\n" +                  // 14
"correctly \n" +                 // 15
"found\n" +                      // 16
"\n" +                           // 17
"- item1\n" +                    // 18
"- item2\n" +                    // 19
"- item3\n" +                    // 20
"\n" +                           // 21
"This is a last paragraph\n"     // 22
            , new MarkdownPipelineBuilder().UsePragmaLines().Build());

        foreach (var exact in new int[] {0, 2, 4, 6, 8, 10, 18, 19, 20, 22})
        {
            Assert.Equal(exact, doc.FindClosestLine(exact));
        }

        Assert.Equal(22, doc.FindClosestLine(23));

        Assert.Equal(10, doc.FindClosestLine(11));
        Assert.Equal(10, doc.FindClosestLine(12));
        Assert.Equal(10, doc.FindClosestLine(13));
        Assert.Equal(18, doc.FindClosestLine(14)); // > 50% of the paragraph, we switch to next
        Assert.Equal(18, doc.FindClosestLine(15));
        Assert.Equal(18, doc.FindClosestLine(16));
    }

    [Fact]
    public void TestFindClosest1()
    {
        var text =
"- item1\n" +                    // 0
"  - item11\n" +                 // 1
"  - item12\n" +                 // 2
"    - item121\n" +              // 3
"  - item13\n" +                 // 4
"    - item131\n" +              // 5
"      - item1311\n";            // 6

        var pipeline = new MarkdownPipelineBuilder().UsePragmaLines().Build();
        var doc = MarkdownConverter.Parse(text, pipeline);

        for (int exact = 0; exact < 7; exact++)
        {
            Assert.Equal(exact, doc.FindClosestLine(exact));
        }

        Assert.Equal(6, doc.FindClosestLine(50));
    }

    [Theory]
    [InlineData("# Title {#custom}\n\ntext", "<h1 id=\"custom\"><a id=\"pragma-line-0\"></a>Title</h1>\n<p id=\"pragma-line-2\">text</p>\n")]
    [InlineData("text\n\n# {#custom}\n\n---", "<p id=\"pragma-line-0\">text</p>\n<a id=\"pragma-line-2\"></a>\n<h1 id=\"custom\"></h1>\n<hr id=\"pragma-line-4\" />\n")]
    public void BlockWithAnIdGetsAnAnchor(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder().UsePragmaLines().UseGenericAttributes().Build();

        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, pipeline));
    }
}
