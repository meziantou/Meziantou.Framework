// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Parsers.Inlines;

/// <summary>
/// An inline parser for <see cref="EmphasisInline"/>.
/// </summary>
/// <seealso cref="InlineParser" />
/// <seealso cref="IPostInlineProcessor" />
public class EmphasisInlineParser : InlineParser, IPostInlineProcessor
{
    // Paragraphs with more emphasis delimiters than this keep track of the openers already rejected by a closer
    private const int OpenersBottomThreshold = 32;

    private CharacterMap<EmphasisDescriptor>? _emphasisMap;
    private readonly DelimitersObjectCache _inlinesCache = new();
    /// <summary>
    /// Represents the EmphasisInline type.
    /// </summary>
    public delegate EmphasisInline? TryCreateEmphasisInlineDelegate(char emphasisChar, int delimiterCount);

    /// <summary>
    /// Initializes a new instance of the <see cref="EmphasisInlineParser"/> class.
    /// </summary>
    public EmphasisInlineParser()
    {
        EmphasisDescriptors =
        [
            new EmphasisDescriptor('*', 1, 2, true),
            new EmphasisDescriptor('_', 1, 2, false)
        ];
    }

    /// <summary>
    /// Gets the emphasis descriptors.
    /// </summary>
    public List<EmphasisDescriptor> EmphasisDescriptors { get; }

    /// <summary>
    /// Gets or toggles whether the emphasis parser should be CJK-friendly.
    /// </summary>
    /// <seealso href="https://github.com/tats-u/markdown-cjk-friendly"/>
    public bool CjkFriendlyEmphasis { get; set; }

