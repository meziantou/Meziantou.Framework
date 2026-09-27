// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Meziantou.Framework.Markdown.Extensions.GenericAttributes;
using Meziantou.Framework.Markdown.Extensions.Tables;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Parsers;

/// <summary>
/// The inline parser state used by all <see cref="InlineParser"/>.
/// </summary>
public class InlineProcessor
{
    /// <summary>
    /// The number of open containers or inlines that are walked; beyond it, the chain of open containers is tracked instead.
    /// </summary>
    internal const int OpenContainersTrackingThreshold = 256;

    private readonly List<StringLineGroup.LineOffset> _lineOffsets = [];
    private int _previousSliceOffset;
    private int _previousLineIndexForSliceOffset;
    private int[]? _unescapedSourceOffsets;
    private int _unescapedSourceStart;
    internal ContainerBlock? _previousContainerToReplace;
    internal ContainerBlock? _newContainerToReplace;
    private InlineLinkScanCache? _linkScanCache;
    private InlineHtmlScanCache? _htmlScanCache;
    private GenericAttributesScanCache? _genericAttributesScanCache;
    private AutoLinkScanCache? _autoLinkScanCache;
    private readonly InlineContainerChain _openContainers = new();
    private bool _isParsingInlines;
    private bool _isOpenContainersEngaged;

    /// <summary>
    /// Initializes a new instance of the <see cref="InlineProcessor" /> class.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="parsers">The parsers.</param>
    /// <param name="preciseSourcelocation">A value indicating whether to provide precise source location.</param>
    /// <param name="context">A parser context used for the parsing.</param>
    /// <param name="trackTrivia">Whether to parse trivia such as whitespace, extra heading characters and unescaped string values.</param>
    /// <exception cref="ArgumentNullException">
    /// </exception>
    public InlineProcessor(MarkdownDocument document, InlineParserList parsers, bool preciseSourcelocation, MarkdownParserContext? context, bool trackTrivia = false)
    {
        Setup(document, parsers, preciseSourcelocation, context, trackTrivia);
    }

    private InlineProcessor() { }

    /// <summary>
    /// Gets the current block being processed.
    /// </summary>
    public LeafBlock? Block { get; private set; }

    /// <summary>
    /// Gets a value indicating whether to provide precise source location.
    /// </summary>
    public bool PreciseSourceLocation { get; private set; }

    /// <summary>
    /// Gets or sets the new block to replace the block being processed.
    /// </summary>
    public Block? BlockNew { get; set; }

    /// <summary>
    /// Gets or sets the current inline. Used by <see cref="InlineParser"/> to return a new inline if match was successfull
    /// </summary>
    public Inline? Inline { get; set; }

    /// <summary>
    /// Gets the root container of the current <see cref="Block"/>.
    /// </summary>
    public ContainerInline? Root { get; internal set; }

    /// <summary>
    /// Gets the list of inline parsers.
    /// </summary>
    public InlineParserList Parsers { get; private set; } = null!; // Set in Setup

    /// <summary>
    /// Gets the parser context or <c>null</c> if none is available.
    /// </summary>
    public MarkdownParserContext? Context { get; private set; }

    /// <summary>
    /// Gets the root document.
    /// </summary>
    public MarkdownDocument Document { get; private set; } = null!; // Set in Setup

    /// <summary>
    /// Gets or sets the index of the line from the begining of the document being processed.
    /// </summary>
    public int LineIndex { get; private set; }

    /// <summary>
    /// Gets the parser states that can be used by <see cref="InlineParser"/> using their <see cref="ParserBase{Inline}.Index"/> property.
    /// </summary>
    public object[] ParserStates { get; private set; } = null!; // Set in Setup

    /// <summary>
    /// Gets the cache used by <see cref="LinkInlineParser"/> to avoid scanning the same characters again for each link opener.
    /// </summary>
    internal InlineLinkScanCache LinkScanCache => _linkScanCache ??= new();

    /// <summary>
    /// Gets the cache used by <see cref="AutolinkInlineParser"/> to avoid searching the same characters again for the end of each inline raw HTML construct.
    /// </summary>
    internal InlineHtmlScanCache HtmlScanCache => _htmlScanCache ??= new();

