// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

[TestFixture]
public partial class TestEmphasisPlus
{
    [Test]
    public void StrongNormal()
    {
        TestParser.TestSpec("***Strong emphasis*** normal", "<p><em><strong>Strong emphasis</strong></em> normal</p>", "");
    }

    [Test]
    public void NormalStrongNormal()
    {
        TestParser.TestSpec("normal ***Strong emphasis*** normal", "<p>normal <em><strong>Strong emphasis</strong></em> normal</p>", "");
    }

    [Test]
    public void SupplementaryPunctuation()
    {
        TestParser.TestSpec("a*a∇*a\n\na*∇a*a\n\na*a𝜵*a\n\na*𝜵a*a\n\na*𐬼a*a\n\na*a𐬼*a", "<p>a*a∇*a</p>\n<p>a*∇a*a</p>\n<p>a*a𝜵*a</p>\n<p>a*𝜵a*a</p>\n<p>a*𐬼a*a</p>\n<p>a*a𐬼*a</p>", "");
    }

    [Test]
    public void RecognizeSupplementaryChars()
    {
        TestParser.TestSpec("🌶️**𰻞**🍜**𰻞**🌶️**麺**🍜", "<p>🌶️<strong>𰻞</strong>🍜<strong>𰻞</strong>🌶️<strong>麺</strong>🍜</p>", "");
    }

    [Test]
    public void OpenEmphasisHasConvenientContentStringSlice()
    {
        var pipeline = new MarkdownPipelineBuilder().Build();

        var document = MarkdownConverter.Parse("test*test", pipeline);

        var emphasisDelimiterLiteral = (LiteralInline)((ParagraphBlock)document.LastChild).Inline.ElementAt(1);
        Assert.That(emphasisDelimiterLiteral.Content.Text == "test*test");
        Assert.That(emphasisDelimiterLiteral.Content.Start == 4);
        Assert.That(emphasisDelimiterLiteral.Content.End == 4);
    }
}