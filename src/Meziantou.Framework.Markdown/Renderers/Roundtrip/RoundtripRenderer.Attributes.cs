using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Meziantou.Framework.Markdown.Extensions.GenericAttributes;
using Meziantou.Framework.Markdown.Extensions.Tables;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Renderers.Roundtrip;

// With the generic attributes, the attributes of the blocks and the inlines that were changed after the parsing are written back.
// They replace the first attributes of the text attached to the object, and the others are written as empty attributes, so that
// the lines stay the same. An object without attributes in the text gets them where the parser reads them back: after the text of
// a paragraph or a heading, on the opening line of a fenced block, on a line before another block, or after an inline.
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The recorder only wraps the writer while a document is written, and does not own it")]
public partial class RoundtripRenderer
{
    private Dictionary<MarkdownObject, ChangedAttributes>? _changedAttributes;
    private RecordingTextWriter? _attributesRecorder;

    // The text of the current leaf block, without the indent of the containers: the start and the end of each line in the recorded
    // text. The last line has no end yet.
    private List<int>? _leafLines;

    /// <summary>
    /// Gets the changed attributes of an object, or <see langword="null"/> when they were not changed.
    /// </summary>
    internal ChangedAttributes? GetChangedAttributes(MarkdownObject obj) => _changedAttributes?.GetValueOrDefault(obj);

    private void BeginAttributes(MarkdownDocument document)
    {
        if (!AttributesTrivia.IsTracked(document))
        {
            return;
        }

        Dictionary<MarkdownObject, ChangedAttributes>? changes = null;
        foreach (var obj in document.Descendants())
        {
            if (obj is GenericAttributesInline || !AttributesTrivia.IsChanged(obj))
            {
                continue;
            }

            var text = AttributesTrivia.Format(obj);
            var trivia = AttributesTrivia.Get(obj);
            if (text is null && trivia?.HasSourceText is not true)
            {
                continue;
            }

            changes ??= new(ReferenceEqualityComparer.Instance);
            changes.Add(obj, new ChangedAttributes(obj, trivia, text));
        }

        if (changes is null)
        {
            return;
        }

        // The checks read the text written before the attributes
        _changedAttributes = changes;
        _attributesRecorder = new RecordingTextWriter(Writer);
        ReplaceWriter(_attributesRecorder);
        IndentCallbacks = (OnIndentBefore, OnIndentAfter);
    }

    // Returns an object whose changed attributes were not written
    private MarkdownObject? EndAttributes()
    {
        if (_attributesRecorder is null)
        {
            return null;
        }

        if (Writer == _attributesRecorder)
        {
            ReplaceWriter(_attributesRecorder.InnerWriter);
        }

        var changes = _changedAttributes!;
        _changedAttributes = null;
        _attributesRecorder = null;
        _leafLines = null;
        IndentCallbacks = null;
        foreach (var change in changes.Values)
        {
            if (!change.IsWritten)
            {
                return change.Object;
            }
        }

        return null;
    }

    private void OnAttributesObjectWriteBefore(MarkdownObject obj)
    {
        if (_attributesRecorder is null)
        {
            return;
        }

        if (obj is LeafBlock)
        {
            _leafLines = null;
        }
        else if (obj is Inline inline)
        {
            if (inline is { PreviousSibling: null, Parent: { Parent: null, ParentBlock: not null } })
            {
                // The first inline of a leaf block: its first line starts here, or after the indent of the containers
                _leafLines = [];
                if (!PreviousWasLine)
                {
                    _leafLines.Add(_attributesRecorder.Length);
                }
            }

            if (GetChangedAttributes(inline) is { NeedsPlace: true } changedAttributes)
            {
                changedAttributes.TextBefore = GetLeafText();
            }
        }
    }

    private void OnAttributesObjectWriteAfter(MarkdownObject obj)
    {
        if (obj is Inline inline && GetChangedAttributes(inline) is { NeedsPlace: true } changedAttributes)
        {
            WriteAttributesAfterInline(inline, changedAttributes);
        }
    }

    private void OnIndentBefore()
    {
        if (_leafLines is { Count: > 0 } lines && lines.Count % 2 == 1)
        {
            lines.Add(_attributesRecorder!.Length);
        }
    }

