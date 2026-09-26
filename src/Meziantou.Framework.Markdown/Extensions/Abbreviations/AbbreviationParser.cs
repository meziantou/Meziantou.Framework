// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Markdig.Extensions.Abbreviations;

/// <summary>
/// A block parser for abbreviations.
/// </summary>
/// <seealso cref="BlockParser" />
public class AbbreviationParser : BlockParser
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AbbreviationParser"/> class.
    /// </summary>
    public AbbreviationParser()
    {
        OpeningCharacters = ['*'];
    }

    /// <summary>
    /// Attempts to open a block at the current parser position.
    /// </summary>
    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (processor.IsCodeIndent)
        {
            return BlockState.None;
        }

        // A link must be of the form *[Some Text]: An abbreviation 
        var slice = processor.Line;
        var startPosition = slice.Start;
        var c = slice.NextChar();
        if (c != '[')
        {
            return BlockState.None;
        }

        if (!LinkHelper.TryParseLabel(ref slice, out string? label, out SourceSpan labelSpan))
        {
            return BlockState.None;
        }

        c = slice.CurrentChar;
        if (c != ':')
        {
            return BlockState.None;
        }
        slice.SkipChar();

        slice.Trim();

        var abbr = new Abbreviation(this)
        {
            Label = label,
            Text = slice,
            Span = new SourceSpan(startPosition, slice.End),
            Line = processor.LineIndex,
            Column = processor.Column,
            LabelSpan = labelSpan,
        };
        if (!processor.Document.HasAbbreviations())
        {
            processor.Document.ProcessInlinesEnd += DocumentOnProcessInlinesEnd;
        }
        processor.Document.AddAbbreviation(abbr.Label, abbr);

        return BlockState.BreakDiscard;
    }

    private void DocumentOnProcessInlinesEnd(InlineProcessor inlineProcessor, Inline? inline)
    {
        var abbreviations = inlineProcessor.Document.GetAbbreviations();
        // Should not happen, but another extension could decide to remove them, so...
        if (abbreviations is null)
        {
            return;
        }

        // Build a text matcher from the abbreviations labels
        var prefixTree = new CompactPrefixTree<Abbreviation>(abbreviations);

        // Allocate the traversal stack once and reuse it across all leaf blocks.
        var stack = new Stack<ContainerInline>();

        foreach (var leaf in inlineProcessor.Document.Descendants<LeafBlock>())
        {
            if (leaf.Inline is not null)
            {
                SubstituteInlineTree(leaf.Inline, prefixTree, stack);
            }
        }
    }

    private static void SubstituteInlineTree(
        ContainerInline root,
        CompactPrefixTree<Abbreviation> prefixTree,
        Stack<ContainerInline> stack)
    {
        stack.Push(root);

        while (stack.Count > 0)
        {
            var container = stack.Pop();
            var child = container.FirstChild;
            while (child != null)
            {
                var next = child.NextSibling;
                if (child is LiteralInline literal)
                {
                    SubstituteInLiteral(literal, prefixTree);
                }
                else if (child is ContainerInline childContainer)
                {
                    stack.Push(childContainer);
                }
                child = next;
            }
        }
    }

    private static void SubstituteInLiteral(LiteralInline literal, CompactPrefixTree<Abbreviation> prefixTree)
    {
        var content = literal.Content;
        var text = content.Text;
        var parent = literal.Parent;

        // Nothing to do if this literal has no parent to insert siblings into
        if (parent is null)
        {
            return;
        }

        // Save original span end before any mutations: on the first substitution
        // currentLiteral IS literal, so currentLiteral.Span.End = abbrSpanStart - 1
        // would corrupt literal.Span.End, which we need for remaining-literal calculations.
        var originalSpanEnd = literal.Span.End;

        // The "current" literal we're truncating as we find abbreviations.
        // We start with the original literal — it stays in place and we insert after it.
        var currentLiteral = literal;

        for (int i = content.Start; i <= content.End; i++)
        {
            // Abbreviation must start at the beginning of the content or after whitespace
            if (i != content.Start)
            {
                // Find the next whitespace-separated word start
                for (i = i - 1; i <= content.End; i++)
                {
                    if (text[i].IsWhitespace())
                    {
                        i++;
                        goto ValidAbbreviationStart;
                    }
                }
                break;
            }

        ValidAbbreviationStart:;

            if (prefixTree.TryMatchLongest(text.AsSpan(i, content.End - i + 1), out KeyValuePair<string, Abbreviation> abbreviationMatch))
            {
                var match = abbreviationMatch.Key;
                if (!IsValidAbbreviationEnding(match, content, i))
                {
                    continue;
                }

                var indexAfterMatch = i + match.Length;

                // Compute source position from the original literal's own span/line/column.
                // We use literal.Content.Start (the ORIGINAL literal parameter, never reassigned)
                // because span positions are always relative to the start of the original literal.
                // (InlineProcessor is not available post-parse; this is safe because
                //  LiteralInlineParser never produces a literal spanning a line break.)
                int charOffset = i - literal.Content.Start; // offset from original literal start
                var abbrSpanStart = literal.Span.Start + charOffset;
                var abbrInline = new AbbreviationInline(abbreviationMatch.Value)
                {
                    Span = new SourceSpan(abbrSpanStart, abbrSpanStart + match.Length - 1),
                    Line = literal.Line,
                    Column = literal.Column + charOffset,
                };

                // Truncate currentLiteral to end just before the abbreviation
                currentLiteral.Content.End = i - 1;
                currentLiteral.Span.End = abbrSpanStart - 1;

                // Insert abbreviation after currentLiteral
                currentLiteral.InsertAfter(abbrInline);

                // If the truncated literal is now empty (abbreviation was at the very start
                // of its content), remove it so it doesn't litter the tree.
                if (currentLiteral.Content.End < currentLiteral.Content.Start)
                {
                    currentLiteral.Remove();
                }

                // If there is remaining text after the abbreviation, create a new literal for it
                if (indexAfterMatch <= content.End)
                {
                    var remainingContent = content;
                    remainingContent.Start = indexAfterMatch;

                    var remainingLiteral = new LiteralInline
                    {
                        Content = remainingContent,
                        Span = new SourceSpan(abbrInline.Span.End + 1, originalSpanEnd),
                        Line = literal.Line,
                        Column = literal.Column + (indexAfterMatch - literal.Content.Start),
                    };
                    abbrInline.InsertAfter(remainingLiteral);

                    // Continue scanning from the new literal
                    currentLiteral = remainingLiteral;
                    // Update content reference so the loop bounds are correct
                    content = remainingContent;
                    i = indexAfterMatch - 1;
                }
                else
                {
                    // No text left — stop
                    break;
                }
            }
        }
    }

    private static bool IsValidAbbreviationEnding(string match, StringSlice content, int matchIndex)
    {
        // This will check if the next char at the end of the StringSlice is whitespace, punctuation or \0.
        var contentNew = content;
        contentNew.End = content.End + 1;
        int index = matchIndex + match.Length;
        while (index <= contentNew.End)
        {
            var c = contentNew.PeekCharAbsolute(index);

            if (c.IsWhitespace())
            {
                break;
            }

            if (!c.IsAsciiPunctuationOrZero())
            {
                return false;
            }

            index++;
        }
        return true;
    }
}