    /// <summary>
    /// Gets the cache used by <see cref="GenericAttributesParser"/> to avoid scanning the same characters again for each '{'.
    /// </summary>
    internal GenericAttributesScanCache GenericAttributesScanCache => _genericAttributesScanCache ??= new();

    /// <summary>
    /// Gets the cache used by the autolink parser to avoid scanning the same characters again for each URL candidate.
    /// </summary>
    internal AutoLinkScanCache AutoLinkScanCache => _autoLinkScanCache ??= new();

    private long _referenceExpansionLength;

    /// <summary>
    /// Gets or sets the maximum total length of the text copied into the document when references are expanded (the URL
    /// and title of a link reference definition, the text of an abbreviation). Each use copies the definition, so without
    /// a limit a long definition used many times turns a small document into a huge one.
    /// </summary>
    internal long MaximumReferenceExpansionLength { get; set; } = long.MaxValue;

    /// <summary>
    /// Records the expansion of a reference, and returns <see langword="false"/> when it would exceed <see cref="MaximumReferenceExpansionLength"/>.
    /// </summary>
    internal bool TryAddReferenceExpansion(long length)
    {
        if (length > MaximumReferenceExpansionLength - _referenceExpansionLength)
            return false;

        _referenceExpansionLength += length;
        return true;
    }

    /// <summary>
    /// Gets or sets the debug log writer. No log if null.
    /// </summary>
    public TextWriter? DebugLog { get; set; }

    /// <summary>
    /// True to parse trivia such as whitespace, extra heading characters and unescaped
    /// string values.
    /// </summary>
    public bool TrackTrivia { get; private set; }

    /// <summary>
    /// Gets the literal inline parser.
    /// </summary>
    public LiteralInlineParser LiteralInlineParser { get; } = new();

    /// <summary>
    /// Gets source position from local span.
    /// </summary>
    public SourceSpan GetSourcePositionFromLocalSpan(SourceSpan span)
    {
        if (span.IsEmpty)
        {
            return SourceSpan.Empty;
        }

        return new SourceSpan(GetSourcePosition(span.Start, out _, out _), GetSourcePosition(span.End));
    }

    /// <summary>
    /// Gets the source position for the specified offset within the current slice.
    /// </summary>
    /// <param name="sliceOffset">The slice offset.</param>
    /// <param name="lineIndex">The line index.</param>
    /// <param name="column">The column.</param>
    /// <returns>The source position</returns>
    public int GetSourcePosition(int sliceOffset, out int lineIndex, out int column)
    {
        if (_unescapedSourceOffsets is { } offsetsMap && (uint)sliceOffset < (uint)offsetsMap.Length)
        {
            // This overload locates the start of an inline: include a removed
            // pipe escape. The position-only overload locates its original end.
            sliceOffset = sliceOffset == 0 ? _unescapedSourceStart : offsetsMap[sliceOffset - 1] + 1;
        }
        column = 0;
        lineIndex = sliceOffset >= _previousSliceOffset ? _previousLineIndexForSliceOffset : 0;
        int position = 0;
        if (PreciseSourceLocation)
        {
            var offsets = CollectionsMarshal.AsSpan(_lineOffsets);

            for (; (uint)lineIndex < (uint)offsets.Length; lineIndex++)
            {
                ref var lineOffset = ref offsets[lineIndex];

                if (sliceOffset <= lineOffset.End)
                {
                    // Use the beginning of the line as a previous slice offset
                    // (since it is on the same line)
                    _previousSliceOffset = lineOffset.Start;
                    var delta = sliceOffset - _previousSliceOffset;
                    column = lineOffset.Column + delta;
                    position = lineOffset.LinePosition + delta + lineOffset.Offset;
                    _previousLineIndexForSliceOffset = lineIndex;

                    // Return an absolute line index
                    lineIndex = lineIndex + LineIndex;
                    break;
                }
            }
        }
        return position;
    }