    private void OnIndentAfter()
    {
        _leafLines?.Add(_attributesRecorder!.Length);
    }

    /// <summary>
    /// Gets the text written so far for the current leaf block, without the indent of the containers.
    /// </summary>
    internal string GetLeafText()
    {
        if (_leafLines is not { Count: > 0 } lines || _attributesRecorder is null)
        {
            return "";
        }

        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i += 2)
        {
            var end = i + 1 < lines.Count ? lines[i + 1] : _attributesRecorder.Length;
            _attributesRecorder.AppendTo(sb, lines[i], end);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Writes the changed attributes of a paragraph or a heading after its text, when the text has no attributes to replace.
    /// </summary>
    /// <param name="leaf">The paragraph or the heading.</param>
    /// <param name="changedAttributes">The changed attributes.</param>
    /// <param name="isAtx">Whether the leaf is an ATX heading, whose closing sequence follows.</param>
    /// <param name="closingSequence">The closing sequence of an ATX heading.</param>
    internal void WriteAttributesAfterText(LeafBlock leaf, ChangedAttributes changedAttributes, bool isAtx, StringSlice closingSequence)
    {
        var attributes = changedAttributes.Text!;
        var text = GetLeafText();

        // The parser reads the attributes of an ATX heading from the first '{' of its line, then finds the closing sequence before
        // them. When the content has a '{', it reads them as inline attributes instead, and finds the closing sequence at the end.
        if (isAtx && !text.Contains('{', StringComparison.Ordinal))
        {
            AttributesTrivia.CheckAttributesAfterText(leaf, text + closingSequence.ToString() + " ", attributes, readsFirstBrace: true, checkBlocks: false);
            Write(closingSequence);
            Write(' ');
            Write(attributes);
        }
        else
        {
            AttributesTrivia.CheckAttributesAfterText(leaf, text + " ", attributes, readsFirstBrace: isAtx, checkBlocks: !isAtx);
            Write(' ');
            Write(attributes);
            Write(closingSequence);
        }

        changedAttributes.IsWritten = true;
    }

    /// <summary>
    /// Writes the trivia after the arguments of the opening line of a fenced block, with the changed attributes of the block.
    /// </summary>
    internal void WriteTriviaAfterArguments(Block block, IFencedBlock fenced)
    {
        var trivia = fenced.TriviaAfterArguments.Text is null ? StringSlice.Empty : fenced.TriviaAfterArguments;
        if (GetChangedAttributes(block) is not { } changedAttributes)
        {
            if (!trivia.IsEmpty)
            {
                Write(trivia);
            }

            return;
        }

        var source = changedAttributes.Trivia?.TriviaAfterAttributes ?? StringSlice.Empty;
        if (!source.IsEmpty && ReferenceEquals(source.Text, trivia.Text) && source.Start >= trivia.Start && source.End <= trivia.End)
        {
            Write(new StringSlice(trivia.Text, trivia.Start, source.Start - 1));
            Write(AttributesTrivia.ReplaceAttributes(block, changedAttributes.Take(), source.AsSpan()));
            Write(new StringSlice(trivia.Text, source.End + 1, trivia.End));
            return;
        }

        if (!changedAttributes.NeedsPlace)
        {
            if (!trivia.IsEmpty)
            {
                Write(trivia);
            }

            return;
        }

        // The parser reads them from the first '{' of the line, and the info string of a fence of backticks has none. The
        // extensions added after the generic attributes have parsers that do not read them.
        var attributes = changedAttributes.Text!;
        if (block.Parser is not IAttributesParseable { TryParseAttributes: not null })
        {
            AttributesTrivia.ThrowCannotWrite(block, "the parser of the block does not read attributes, as the generic attributes were enabled before its extension");
        }

        if (fenced.UnescapedInfo.AsSpan().Contains('{') || fenced.UnescapedArguments.AsSpan().Contains('{'))
        {
            AttributesTrivia.ThrowCannotWrite(block, "the info string of the fenced block has a '{'");
        }

        if (fenced.FencedChar == '`' && attributes.Contains('`', StringComparison.Ordinal))
        {
            AttributesTrivia.ThrowCannotWrite(block, "the opening line of a fenced block of backticks cannot have a backtick");
        }

        var textEnd = trivia.End;
        while (textEnd >= trivia.Start && trivia.Text[textEnd].IsSpaceOrTab())
        {
            textEnd--;
        }

        Write(new StringSlice(trivia.Text, trivia.Start, textEnd));
        Write(' ');
        Write(attributes);
        Write(new StringSlice(trivia.Text, textEnd + 1, trivia.End));
        changedAttributes.IsWritten = true;
    }

    private static readonly string[] LinkPrefixes = ["://", "www.", "mailto:", "tel:"];

    // The blocks whose attributes can only be written on a line before them. The parser of an alert replaces the attributes of its
    // quote.
    private static bool IsWrittenOnLineBefore(Block block) => block is QuoteBlock and not Extensions.Alerts.AlertBlock or ListBlock or ThematicBreakBlock or HtmlBlock;

    // Writes the changed attributes of a block on a line before it. The line is a paragraph that only has attributes: it needs a
    // blank line before it, so that it does not continue a paragraph, and the block must be one that interrupts a paragraph.
    private void WriteLineOfAttributesBefore(Block block, ChangedAttributes changedAttributes)
    {
        if (block.Parent is not MarkdownDocument document)
        {
            AttributesTrivia.ThrowCannotWrite(block, "a line of attributes can only be added before a block of the document, not before a block of a container");
            return;
        }

        var index = document.IndexOf(block);
        if (index > 0 && block.LinesBefore is not { Count: > 0 } && document[index - 1].LinesAfter is not { Count: > 0 })
        {
            AttributesTrivia.ThrowCannotWrite(block, "a line of attributes needs a blank line before it");
        }

        var interruptsParagraph = block switch
        {
            ListBlock list => list is [ListItemBlock { Count: > 0 } item, ..] && item[0].Line == item.Line && (!list.IsOrdered || list is { BulletType: '1', OrderedStart: "1" }),
            ThematicBreakBlock thematicBreak => thematicBreak.ThematicChar != '-',
            HtmlBlock html => html.Type != HtmlBlockType.NonInterruptingBlock,
            _ => true,
        };
        if (!interruptsParagraph)
        {
            AttributesTrivia.ThrowCannotWrite(block, "the block does not interrupt the paragraph of a line of attributes");
        }

        // The first line of a list of dashes can be the delimiter row of a table whose header row is the line of attributes
        if (block is ListBlock { BulletType: '-' } && CanBeDelimiterRow(block))
        {
            AttributesTrivia.ThrowCannotWrite(block, "the first line of the list could be the delimiter row of a table");
        }

        Write(changedAttributes.Text);
        WriteLine(block.LinesBefore is [.., var lastLine] ? lastLine.NewLine : NewLine.LineFeed);
        changedAttributes.IsWritten = true;
    }

    // Whether the content of the first line of a list of dashes only has the characters of the delimiter row of a table
    private static bool CanBeDelimiterRow(Block block)
    {
        switch (block)
        {
            case ListBlock { BulletType: '-' } or ListItemBlock:
                return ((ContainerBlock)block) is not [var first, ..] || CanBeDelimiterRow(first);

            case ParagraphBlock paragraph:
                // The dashes can be typographic dashes
                for (var inline = paragraph.Inline?.FirstChild; inline is not null and not LineBreakInline; inline = inline.NextSibling)
                {
                    if (inline is not (LiteralInline or Extensions.SmartyPants.SmartyPant) || (inline is LiteralInline literal && literal.Content.AsSpan().ContainsAnyExcept("|-: \t")))
                    {
                        return false;
                    }
                }

                return true;

            case CodeBlock { Lines.Count: > 0 } code:
                return !code.Lines.Lines[0].Slice.AsSpan().ContainsAnyExcept("|-: \t");

            // A table, or a thematic break of dashes
            case Table or ThematicBreakBlock { ThematicChar: '-' }:
                return true;

            default:
                return false;
        }
    }

    // Writes the changed attributes of an inline right after it, where the parser attaches them to it
    private void WriteAttributesAfterInline(Inline inline, ChangedAttributes changedAttributes)
    {
        // The inlines that the parser attaches the attributes that follow them to, and that end with a punctuation, so that the text
        // after them is parsed the same after the attributes. The attributes after an emphasis would change the character after its
        // closing delimiters, which decides which delimiters are emphasis.
        var attributes = changedAttributes.Text!;
        var isAttached = inline switch
        {
            // The links that the extensions find in the text, such as www.example.com, are written as text, which the attributes
            // would continue
            LinkInline link => link.GetType() == typeof(LinkInline) && !link.IsAutoLink,
            AutolinkInline or CodeInline or HtmlInline => true,
            _ => false,
        };
        if (!isAttached)
        {
            AttributesTrivia.ThrowCannotWrite(inline, "the parser attaches the attributes that follow it to another object");
        }

        Block? leaf = null;
        for (var parent = inline.Parent; parent is not null; parent = parent.Parent)
        {
            leaf = parent.ParentBlock;
        }

        if (leaf is HeadingBlock { IsSetext: false })
        {
            AttributesTrivia.ThrowCannotWrite(inline, "the parser reads the attributes in an ATX heading as the attributes of the heading");
        }

        // The parser of a table reads the cells from the text, which the attributes would change
        for (var block = leaf; block is not null; block = block.Parent)
        {
            if (block is Table)
            {
                AttributesTrivia.ThrowCannotWrite(inline, "it is in a table");
            }
        }

        // The parser reads the line break that follows attributes as part of them, which only matters before another line
        if (inline.NextSibling is LineBreakInline { NextSibling: not null } || (inline.NextSibling is LiteralInline literal && literal.Content.AsSpan() is ['\r' or '\n', ..]))
        {
            AttributesTrivia.ThrowCannotWrite(inline, "it ends a line, whose line break would be read as part of the attributes");
        }

        // The parser of a fenced block reads the attributes of its opening line, and cuts the info string there, which a backtick
        // in it would make it a fence again
        var text = GetLeafText();
        var line = text.AsSpan(text.AsSpan().LastIndexOfAny('\r', '\n') + 1).TrimStart(" \t");
        if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
        {
            AttributesTrivia.ThrowCannotWrite(inline, "its line starts like a fenced block, which the attributes could make it");
        }

        // The links that the extensions find in the text end at a whitespace, so a link that starts before the inline would continue
        // in the attributes
        var textBefore = changedAttributes.TextBefore ?? "";
        var lineBefore = textBefore.AsSpan(textBefore.AsSpan().LastIndexOfAny('\r', '\n') + 1);
        var word = lineBefore[(lineBefore.LastIndexOfAny(" \t") + 1)..];
        foreach (var prefix in LinkPrefixes)
        {
            if (word.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            {
                AttributesTrivia.ThrowCannotWrite(inline, "the text before them could be a link that continues in them");
            }
        }

        AttributesTrivia.CheckAttributesAfterText(inline, text, attributes, readsFirstBrace: false, checkBlocks: true);
        Write(attributes);
        changedAttributes.IsWritten = true;
    }

    // Records the text written to a writer
    private sealed class RecordingTextWriter : TextWriter
    {
        private readonly StringBuilder _text = new();

        public RecordingTextWriter(TextWriter innerWriter)
            : base(innerWriter.FormatProvider)
        {
            InnerWriter = innerWriter;
            NewLine = innerWriter.NewLine;
        }

        public TextWriter InnerWriter { get; }

        public int Length => _text.Length;

        public override Encoding Encoding => InnerWriter.Encoding;

        public void AppendTo(StringBuilder sb, int start, int end) => sb.Append(_text, start, end - start);

        public override void Write(char value)
        {
            _text.Append(value);
            InnerWriter.Write(value);
        }

        public override void Write(char[] buffer, int index, int count) => Write(buffer.AsSpan(index, count));

        public override void Write(string? value) => Write(value.AsSpan());

        public override void Write(ReadOnlySpan<char> buffer)
        {
            _text.Append(buffer);
            InnerWriter.Write(buffer);
        }

        public override void Flush() => InnerWriter.Flush();
    }
}
