// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Extensions.GenericAttributes;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Extensions.AutoIdentifiers;

/// <summary>
/// The auto-identifier extension
/// </summary>
/// <seealso cref="IMarkdownExtension" />
public class AutoIdentifierExtension : IMarkdownExtension
{
    private const string AutoIdentifierKey = "AutoIdentifier";

    private static readonly StripRendererCache RendererCache = new();

    private readonly AutoIdentifierOptions _options;
    private readonly ProcessInlineDelegate _processInlinesBegin;
    private readonly ProcessInlineDelegate _processInlinesEnd;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoIdentifierExtension"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public AutoIdentifierExtension(AutoIdentifierOptions options)
    {
        _options = options;
        _processInlinesBegin = DocumentOnProcessInlinesBegin;
        _processInlinesEnd = HeadingBlock_ProcessInlinesEnd;
    }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        var headingBlockParser = pipeline.BlockParsers.Find<HeadingBlockParser>();
        if (headingBlockParser != null)
        {
            // Install a hook on the HeadingBlockParser when a HeadingBlock is actually processed
            headingBlockParser.Closed -= HeadingBlockParser_Closed;
            headingBlockParser.Closed += HeadingBlockParser_Closed;
        }
        var paragraphBlockParser = pipeline.BlockParsers.FindExact<ParagraphBlockParser>();
        if (paragraphBlockParser != null)
        {
            // Install a hook on the ParagraphBlockParser when a HeadingBlock is actually processed as a Setex heading
            paragraphBlockParser.Closed -= HeadingBlockParser_Closed;
            paragraphBlockParser.Closed += HeadingBlockParser_Closed;
        }
    }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }

    /// <summary>
    /// Process on a new <see cref="HeadingBlock"/>
    /// </summary>
    /// <param name="processor">The processor.</param>
    /// <param name="block">The heading block.</param>
    private void HeadingBlockParser_Closed(BlockProcessor processor, Block block)
    {
        // We may have a ParagraphBlock here as we have a hook on the ParagraphBlockParser
        if (!(block is HeadingBlock headingBlock))
        {
            return;
        }

        // If the AutoLink options is set, we register a LinkReferenceDefinition at the document level
        if (_options.HasFlag(AutoIdentifierOptions.AutoLink))
        {
            var linkRef = new HeadingLinkReferenceDefinition(headingBlock)
            {
                CreateLinkInline = CreateLinkInlineForHeading
            };

            // The label is computed when the inlines are processed, as it depends on the inline parsers of the pipeline
            var doc = processor.Document;
            var headings = doc.GetData(this) as List<HeadingLinkReferenceDefinition>;
            if (headings is null)
            {
                headings = [];
                doc.SetData(this, headings);
                doc.ProcessInlinesBegin += _processInlinesBegin;
            }
            headings.Add(linkRef);
        }

        // Then we register after inline have been processed to actually generate the proper #id
        headingBlock.ProcessInlinesEnd += _processInlinesEnd;
    }

    private void DocumentOnProcessInlinesBegin(InlineProcessor processor, Inline? inline)
    {
        var doc = processor.Document;
        var headings = (List<HeadingLinkReferenceDefinition>)doc.GetData(this)!;
        var hasGenericAttributes = processor.Parsers.Find<GenericAttributesParser>() is not null;

        // When several headings have the same label, the last one wins
        var dictionary = new Dictionary<string, HeadingLinkReferenceDefinition>();
        foreach (var heading in headings)
        {
            var headingLine = heading.Heading.Lines.Lines[0].Slice;
            var label = hasGenericAttributes ? GetLabelWithoutAttributes(headingLine) : headingLine.ToString();
            dictionary[label] = heading;
        }

        foreach (var keyPair in dictionary)
        {
            // Here we make sure that auto-identifiers will not override an existing link definition
            // defined in the document
            // If it is the case, we skip the auto identifier for the Heading
            if (!doc.TryGetLinkReferenceDefinition(keyPair.Key, out _))
            {
                doc.SetLinkReferenceDefinition(keyPair.Key, keyPair.Value, false);
            }
        }
        // Once we are done, we don't need to keep the intermediate list around
        doc.RemoveData(this);
    }

    /// <summary>
    /// Gets the text of a heading line without the generic attributes that the inline parser reads, as they are not part of
    /// the text of the heading.
    /// </summary>
    private static string GetLabelWithoutAttributes(StringSlice line)
    {
        var text = line.Text;
        if (line.IndexOf('{') < 0)
        {
            return line.ToString();
        }

        var label = new ValueStringBuilder(unsafe(stackalloc char[ValueStringBuilder.StackallocThreshold]));
        var copyStart = line.Start;
        var endsWithAttributes = false;
        var index = line.Start;
        while (index <= line.End)
        {
            var c = text[index];
            if (c == '\\' && index < line.End && text[index + 1].IsAsciiPunctuation())
            {
                // An escaped character is never the start of attributes
                index += 2;
                endsWithAttributes = false;
            }
            else if (c == '`')
            {
                index = SkipCodeSpan(text, index, line.End);
                endsWithAttributes = false;
            }
            else if (c == '{')
            {
                var attributes = new StringSlice(text, index, line.End);
                if (GenericAttributesParser.TryParse(ref attributes, out _))
                {
                    label.Append(text.AsSpan(copyStart, index - copyStart));
                    var isAfterWhitespace = index == line.Start || text[index - 1].IsWhitespace();
                    index = attributes.Start;

                    // Keep a single whitespace between the text around the attributes
                    if (isAfterWhitespace)
                    {
                        while (index <= line.End && text[index].IsWhitespace())
                        {
                            index++;
                        }
                    }

                    copyStart = index;
                    endsWithAttributes = true;
                }
                else
                {
                    index++;
                    endsWithAttributes = false;
                }
            }
            else
            {
                index++;
                if (!c.IsWhitespace())
                {
                    endsWithAttributes = false;
                }
            }
        }

        label.Append(text.AsSpan(copyStart, line.End + 1 - copyStart));

        // The whitespace before the attributes that end the line is not part of the text
        var result = label.AsSpan();
        if (endsWithAttributes)
        {
            result = result.TrimEnd();
        }

        var labelText = result.ToString();
        label.Dispose();
        return labelText;
    }

    /// <summary>
    /// Skips a code span starting at the specified backtick, or only its opening backticks when it is not closed.
    /// </summary>
    private static int SkipCodeSpan(string text, int start, int end)
    {
        var index = start;
        while (index <= end && text[index] == '`')
        {
            index++;
        }

        var openingLength = index - start;
        while (index <= end)
        {
            if (text[index] != '`')
            {
                index++;
                continue;
            }

            var closingStart = index;
            while (index <= end && text[index] == '`')
            {
                index++;
            }

            if (index - closingStart == openingLength)
            {
                return index;
            }
        }

        return start + openingLength;
    }

    /// <summary>
    /// Callback when there is a reference to found to a heading.
    /// Note that reference are only working if they are declared after.
    /// </summary>
    private static LinkInline CreateLinkInlineForHeading(InlineProcessor inlineState, LinkReferenceDefinition linkRef, Inline? child)
    {
        var headingRef = (HeadingLinkReferenceDefinition) linkRef;
        return new LinkInline()
        {
            // Use GetDynamicUrl to allow late binding of the Url (as a link may occur before the heading is declared and
            // the inlines of the heading are actually processed by HeadingBlock_ProcessInlinesEnd)
            GetDynamicUrl = () => HtmlHelper.Unescape("#" + headingRef.Heading.GetAttributes().Id),
            Title = HtmlHelper.Unescape(linkRef.Title),
        };
    }

    /// <summary>
    /// Process the inlines of the heading to create a unique identifier
    /// </summary>
    /// <param name="processor">The processor.</param>
    /// <param name="inline">The inline.</param>
    private void HeadingBlock_ProcessInlinesEnd(InlineProcessor processor, Inline? inline)
    {
        var identifiers = processor.Document.GetData(AutoIdentifierKey) as HeadingIdentifiers;
        if (identifiers is null)
        {
            identifiers = new HeadingIdentifiers();
            processor.Document.SetData(AutoIdentifierKey, identifiers);
        }

        var headingBlock = (HeadingBlock) processor.Block!;
        if (headingBlock.Inline is null)
        {
            return;
        }

        // If id is already set, don't try to modify it
        var attributes = processor.Block!.GetAttributes();
        if (attributes.Id != null)
        {
            return;
        }

        // Use internally a HtmlRenderer to strip links from a heading
        var stripRenderer = RendererCache.Get();

        stripRenderer.Render(headingBlock.Inline);
        ReadOnlySpan<char> rawHeadingText = ((FastStringWriter)stripRenderer.Writer).AsSpan();

        // Urilize the link
        string headingText = _options.HasFlag(AutoIdentifierOptions.GitHub)
            ? LinkHelper.UrilizeAsGfm(rawHeadingText)
            : LinkHelper.Urilize(rawHeadingText, _options.HasFlag(AutoIdentifierOptions.AllowOnlyAscii));

        RendererCache.Release(stripRenderer);

        // If the heading is empty, use the word "section" instead
        var baseHeadingId = string.IsNullOrEmpty(headingText) ? "section" : headingText;

        // Add a trailing -1, -2, -3...etc. in case of collision
        var headingId = baseHeadingId;
        if (!identifiers.Ids.Add(headingId))
        {
            // Identifiers are never removed, so the suffixes below the last one used for this base are all taken: start
            // after it instead of trying them all again, which is quadratic when many headings have the same text
            identifiers.LastSuffixes.TryGetValue(baseHeadingId, out uint index);

            var headingBuffer = new ValueStringBuilder(unsafe(stackalloc char[ValueStringBuilder.StackallocThreshold]));
            headingBuffer.Append(baseHeadingId);
            headingBuffer.Append('-');
            do
            {
                index++;
                headingBuffer.AppendSpanFormattable(index, provider: CultureInfo.InvariantCulture);
                headingId = headingBuffer.AsSpan().ToString();
                headingBuffer.Length = baseHeadingId.Length + 1;
            }
            while (!identifiers.Ids.Add(headingId));
            headingBuffer.Dispose();

            identifiers.LastSuffixes[baseHeadingId] = index;
        }

        attributes.Id = headingId;
    }

    private sealed class HeadingIdentifiers
    {
        public HashSet<string> Ids { get; } = [];

        // Last numeric suffix used for each base identifier
        public Dictionary<string, uint> LastSuffixes { get; } = [];
    }

    private sealed class StripRendererCache : ObjectCache<HtmlRenderer>
    {
        protected override HtmlRenderer NewInstance()
        {
            var headingWriter = new FastStringWriter();
            var stripRenderer = new HtmlRenderer(headingWriter)
            {
                // Set to false both to avoid having any HTML tags in the output
                EnableHtmlForInline = false,
                EnableHtmlEscape = false
            };
            return stripRenderer;
        }

        protected override void Reset(HtmlRenderer instance)
        {
            instance.ResetInternal();

            ((FastStringWriter)instance.Writer).Reset();
        }
    }
}
