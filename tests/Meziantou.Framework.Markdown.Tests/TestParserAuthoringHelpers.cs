using System.Text;

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

    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    public void ClosingAContainerFromAnInlineParserMovesFollowingInlinesOutOfIt(int emphasisCount)
    {
        // Many unresolved delimiters make the inline processor track the open containers
        var pipeline = new MarkdownPipelineBuilder();
        pipeline.InlineParsers.InsertBefore<AutolinkInlineParser>(new ClosingInlineParser());

        var document = MarkdownConverter.Parse(string.Concat(Enumerable.Repeat("*x ", emphasisCount)) + "*a [b%c", pipeline.Build());
        var paragraph = (ParagraphBlock)document[0];
        var literals = paragraph.Inline!.FindDescendants<LiteralInline>().ToList();

        Assert.IsType<LinkDelimiterInline>(literals.Single(literal => literal.Content.ToString() == "b").Parent);
        Assert.Same(paragraph.Inline, literals.Single(literal => literal.Content.ToString() == "c").Parent);
    }

    [Fact]
    public void RecoveringFromTheDepthLimitInAnInlineParserKeepsTheLinkDelimiters()
    {
        // The parser removes the container that is too deep. The link delimiters must still be the ones of the tree:
        // the image delimiter that was too deep must not stop the deactivation of the link delimiters around [x](u).
        var pipeline = new MarkdownPipelineBuilder();
        pipeline.InlineParsers.InsertBefore<AutolinkInlineParser>(new RemoveDeepestContainerOnFailureInlineParser());
        var markdown = "[a [b [c [d " + string.Concat(Enumerable.Repeat("*a ", 10236)) + "![%] ] ] [l [x](u) ](v) ](w)";

        var html = MarkdownConverter.ToHtml(markdown, pipeline.Build());

        Assert.Equal("<p>[a [b [c [d " + string.Concat(Enumerable.Repeat("*a ", 10236)) + "] ] ] [l <a href=\"u\">x</a> ](v) ](w)</p>\n", html);
    }

    [Fact]
    public void OpenContainersAreNotTrackedOutsideProcessInlineLeaf()
    {
        var (paragraph, processor) = CreateParagraph(new string('[', 500) + "a", new MarkdownPipelineBuilder().Build());

        processor.ProcessInlineLeaf(paragraph);
        processor.Emit(new LiteralInline("b"));

        Assert.All(GetContainers(paragraph), container => !container.IsInOpenChain);
        Assert.Equal(new string('[', 500) + "ab", GetText(paragraph.Inline!));
    }

    [Fact]
    public void OpenContainersAreNotTrackedAfterAnInlineParserThrows()
    {
        var builder = new MarkdownPipelineBuilder();
        builder.InlineParsers.InsertBefore<AutolinkInlineParser>(new ThrowingInlineParser());
        var (paragraph, processor) = CreateParagraph(new string('[', 500) + "a%b", builder.Build());

        Assert.Throws<InvalidOperationException>(() => processor.ProcessInlineLeaf(paragraph));

        Assert.All(GetContainers(paragraph), container => !container.IsInOpenChain);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    public void InlineParsersCanReplaceTheRootInline(int emphasisCount)
    {
        // The parser moves the root into a link delimiter that becomes the root of the leaf block, so the link delimiter is a
        // parent of the current inline
        var pipeline = new MarkdownPipelineBuilder();
        pipeline.InlineParsers.InsertBefore<AutolinkInlineParser>(new WrapRootInlineParser());
        var prefix = string.Concat(Enumerable.Repeat("*x ", emphasisCount));

        var html = MarkdownConverter.ToHtml(prefix + "a *b%](u) c", pipeline.Build());

        Assert.Equal("<p>[<a href=\"u\">" + prefix + "a *b</a> c</p>\n", html);
    }

    private static (ParagraphBlock Paragraph, InlineProcessor Processor) CreateParagraph(string markdown, MarkdownPipeline pipeline)
    {
        var document = new MarkdownDocument();
        var paragraph = new ParagraphBlock();
        var text = new StringSlice(markdown);
        paragraph.AppendLine(ref text, column: 0, line: 0, sourceLinePosition: 0, trackTrivia: false);
        document.Add(paragraph);
        return (paragraph, new InlineProcessor(document, pipeline.InlineParsers, preciseSourcelocation: false, context: null));
    }

    private static ContainerInline[] GetContainers(ParagraphBlock paragraph)
    {
        return [paragraph.Inline!, .. paragraph.Inline!.FindDescendants<ContainerInline>()];
    }

    private static string GetText(ContainerInline container)
    {
        var builder = new StringBuilder();
        var stack = new Stack<Inline>();
        stack.Push(container);
        while (stack.TryPop(out var inline))
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;
                case DelimiterInline delimiter:
                    builder.Append(delimiter.ToLiteral());
                    break;
            }

            if (inline is ContainerInline children)
            {
                for (var child = children.LastChild; child is not null; child = child.PreviousSibling)
                {
                    stack.Push(child);
                }
            }
        }

        return builder.ToString();
    }

    private sealed class RemoveDeepestContainerOnFailureInlineParser : InlineParser
    {
        public RemoveDeepestContainerOnFailureInlineParser()
        {
            OpeningCharacters = ['%'];
        }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            try
            {
                processor.Emit(new LiteralInline("%"));
            }
            catch (ArgumentException)
            {
                ContainerInline container = processor.Root!;
                while (container.LastChild is ContainerInline { IsClosed: false } child)
                {
                    container = child;
                }

                container.Remove();
                processor.Inline = null;
            }

            slice.SkipChar();
            return true;
        }
    }

    private sealed class ThrowingInlineParser : InlineParser
    {
        public ThrowingInlineParser()
        {
            OpeningCharacters = ['%'];
        }

        public override bool Match(InlineProcessor processor, ref StringSlice slice) => throw new InvalidOperationException("The parser failed");
    }

    private sealed class WrapRootInlineParser : InlineParser
    {
        public WrapRootInlineParser()
        {
            OpeningCharacters = ['%'];
        }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            var root = processor.Block!.Inline!;
            ContainerInline container = root;
            while (container.LastChild is ContainerInline { IsClosed: false } child)
            {
                container = child;
            }

            var wrapper = new LinkDelimiterInline(this) { Type = DelimiterType.Open };
            processor.Block.Inline = wrapper;
            wrapper.AppendChild(root);
            processor.Inline = container.LastChild ?? container;
            slice.SkipChar();
            return true;
        }
    }

    private sealed class ClosingInlineParser : InlineParser
    {
        public ClosingInlineParser()
        {
            OpeningCharacters = ['%'];
        }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            processor.Inline!.Parent!.IsClosed = true;
            processor.Inline = null;
            slice.SkipChar();
            return true;
        }
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