    /// <summary>
    /// Gets the source position for the specified offset within the current slice.
    /// </summary>
    /// <param name="sliceOffset">The slice offset.</param>
    /// <returns>The source position</returns>
    public int GetSourcePosition(int sliceOffset)
    {
        if (_unescapedSourceOffsets is { } offsetsMap && (uint)sliceOffset < (uint)offsetsMap.Length)
            sliceOffset = offsetsMap[sliceOffset];
        if (PreciseSourceLocation)
        {
            int lineIndex = sliceOffset >= _previousSliceOffset ? _previousLineIndexForSliceOffset : 0;

            var offsets = CollectionsMarshal.AsSpan(_lineOffsets);

            for (; (uint)lineIndex < (uint)offsets.Length; lineIndex++)
            {
                ref var lineOffset = ref offsets[lineIndex];

                if (sliceOffset <= lineOffset.End)
                {
                    _previousLineIndexForSliceOffset = lineIndex;
                    _previousSliceOffset = lineOffset.Start;

                    return sliceOffset - lineOffset.Start + lineOffset.LinePosition + lineOffset.Offset;
                }
            }
        }
        return 0;
    }

    /// <summary>
    /// Requests a replacement for a parent container while processing the current leaf block.
    /// </summary>
    /// <param name="previousParentContainer">The parent container that has already been replaced in the block tree.</param>
    /// <param name="newParentContainer">The replacement parent container.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="previousParentContainer"/> or <paramref name="newParentContainer"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if a replacement has already been requested for this leaf processing pass.</exception>
    /// <remarks>
    /// This method does not perform the replacement in the block tree. Callers must already update
    /// the AST (replace the parent block and transfer children as needed). This method only synchronizes
    /// the traversal state used by <see cref="MarkdownParser.ProcessInlines"/>.
    /// </remarks>
    public void ReplaceParentContainer(ContainerBlock previousParentContainer, ContainerBlock newParentContainer)
    {
        if (previousParentContainer is null) ThrowHelper.ArgumentNullException(nameof(previousParentContainer));
        if (newParentContainer is null) ThrowHelper.ArgumentNullException(nameof(newParentContainer));

        // Limitation for now, only one parent container can be replaced.
        if (_previousContainerToReplace != null)
        {
            throw new InvalidOperationException("A block is already being replaced");
        }

        _previousContainerToReplace = previousParentContainer;
        _newContainerToReplace = newParentContainer;
    }

