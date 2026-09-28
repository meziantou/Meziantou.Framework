// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public partial class TestEmphasisPlus
{
    [Fact]
    public void StrongNormal()
    {
        TestParser.TestSpec("***Strong emphasis*** normal", "<p><em><strong>Strong emphasis</strong></em> normal</p>", "");
    }

    [Fact]
    public void NormalStrongNormal()
    {
        TestParser.TestSpec("normal ***Strong emphasis*** normal", "<p>normal <em><strong>Strong emphasis</strong></em> normal</p>", "");
    }

    [Fact]
    public void SupplementaryPunctuation()
    {
        TestParser.TestSpec("a*a∇*a\n\na*∇a*a\n\na*a𝜵*a\n\na*𝜵a*a\n\na*𐬼a*a\n\na*a𐬼*a", "<p>a*a∇*a</p>\n<p>a*∇a*a</p>\n<p>a*a𝜵*a</p>\n<p>a*𝜵a*a</p>\n<p>a*𐬼a*a</p>\n<p>a*a𐬼*a</p>", "");
    }

    [Fact]
    public void RecognizeSupplementaryChars()
    {
        TestParser.TestSpec("🌶️**𰻞**🍜**𰻞**🌶️**麺**🍜", "<p>🌶️<strong>𰻞</strong>🍜<strong>𰻞</strong>🌶️<strong>麺</strong>🍜</p>", "");
    }

    [Fact]
    public void OpenEmphasisHasConvenientContentStringSlice()
    {
        var pipeline = new MarkdownPipelineBuilder().Build();

        var document = MarkdownConverter.Parse("test*test", pipeline);

        var emphasisDelimiterLiteral = (LiteralInline)((ParagraphBlock)document.LastChild!).Inline!.ElementAt(1);
        Assert.Equal("test*test", emphasisDelimiterLiteral.Content.Text);
        Assert.Equal(4, emphasisDelimiterLiteral.Content.Start);
        Assert.Equal(4, emphasisDelimiterLiteral.Content.End);
    }
}