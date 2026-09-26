using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public sealed class TestParserAuthoringHelpers
{
    [Fact]
    public void GetParserStateResetsBetweenLeafBlocksAndEmitAppendsInline()
    {
        var pipeline = new MarkdownPipelineBuilder();
        pipeline.InlineParsers.InsertBefore<AutolinkInlineParser>(new CountingInlineParser());

        var html = MarkdownConverter.ToHtml(
            """
            @@

            @
            """.ReplaceLineEndings("\n"),
            pipeline.Build());

        Assert.Equal("<p>12</p>\n<p>1</p>\n", html);
    }

    [Fact]
    public void TryDiscardOnlyDiscardsOpenNonRootBlocks()
    {
        var parser = new ParagraphBlockParser();
        var document = new MarkdownDocument();
        var processor = new BlockProcessor(document, new BlockParserList([parser]), context: null, trackTrivia: false);

        var detached = new ParagraphBlock(parser);
        Assert.False(processor.TryDiscard(detached));

        var paragraph = new ParagraphBlock(parser);
        document.Add(paragraph);
        processor.Open(paragraph);

        Assert.HasCount(1, document);
        Assert.True(processor.TryDiscard(paragraph));
        Assert.Empty(document);
        Assert.Null(paragraph.Parent);

        Assert.False(processor.TryDiscard(document));
    }

    [Fact]
    public void EmitUpdatesInlineAndLeafSpans()
    {
        var pipeline = new MarkdownPipelineBuilder();
        pipeline.InlineParsers.InsertBefore<AutolinkInlineParser>(new SpanEmittingInlineParser());

        var document = MarkdownConverter.Parse("~", pipeline.Build());
        var paragraph = document[0] as ParagraphBlock;

        Assert.NotNull(paragraph);
        Assert.NotNull(paragraph!.Inline);
        Assert.Equal(new SourceSpan(0, 0), paragraph.Inline!.Span);
        Assert.Equal(new SourceSpan(0, 0), paragraph.Span);
    }

    private sealed class CountingInlineParser : InlineParser
    {
        public CountingInlineParser()
        {
            OpeningCharacters = ['@'];
        }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            if (slice.CurrentChar != '@')
            {
                return false;
            }

            var state = processor.GetParserState<CounterState>(this);
            state.Count++;

            processor.Emit(new LiteralInline(state.Count.ToString()));
            slice.SkipChar();
            return true;
        }
    }

    private sealed class CounterState
    {
        public int Count { get; set; }
    }

    private sealed class SpanEmittingInlineParser : InlineParser
    {
        public SpanEmittingInlineParser()
        {
            OpeningCharacters = ['~'];
        }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            if (slice.CurrentChar != '~')
            {
                return false;
            }

            int start = processor.GetSourcePosition(slice.Start, out int line, out int column);
            processor.Emit(new LiteralInline("x")
            {
                Line = line,
                Column = column,
                Span = new SourceSpan(start, start)
            });

            slice.SkipChar();
            return true;
        }
    }
}