    /// <summary>
    /// Processes the inline of the specified <see cref="LeafBlock"/>.
    /// </summary>
    /// <param name="leafBlock">The leaf block.</param>
    public void ProcessInlineLeaf(LeafBlock leafBlock)
    {
        if (leafBlock is null) ThrowHelper.ArgumentNullException_leafBlock();

        _previousContainerToReplace = null;
        _newContainerToReplace = null;

        // clear parser states
        Array.Clear(ParserStates, 0, ParserStates.Length);

        Root = new ContainerInline() { IsClosed = false };
        leafBlock.Inline = Root;
        _openContainers.MaximumDepth = 0;
        Inline = null;
        Block = leafBlock;
        BlockNew = null;
        LineIndex = leafBlock.Line;

        _previousSliceOffset = 0;
        _previousLineIndexForSliceOffset = 0;
        _lineOffsets.Clear();
        _htmlScanCache?.Clear();
        _genericAttributesScanCache?.Clear();
        var text = leafBlock.Lines.ToSlice(_lineOffsets);
        _unescapedSourceStart = text.Start;
        _unescapedSourceOffsets = leafBlock.Parser is GfmPipeTableParser
            ? GfmPipeTableParser.UnescapePipes(ref text) : null;
        var textEnd = text.End;
        leafBlock.Lines.Release();
        int previousStart = -1;

        // The chain of open containers is only tracked while the parsers run, even when one of them throws
        _isParsingInlines = true;
        using var parsingScope = new InlineParsingScope(this);

        while (!text.IsEmpty)
        {
            // Security check so that the parser can't go into a crazy infinite loop if one extension is messing
            if (previousStart == text.Start)
            {
                ThrowHelper.InvalidOperationException($"The parser is in an invalid infinite loop while trying to parse inlines for block [{leafBlock.GetType().Name}] at position ({leafBlock.ToPositionText()}");
            }
            previousStart = text.Start;

            var c = text.CurrentChar;

            var textSaved = text;
            var parsers = Parsers.GetParsersForOpeningCharacter(c);
            if (parsers != null)
            {
                for (int i = 0; i < parsers.Length; i++)
                {
                    text = textSaved;
                    if (parsers[i].Match(this, ref text))
                    {
                        goto done;
                    }
                }
            }
            parsers = Parsers.GlobalParsers;
            if (parsers != null)
            {
                for (int i = 0; i < parsers.Length; i++)
                {
                    text = textSaved;
                    if (parsers[i].Match(this, ref text))
                    {
                        goto done;
                    }
                }
            }

            text = textSaved;
            // Else match using the default literal inline parser
            LiteralInlineParser.Match(this, ref text);

            done:
            var nextInline = Inline;
            if (nextInline != null)
            {
                if (nextInline.Parent is null)
                {
                    // Get deepest container
                    var container = FindLastContainer();
                    if (!ReferenceEquals(container, nextInline))
                    {
                        container.AppendChild(nextInline);
                    }

                    if (container == Root)
                    {
                        if (container.Span.IsEmpty)
                        {
                            container.Span = nextInline.Span;
                        }
                        container.Span.End = nextInline.Span.End;
                    }

                }
            }
            else
            {
                // Get deepest container
                var container = FindLastContainer();

                Inline = container.LastChild is LeafInline ? container.LastChild : container;
                if (Inline == Root)
                {
                    Inline = null;
                }
            }

            //if (DebugLog != null)
            //{
            //    DebugLog.WriteLine($"** Dump: char '{c}");
            //    leafBlock.Inline.DumpTo(DebugLog);
            //}
        }

        if (TrackTrivia)
        {
            if (!(leafBlock is HeadingBlock))
            {
                var newLine = leafBlock.NewLine;
                if (newLine != NewLine.None)
                {
                    var position = GetSourcePosition(textEnd + 1, out int line, out int column);
                    leafBlock.Inline.AppendChild(new LineBreakInline { NewLine = newLine, Line = line, Column = column, Span = { Start = position, End = position + (newLine == NewLine.CarriageReturnLineFeed ? 1 : 0) } });
                }
            }
        }

        Inline = null;
        //if (DebugLog != null)
        //{
        //    DebugLog.WriteLine("** Dump before Emphasis:");
        //    leafBlock.Inline.DumpTo(DebugLog);
        //}

        // The parsers are done, so the containers do not need to report their changes anymore
        EndInlineParsing();
        var maximumContainerDepth = _openContainers.MaximumDepth;

        // PostProcess all inlines
        PostProcessInlines(0, Root, null, true);

        // Unresolved delimiters nest the inlines that follow them, so the depth reached while parsing grows
        // with the number of delimiters even when post-processing resolves them into a flat tree
        // (e.g. `*a* *b* ...`). Only reject the input when the resolved inlines are really nested that deeply.
        if (maximumContainerDepth > ThrowHelper.LargeDepthLimit)
        {
            ThrowHelper.CheckDepthLimit(GetInlineNestingDepth(BlockNew ?? leafBlock), useLargeLimit: true);
        }

        //TransformDelimitersToLiterals();

        //if (DebugLog != null)
        //{
        //    DebugLog.WriteLine();
        //    DebugLog.WriteLine("** Dump after Emphasis:");
        //    leafBlock.Inline.DumpTo(DebugLog);
        //}

        if (leafBlock.Inline.LastChild is not null)
        {
            leafBlock.Inline.Span.End = leafBlock.Inline.LastChild.Span.End;
            leafBlock.UpdateSpanEnd(leafBlock.Inline.Span.End);
        }
    }

    /// <summary>
    /// Performs the post process inlines operation.
    /// </summary>
    public void PostProcessInlines(int startingIndex, Inline? root, Inline? lastChild, bool isFinalProcessing)
    {
        for (int i = startingIndex; i < Parsers.PostInlineProcessors.Length; i++)
        {
            var postInlineProcessor = Parsers.PostInlineProcessors[i];
            if (!postInlineProcessor.PostProcess(this, root, lastChild, i, isFinalProcessing))
            {
                break;
            }
        }
    }

