using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public sealed class TestParserAuthoringParityApi
{
    [Fact]
    public void TrackTriviaCanBeConfiguredFromBuilder()
    {
        var pipeline = new MarkdownPipelineBuilder
        {
            TrackTrivia = true
        }.Build();

        Assert.True(pipeline.TrackTrivia);
    }

    [Fact]
    public void CustomBlockParserCanTakeLinesBefore()
    {
        var pipeline = new MarkdownPipelineBuilder
        {
            TrackTrivia = true
        };
        pipeline.BlockParsers.InsertBefore<ParagraphBlockParser>(new LinesBeforeBlockParser());

        var document = MarkdownConverter.Parse("\n\n!marker", pipeline.Build());

        Assert.HasCount(1, document);
        var block = document[0] as LinesBeforeLeafBlock;
        Assert.NotNull(block);
        Assert.NotNull(block!.LinesBefore);
        Assert.HasCount(2, block.LinesBefore);
    }

    [Fact]
    public void InlineParserCanReplaceParentContainerThroughPublicApi()
    {
        var pipeline = new MarkdownPipelineBuilder();
        pipeline.InlineParsers.InsertBefore<LinkInlineParser>(new ParentContainerReplacementInlineParser());

        var document = MarkdownConverter.Parse(
            """
            > [!TEST]
            > body
            """.ReplaceLineEndings("\n"),
            pipeline.Build());

        Assert.HasCount(1, document);
        var replacementBlock = document[0] as ReplacementContainerBlock;
        Assert.NotNull(replacementBlock);

        var paragraph = replacementBlock!.Count > 0 ? replacementBlock[0] as ParagraphBlock : null;
        Assert.NotNull(paragraph);
        var literal = paragraph!.Inline?.FirstChild as LiteralInline;
        Assert.NotNull(literal);
        Assert.Equal("body", literal!.Content.ToString());
    }

    private sealed class LinesBeforeBlockParser : BlockParser
    {
        public LinesBeforeBlockParser()
        {
            OpeningCharacters = ['!'];
        }

        public override BlockState TryOpen(BlockProcessor processor)
        {
            if (processor.CurrentChar != '!')
            {
                return BlockState.None;
            }

            var block = new LinesBeforeLeafBlock(this)
            {
                Line = processor.LineIndex,
                Column = processor.Column,
                Span = new SourceSpan(processor.Start, processor.Line.End)
            };

            if (processor.TrackTrivia)
            {
                block.LinesBefore = processor.TakeLinesBefore();
            }

            processor.NewBlocks.Push(block);
            return BlockState.BreakDiscard;
        }
    }

    private sealed class LinesBeforeLeafBlock : LeafBlock
    {
        public LinesBeforeLeafBlock(BlockParser parser) : base(parser)
        {
            ProcessInlines = false;
        }
    }

    private sealed class ReplacementContainerBlock : ContainerBlock
    {
        public ReplacementContainerBlock() : base(null)
        {
        }
    }

    private sealed class ParentContainerReplacementInlineParser : InlineParser
    {
        private const string Marker = "[!TEST]";

        public ParentContainerReplacementInlineParser()
        {
            OpeningCharacters = ['['];
        }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            if (processor.Block is not ParagraphBlock paragraphBlock
                || paragraphBlock.Parent is not QuoteBlock quoteBlock
                || paragraphBlock.Inline?.FirstChild is not null
                || quoteBlock.Parent is not MarkdownDocument)
            {
                return false;
            }

            if (!slice.Match(Marker))
            {
                return false;
            }

            slice.Start += Marker.Length;

            while (slice.CurrentChar.IsSpaceOrTab())
            {
                slice.NextChar();
            }

            var c = slice.CurrentChar;
            if (c == '\r')
            {
                slice.SkipChar();
                if (slice.CurrentChar == '\n')
                {
                    slice.SkipChar();
                }
            }
            else if (c == '\n')
            {
                slice.SkipChar();
            }
            else if (c != '\0')
            {
                return false;
            }

            var replacementBlock = new ReplacementContainerBlock
            {
                Span = quoteBlock.Span,
                Line = quoteBlock.Line,
                Column = quoteBlock.Column
            };

            var parent = quoteBlock.Parent;
            var quoteBlockIndex = parent.IndexOf(quoteBlock);
            parent[quoteBlockIndex] = replacementBlock;

            while (quoteBlock.Count > 0)
            {
                var child = quoteBlock[0];
                quoteBlock.RemoveAt(0);
                replacementBlock.Add(child);
            }

            processor.ReplaceParentContainer(quoteBlock, replacementBlock);
            return true;
        }
    }

}