    /// <summary>
    /// Determines whether this parser is using the specified character as an emphasis delimiter.
    /// </summary>
    /// <param name="c">The character to look for.</param>
    /// <returns><c>true</c> if this parser is using the specified character as an emphasis delimiter; otherwise <c>false</c></returns>
    public bool HasEmphasisChar(char c)
    {
        foreach (var emphasis in EmphasisDescriptors)
        {
            if (emphasis.Character == c)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Gets or sets the try create emphasis inline list.
    /// </summary>
    public readonly List<TryCreateEmphasisInlineDelegate> TryCreateEmphasisInlineList = [];

    /// <summary>
    /// Performs the initialize operation.
    /// </summary>
    public override void Initialize()
    {
        OpeningCharacters = new char[EmphasisDescriptors.Count];

        var tempMap = new List<KeyValuePair<char, EmphasisDescriptor>>();
        for (int i = 0; i < EmphasisDescriptors.Count; i++)
        {
            var emphasis = EmphasisDescriptors[i];
            if (Array.IndexOf(OpeningCharacters, emphasis.Character) >= 0)
            {
                ThrowHelper.InvalidOperationException(
                    $"The character `{emphasis.Character}` is already used by another emphasis descriptor");
            }

            OpeningCharacters[i] = emphasis.Character;

            tempMap.Add(new KeyValuePair<char, EmphasisDescriptor>(emphasis.Character, emphasis));
        }

        _emphasisMap = new CharacterMap<EmphasisDescriptor>(tempMap);
    }

    /// <summary>
    /// Performs the post process operation.
    /// </summary>
    public bool PostProcess(InlineProcessor state, Inline? root, Inline? lastChild, int postInlineProcessorIndex, bool isFinalProcessing)
    {
        if (root is null || !root.IsContainerInline)
        {
            return true;
        }

        ContainerInline container = unsafe(Unsafe.As<ContainerInline>(root));

        List<EmphasisDelimiterInline>? delimiters = null;
        if (container is EmphasisDelimiterInline emphasisDelimiter)
        {
            delimiters = _inlinesCache.Get();
            delimiters.Add(emphasisDelimiter);
        }

        // Collect all EmphasisDelimiterInline by searching from the root container
        var child = container.FirstChild;
        while (child != null)
        {
            // Stop the search on the delimitation child
            if (child == lastChild)
            {
                break;
            }

            if (child.IsContainer && child is DelimiterInline delimiterInline)
            {
                // If we have a delimiter, we search into it as we should have a tree of EmphasisDelimiterInline
                if (delimiterInline is EmphasisDelimiterInline delimiter)
                {
                    delimiters ??= _inlinesCache.Get();
                    delimiters.Add(delimiter);
                }

                // Follow DelimiterInline (EmphasisDelimiter, TableDelimiter...)
                // If the delimiter has IsClosed=true (e.g., pipe table delimiter), it has no children
                // In that case, continue to next sibling instead of stopping
                var firstChild = delimiterInline.FirstChild;
                child = firstChild ?? delimiterInline.NextSibling;
            }
            else
            {
                child = child.NextSibling;
            }
        }

        if (delimiters != null)
        {
            ProcessEmphasis(delimiters);
            _inlinesCache.Release(delimiters);
        }
        return true;
    }

    /// <summary>
    /// Attempts to match the parser at the current position.
    /// </summary>
    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        // First, some definitions.
        // A delimiter run is a sequence of one or more delimiter characters that is not preceded or followed by the same delimiter character
        // The amount of delimiter characters in the delimiter run may exceed emphasisDesc.MaximumCount, as that is handeled in `ProcessEmphasis`

        var delimiterChar = slice.CurrentChar;
        var emphasisDesc = _emphasisMap![delimiterChar]!;

        Rune pc = (Rune)0;
        Rune twoPreviousChar = default;

        if (processor.Inline is HtmlEntityInline htmlEntityInline)
        {
            if (htmlEntityInline.Transcoded.Length > 0)
            {
                var transcoded = htmlEntityInline.Transcoded.Text.AsSpan(htmlEntityInline.Transcoded.Start, htmlEntityInline.Transcoded.Length);
                Rune.DecodeLastFromUtf16(transcoded, out pc, out int charsConsumed);

                if (CjkFriendlyEmphasis && charsConsumed < transcoded.Length)
                {
                    Rune.DecodeLastFromUtf16(transcoded.Slice(0, transcoded.Length - charsConsumed), out twoPreviousChar, out _);
                }
            }
        }
        if (pc.Value == 0)
        {
            pc = slice.PeekRuneExtra(-1);
            if (CjkFriendlyEmphasis)
            {
                // This cannot be a delegate (Func<Rune>?) because slice is a reference
                twoPreviousChar = slice.PeekRuneExtra(-1 - pc.Utf16SequenceLength);
            }
            // delimiterChar is BMP, so slice.PeekCharExtra(-2) is (a part of) the character two positions back.
            if (pc == (Rune)delimiterChar && slice.PeekCharExtra(-2) != '\\')
            {
                // If we get here, we determined that either:
                // a) there weren't enough delimiters in the delimiter run to satisfy the MinimumCount condition
                // b) the previous character couldn't open/close
                return false;
            }
        }
        var startPosition = slice.Start;

        int delimiterCount = slice.CountAndSkipChar(delimiterChar);

        // If the emphasis doesn't have the minimum required character
        if (delimiterCount < emphasisDesc.MinimumCount)
        {
            return false;
        }

        Rune c = slice.CurrentRune;

        // The following character is actually an entity, we need to decode it
        if (HtmlEntityParser.TryParse(ref slice, out string? htmlString, out _))
        {
            // Note: c is U+FFFD when decode error
            Rune.DecodeFromUtf16(htmlString, out c, out _);
        }

        bool canOpen;
        bool canClose;
        // Calculate Open-Close for current character
        if (CjkFriendlyEmphasis)
        {
            CharHelper.CheckOpenCloseDelimiterCjkFriendly(pc, c, twoPreviousChar, emphasisDesc.EnableWithinWord, out canOpen, out canClose);
        }
        else
        {
            CharHelper.CheckOpenCloseDelimiter(pc, c, emphasisDesc.EnableWithinWord, out canOpen, out canClose);
        }

        // We have potentially an open or close emphasis
        if (canOpen || canClose)
        {
            var delimiterType = DelimiterType.Undefined;
            if (canOpen) delimiterType |= DelimiterType.Open;
            if (canClose) delimiterType |= DelimiterType.Close;

            var delimiter = new EmphasisDelimiterInline(this, emphasisDesc, new StringSlice(slice.Text, startPosition, slice.Start - 1))
            {
                DelimiterCount = delimiterCount,
                Type = delimiterType,
                Span = new SourceSpan(processor.GetSourcePosition(startPosition, out int line, out int column), processor.GetSourcePosition(slice.Start - 1)),
                Column = column,
                Line = line,
            };

            processor.Inline = delimiter;
            return true;
        }

        // We don't have an emphasis
        return false;
    }

    private void ProcessEmphasis(List<EmphasisDelimiterInline> delimiters)
    {
        // The following method is inspired by the "An algorithm for parsing nested emphasis and links"
        // at the end of the CommonMark specs.

        // Delimiters are only removed at or before the current closer. The delimiter stack is the start of the list, and
        // delimiters are moved to it when they are reached, so removing them never shifts the ones not processed yet.
        var stackCount = 0;

        // For each emphasis character, closer that can also open or not, and closer count modulo 3, the delimiters below
        // openersBottom were all rejected as openers by a previous closer. Whether a delimiter can open for a closer only depends
        // on these and on the count of the delimiter, so they stay rejected until one of them is matched, which is the only
        // way the count of a delimiter changes: a match lowers the bottoms to the opener. Scanning a few delimiters is cheaper.
        var bottomCount = delimiters.Count > OpenersBottomThreshold ? OpeningCharacters!.Length * 6 : 0;
        Span<int> openersBottom = bottomCount <= 64 ? unsafe(stackalloc int[bottomCount]) : new int[bottomCount];
        openersBottom.Clear();
        var maximumBottom = 0;

        // Move current_position forward in the delimiter stack (if needed) until
        // we find the first potential closer with delimiter * or _. (This will be the potential closer closest to the beginning of the input – the first one in parse order.)
        for (var next = 0; next < delimiters.Count; next++)
        {
            var i = stackCount++;
            delimiters[i] = delimiters[next];
            var closeDelimiter = delimiters[i];
            // Skip delimiters not supported by this instance
            EmphasisDescriptor? emphasisDesc = _emphasisMap![closeDelimiter.DelimiterChar];
            if (emphasisDesc is null)
            {
                continue;
            }

            if (closeDelimiter.Type.HasFlag(DelimiterType.Close))
            {
                var characterIndex = -2;
                while (closeDelimiter.DelimiterCount >= emphasisDesc.MinimumCount)
                {
                    // The bottoms are all 0 until a closer finds no opener
                    var bottomIndex = -1;
                    if (maximumBottom > 0)
                    {
                        bottomIndex = GetBottomIndex(closeDelimiter, ref characterIndex);
                    }

                    // Now, look back in the stack (staying above stack_bottom and the openers_bottom for this delimiter type)
                    // for the first matching potential opener (“matching” means same delimiter).
                    EmphasisDelimiterInline? openDelimiter = null;
                    int openDelimiterIndex = -1;
                    for (int j = i - 1; j >= (bottomIndex < 0 ? 0 : openersBottom[bottomIndex]); j--)
                    {
                        var previousOpenDelimiter = delimiters[j];

                        var isOddMatch = (closeDelimiter.Type.HasFlag(DelimiterType.Open) || previousOpenDelimiter.Type.HasFlag(DelimiterType.Close)) &&
                                         previousOpenDelimiter.DelimiterCount != closeDelimiter.DelimiterCount &&
                                         (previousOpenDelimiter.DelimiterCount + closeDelimiter.DelimiterCount) % 3 == 0 &&
                                         (previousOpenDelimiter.DelimiterCount % 3 != 0 || closeDelimiter.DelimiterCount % 3 != 0);

                        if (previousOpenDelimiter.DelimiterChar == closeDelimiter.DelimiterChar &&
                            previousOpenDelimiter.Type.HasFlag(DelimiterType.Open) &&
                            previousOpenDelimiter.DelimiterCount >= emphasisDesc.MinimumCount && !isOddMatch)
                        {
                            openDelimiter = previousOpenDelimiter;
                            openDelimiterIndex = j;
                            break;
                        }
                    }

                    if (openDelimiter != null)
                    {
                        if (maximumBottom > openDelimiterIndex)
                        {
                            foreach (ref var bottom in openersBottom)
                            {
                                bottom = Math.Min(bottom, openDelimiterIndex);
                            }

                            maximumBottom = openDelimiterIndex;
                        }

                    process_delims:
                        Debug.Assert(openDelimiter.DelimiterCount >= emphasisDesc.MinimumCount, "Extra emphasis should have been discarded by now");
                        Debug.Assert(closeDelimiter.DelimiterCount >= emphasisDesc.MinimumCount, "Extra emphasis should have been discarded by now");
                        int delimiterDelta = Math.Min(Math.Min(openDelimiter.DelimiterCount, closeDelimiter.DelimiterCount), emphasisDesc.MaximumCount);

                        // Insert an emph or strong emph node accordingly, after the text node corresponding to the opener.
                        EmphasisInline? emphasis = null;

                        // Go in backwards order to give priority to newer delegates
                        for (int delegateIndex = TryCreateEmphasisInlineList.Count - 1; delegateIndex >= 0; delegateIndex--)
                        {
                            emphasis = TryCreateEmphasisInlineList[delegateIndex].Invoke(closeDelimiter.DelimiterChar, delimiterDelta);
                            if (emphasis != null) break;
                        }

                        emphasis ??= new EmphasisInline
                        {
                            DelimiterChar = closeDelimiter.DelimiterChar,
                            DelimiterCount = delimiterDelta
                        };

                        // Update position for emphasis
                        var openDelimitercount = openDelimiter.DelimiterCount;
                        var closeDelimitercount = closeDelimiter.DelimiterCount;

                        emphasis!.Span.Start = openDelimiter.Span.Start + openDelimitercount - delimiterDelta;
                        emphasis.Line = openDelimiter.Line;
                        emphasis.Column = openDelimiter.Column + openDelimitercount - delimiterDelta;
                        emphasis.Span.End = closeDelimiter.Span.End - closeDelimitercount + delimiterDelta;

                        openDelimiter.Span.End -= delimiterDelta;
                        openDelimiter.Content.End -= delimiterDelta;
                        closeDelimiter.Content.Start += delimiterDelta;
                        closeDelimiter.Span.Start += delimiterDelta;
                        closeDelimiter.Column += delimiterDelta;

                        openDelimiter.DelimiterCount -= delimiterDelta;
                        closeDelimiter.DelimiterCount -= delimiterDelta;

                        var embracer = (ContainerInline)openDelimiter;

                        // Copy attributes attached to delimiter to the emphasis
                        var attributes = closeDelimiter.TryGetAttributes();
                        if (attributes != null)
                        {
                            emphasis.SetAttributes(attributes);
                        }

                        // Embrace all delimiters
                        embracer.EmbraceChildrenBy(emphasis);

                        // Remove any intermediate emphasis
                        for (int k = i - 1; k >= openDelimiterIndex + 1; k--)
                        {
                            var literalDelimiter = delimiters[k];
                            literalDelimiter.ReplaceBy(literalDelimiter.AsLiteralInline());
                            RemoveFromStack(delimiters, ref stackCount, k);
                            i--;
                        }

                        if (closeDelimiter.DelimiterCount == 0)
                        {
                            var newParent = openDelimiter.DelimiterCount > 0 ? emphasis : emphasis.Parent!;
                            closeDelimiter.MoveChildrenAfter(newParent);
                            closeDelimiter.Remove();
                            RemoveFromStack(delimiters, ref stackCount, i);
                            i--;

                            // Remove the open delimiter if it is also empty
                            if (openDelimiter.DelimiterCount == 0)
                            {
                                openDelimiter.MoveChildrenAfter(openDelimiter);
                                openDelimiter.Remove();
                                RemoveFromStack(delimiters, ref stackCount, openDelimiterIndex);
                                i--;
                            }
                            break;
                        }

                        // The current delimiters are matching
                        if (openDelimiter.DelimiterCount >= emphasisDesc.MinimumCount &&
                            closeDelimiter.DelimiterCount >= emphasisDesc.MinimumCount)
                        {
                            goto process_delims;
                        }
                        else if (openDelimiter.DelimiterCount > 0)
                        {
                            // There are still delimiter characters left, there's just not enough of them
                            openDelimiter.ReplaceBy(openDelimiter.AsLiteralInline());
                            RemoveFromStack(delimiters, ref stackCount, openDelimiterIndex);
                            i--;
                        }
                        else
                        {
                            // Remove the open delimiter if it is also empty
                            var firstChild = openDelimiter.FirstChild!;
                            firstChild.Remove();
                            openDelimiter.ReplaceBy(firstChild);
                            firstChild.IsClosed = true;
                            closeDelimiter.Remove();
                            firstChild.InsertAfter(closeDelimiter);
                            RemoveFromStack(delimiters, ref stackCount, openDelimiterIndex);
                            i--;
                        }
                    }
                    else
                    {
                        if (!openersBottom.IsEmpty)
                        {
                            bottomIndex = GetBottomIndex(closeDelimiter, ref characterIndex);
                            if (bottomIndex >= 0)
                            {
                                openersBottom[bottomIndex] = i;
                                maximumBottom = Math.Max(maximumBottom, i);
                            }
                        }

                        // Keep unmatched closers attached until their trailing content
                        // has been moved out below.
                        break;
                    }
                }

                // Leftover delimiter characters keep the following inlines nested inside
                // the emphasis; move them out so they keep their original position.
                if (closeDelimiter.DelimiterCount > 0 && closeDelimiter.Parent is EmphasisInline closerParentEmphasis)
                {
                    var outermostEmphasis = closerParentEmphasis;
                    while (outermostEmphasis.Parent is EmphasisInline ancestorEmphasis)
                    {
                        outermostEmphasis = ancestorEmphasis;
                    }

                    closeDelimiter.MoveChildrenAfter(outermostEmphasis);
                }

                if (closeDelimiter.DelimiterCount > 0 && !closeDelimiter.Type.HasFlag(DelimiterType.Open))
                {
                    closeDelimiter.ReplaceBy(closeDelimiter.AsLiteralInline());
                    RemoveFromStack(delimiters, ref stackCount, i);
                }
            }
        }

        // Any delimiters left must be literal
        for (var i = 0; i < stackCount; i++)
        {
            var delimiter = delimiters[i];
            delimiter.ReplaceBy(delimiter.AsLiteralInline());
        }

        delimiters.Clear();

        int GetBottomIndex(EmphasisDelimiterInline closeDelimiter, ref int characterIndex)
        {
            if (characterIndex == -2)
            {
                characterIndex = Array.IndexOf(OpeningCharacters!, closeDelimiter.DelimiterChar);
            }

            return characterIndex < 0 ? -1 : (characterIndex * 6) + (closeDelimiter.Type.HasFlag(DelimiterType.Open) ? 3 : 0) + (closeDelimiter.DelimiterCount % 3);
        }

        static void RemoveFromStack(List<EmphasisDelimiterInline> delimiters, ref int stackCount, int index)
        {
            var stack = CollectionsMarshal.AsSpan(delimiters)[..stackCount];
            stack[(index + 1)..].CopyTo(stack[index..]);
            stackCount--;
        }
    }

    /// <summary>
    /// Represents the DelimitersObjectCache type.
    /// </summary>
    public class DelimitersObjectCache : ObjectCache<List<EmphasisDelimiterInline>>
    {
        /// <summary>
        /// Performs the new instance operation.
        /// </summary>
        protected override List<EmphasisDelimiterInline> NewInstance()
        {
            return new List<EmphasisDelimiterInline>(4);
        }

        /// <summary>
        /// Performs the reset operation.
        /// </summary>
        protected override void Reset(List<EmphasisDelimiterInline> instance)
        {
            instance.Clear();
        }
    }
}