    /// <summary>
    /// Gets or creates a parser state instance scoped to the current leaf processing pass.
    /// </summary>
    /// <typeparam name="TState">The type of state to get or create.</typeparam>
    /// <param name="parser">The parser requesting the state.</param>
    /// <param name="factory">A factory used to create a state instance when none exists yet.</param>
    /// <returns>The existing or newly created state instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="parser"/> or <paramref name="factory"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if <paramref name="factory"/> returns <c>null</c>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TState GetParserState<TState>(InlineParser parser, Func<TState> factory) where TState : class
    {
        if (parser is null) ThrowHelper.ArgumentNullException(nameof(parser));
        if (factory is null) ThrowHelper.ArgumentNullException(nameof(factory));

        ref var slot = ref ParserStates[parser.Index];
        if (slot is TState state)
        {
            return state;
        }

        state = factory();
        if (state is null)
        {
            ThrowHelper.InvalidOperationException($"The state factory for [{typeof(TState)}] returned null");
        }

        slot = state;
        return state;
    }

    /// <summary>
    /// Gets or creates a parser state instance scoped to the current leaf processing pass.
    /// </summary>
    /// <typeparam name="TState">The type of state to get or create.</typeparam>
    /// <param name="parser">The parser requesting the state.</param>
    /// <returns>The existing or newly created state instance.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TState GetParserState<TState>(InlineParser parser) where TState : class, new()
    {
        return GetParserState(parser, static () => new TState());
    }

    /// <summary>
    /// Emits an inline into the deepest open inline container for the current leaf.
    /// </summary>
    /// <param name="inline">The inline to emit.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inline"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="inline"/> is already attached to a parent.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Emit(Inline inline)
    {
        if (inline is null) ThrowHelper.ArgumentNullException(nameof(inline));
        if (inline.Parent is not null)
        {
            ThrowHelper.ArgumentException("Inline has already a parent", nameof(inline));
        }

        var container = FindLastContainer();
        container.AppendChild(inline);

        if (ReferenceEquals(container, Root) && !inline.Span.IsEmpty)
        {
            if (container.Span.IsEmpty)
            {
                container.Span = inline.Span;
            }
            else
            {
                if (inline.Span.Start < container.Span.Start)
                {
                    container.Span.Start = inline.Span.Start;
                }

                if (inline.Span.End > container.Span.End)
                {
                    container.Span.End = inline.Span.End;
                }
            }

            Block?.UpdateSpanToInclude(inline.Span);
        }

        Inline = inline;
    }

    private ContainerInline FindLastContainer()
    {
        var root = Block!.Inline!;
        if (_isOpenContainersEngaged)
        {
            if (ReferenceEquals(_openContainers.Root, root))
            {
                return _openContainers.GetDeepestContainer();
            }

            DisengageOpenContainers();
        }

        var container = root;
        for (int depth = 0; ; depth++)
        {
            Inline? lastChild = container.LastChild;
            if (lastChild is not null && lastChild.IsContainerInline && !lastChild.IsClosed)
            {
                // Each unresolved delimiter is an open container, so walking the chain for each inline is quadratic when it is
                // deep. Track it instead.
                if (depth >= OpenContainersTrackingThreshold && _isParsingInlines)
                {
                    EngageOpenContainers(root);
                    return _openContainers.GetDeepestContainer();
                }

                container = unsafe(Unsafe.As<ContainerInline>(lastChild));
            }
            else
            {
                ThrowHelper.CheckDepthLimit(depth, useLargeLimit: true);
                return container;
            }
        }
    }

    /// <summary>
    /// Gets the chain of open containers when it is tracked and still describes the parents of the inlines of the current leaf.
    /// </summary>
    private bool TryGetOpenContainers([NotNullWhen(true)] out InlineContainerChain? openContainers, bool engage = false)
    {
        if (engage && _isParsingInlines && !_isOpenContainersEngaged && Block?.Inline is { } root)
        {
            EngageOpenContainers(root);
        }

        // The chain does not know the parents of its root, so it cannot be used when the root was replaced or moved
        if (_isOpenContainersEngaged && _openContainers.Root is { Parent: null } chainRoot && ReferenceEquals(chainRoot, Block?.Inline))
        {
            openContainers = _openContainers;
            return true;
        }

        openContainers = null;
        return false;
    }

    private void EngageOpenContainers(ContainerInline root)
    {
        _isOpenContainersEngaged = true;
        _openContainers.Engage(root);
    }

    private void DisengageOpenContainers()
    {
        if (_isOpenContainersEngaged)
        {
            _isOpenContainersEngaged = false;
            _openContainers.Disengage();
        }
    }

    private void EndInlineParsing()
    {
        _isParsingInlines = false;
        DisengageOpenContainers();

        // The scans of the autolink parser only apply to the inline text of this leaf
        _autoLinkScanCache?.Clear();
    }

    /// <summary>
    /// Finds the nearest <see cref="LinkDelimiterInline"/> among an inline and its parents.
    /// </summary>
    internal LinkDelimiterInline? FindLinkDelimiter(Inline inline)
    {
        return _isOpenContainersEngaged && TryGetOpenContainers(out var openContainers) && openContainers.TryFindLinkDelimiter(inline, out var linkDelimiter)
            ? linkDelimiter
            : inline.FirstParentOfType<LinkDelimiterInline>();
    }

    /// <summary>
    /// Determines whether an inline or one of its parents is an active <see cref="LinkDelimiterInline"/>.
    /// </summary>
    internal bool HasActiveLinkDelimiter(Inline? inline)
    {
        if (inline is null)
        {
            return false;
        }

        if (_isOpenContainersEngaged && TryGetOpenContainers(out var openContainers) && openContainers.TryHasActiveLinkDelimiter(inline, out var hasActiveLinkDelimiter))
        {
            return hasActiveLinkDelimiter;
        }

        for (; inline is not null; inline = inline.Parent)
        {
            if (inline is LinkDelimiterInline { IsActive: true })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Deactivates an inline and its parents that are link delimiters. Image delimiters are left active.
    /// </summary>
    internal void DeactivateLinkDelimiters(Inline? inline)
    {
        if (inline is null || (_isOpenContainersEngaged && TryGetOpenContainers(out var openContainers) && openContainers.TryDeactivateLinkDelimiters(inline)))
        {
            return;
        }

        // Image delimiters stay active: a link can be in the description of an image, but not in the text of another link
        for (; inline is not null; inline = inline.Parent)
        {
            if (inline is LinkDelimiterInline { IsImage: false } linkDelimiter)
            {
                linkDelimiter.IsActive = false;
            }
        }
    }

    /// <summary>
    /// Gets what the autolink parser looks for before an inline. See <see cref="InlineContainerChain.TryGetAutoLinkContext"/>.
    /// </summary>
    /// <param name="inline">The inline.</param>
    /// <param name="engage"><c>true</c> to track the chain of open containers when it is not tracked yet.</param>
    /// <param name="anchor">The anchor HTML tag.</param>
    /// <param name="linkDelimiterBalance">The balance of the active link delimiters.</param>
    /// <param name="emphasisCharacters">The characters of the emphasis delimiters.</param>
    internal bool TryGetAutoLinkContext(Inline inline, bool engage, out HtmlInline? anchor, out int linkDelimiterBalance, out string emphasisCharacters)
    {
        if (TryGetOpenContainers(out var openContainers, engage))
        {
            return openContainers.TryGetAutoLinkContext(inline, out anchor, out linkDelimiterBalance, out emphasisCharacters);
        }

        anchor = null;
        linkDelimiterBalance = 0;
        emphasisCharacters = "";
        return false;
    }

    /// <summary>
    /// Finds the nearest parent of an inline that is not a <see cref="DelimiterInline"/>.
    /// </summary>
    internal ContainerInline? FindNonDelimiterParent(Inline inline)
    {
        if (_isOpenContainersEngaged && TryGetOpenContainers(out var openContainers) && openContainers.TryFindNonDelimiterParent(inline, out var parent))
        {
            return parent;
        }

        parent = inline.Parent;
        while (parent is DelimiterInline)
        {
            parent = parent.Parent;
        }

        return parent;
    }

    /// <summary>
    /// Determines whether an inline or one of its parents is a <see cref="PipeTableDelimiterInline"/>, or whether its top
    /// parent has one as a child, like <see cref="Inline.ContainsParentOrSiblingOfType{T}"/>.
    /// </summary>
    internal bool ContainsPipeTableDelimiter(Inline inline)
    {
        if (_isOpenContainersEngaged && TryGetOpenContainers(out var openContainers) && openContainers.TryContainsPipeTableDelimiter(inline, out var containsPipeTableDelimiter))
        {
            return containsPipeTableDelimiter;
        }

        if (inline.ContainsParentOfType<PipeTableDelimiterInline>())
        {
            return true;
        }

        var root = inline.Parent;
        while (root?.Parent is not null)
        {
            root = root.Parent;
        }

        // The children of the root are all the inlines of a flat table, so track the chain instead of walking them each time
        var count = 0;
        for (var sibling = root?.FirstChild; sibling is not null; sibling = sibling.NextSibling)
        {
            if (sibling is PipeTableDelimiterInline)
            {
                return true;
            }

            if (++count == OpenContainersTrackingThreshold &&
                TryGetOpenContainers(out openContainers, engage: true) &&
                openContainers.TryContainsPipeTableDelimiter(inline, out containsPipeTableDelimiter))
            {
                return containsPipeTableDelimiter;
            }
        }

        return false;
    }

    private static int GetInlineNestingDepth(Block block)
    {
        var maximumDepth = 0;
        var stack = new Stack<(MarkdownObject Node, int Depth)>();
        stack.Push((block, 0));
        while (stack.TryPop(out var item))
        {
            switch (item.Node)
            {
                case ContainerBlock containerBlock:
                    foreach (var child in containerBlock)
                    {
                        stack.Push((child, 0));
                    }

                    break;

                case LeafBlock { Inline: { } inline }:
                    stack.Push((inline, 0));
                    break;

                case ContainerInline containerInline:
                    maximumDepth = Math.Max(maximumDepth, item.Depth);
                    for (var child = containerInline.FirstChild; child is not null; child = child.NextSibling)
                    {
                        if (child is ContainerInline)
                        {
                            stack.Push((child, item.Depth + 1));
                        }
                    }

                    break;
            }
        }

        return maximumDepth;
    }


    [MemberNotNull(nameof(Document), nameof(Parsers), nameof(ParserStates))]
    private void Setup(MarkdownDocument document, InlineParserList parsers, bool preciseSourcelocation, MarkdownParserContext? context, bool trackTrivia)
    {
        if (document is null) ThrowHelper.ArgumentNullException(nameof(document));
        if (parsers is null) ThrowHelper.ArgumentNullException(nameof(parsers));

        Document = document;
        Parsers = parsers;
        Context = context;
        PreciseSourceLocation = preciseSourcelocation;
        TrackTrivia = trackTrivia;

        if (ParserStates is null || ParserStates.Length < Parsers.Count)
        {
            ParserStates = new object[Parsers.Count];
        }
    }

    private void Reset()
    {
        _unescapedSourceOffsets = null;
        EndInlineParsing();
        Block = null;
        BlockNew = null;
        Inline = null;
        Root = null;
        Parsers = null!;
        Context = null;
        Document = null!;
        DebugLog = null;

        PreciseSourceLocation = false;
        TrackTrivia = false;

        LineIndex = 0;
        _previousSliceOffset = 0;
        _previousLineIndexForSliceOffset = 0;

        LiteralInlineParser.PostMatch = null;

        _lineOffsets.Clear();
        Array.Clear(ParserStates, 0, ParserStates.Length);
        _linkScanCache?.Clear();
        _htmlScanCache?.Clear();
        _genericAttributesScanCache?.Clear();
        _referenceExpansionLength = 0;
        MaximumReferenceExpansionLength = long.MaxValue;
    }

    private static readonly InlineProcessorCache Cache = new();

    internal static InlineProcessor Rent(MarkdownDocument document, InlineParserList parsers, bool preciseSourcelocation, MarkdownParserContext? context, bool trackTrivia)
    {
        var processor = Cache.Get();
        processor.Setup(document, parsers, preciseSourcelocation, context, trackTrivia);
        return processor;
    }

    internal static void Release(InlineProcessor processor)
    {
        Cache.Release(processor);
    }

    private sealed class InlineProcessorCache : ObjectCache<InlineProcessor>
    {
        protected override InlineProcessor NewInstance() => new InlineProcessor();

        protected override void Reset(InlineProcessor instance) => instance.Reset();
    }

    private readonly ref struct InlineParsingScope(InlineProcessor processor)
    {
        public void Dispose() => processor.EndInlineParsing();
    }
}
