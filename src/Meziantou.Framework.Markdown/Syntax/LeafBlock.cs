// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Syntax;

/// <summary>
/// Base class for all leaf blocks.
/// </summary>
/// <seealso cref="Block" />
[DebuggerDisplay("{GetType().Name} Line: {Line}, {Lines}")]
public abstract class LeafBlock : Block
{
    private ContainerInline? _inline;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeafBlock"/> class.
    /// </summary>
    /// <param name="parser">The parser used to create this block.</param>
    protected LeafBlock(BlockParser? parser) : base(parser)
    {
        IsLeafBlock = true;
    }

    /// <summary>
    /// Gets or sets the string lines accumulated for this leaf block.
    /// May be null after process inlines have occurred.
    /// </summary>
    public StringLineGroup Lines;

    /// <summary>
    /// Gets or sets the inline syntax tree (may be null).
    /// </summary>
    public ContainerInline? Inline
    {
        get => _inline;
        set
        {
            if (value != null)
            {
                if (value.Parent != null)
                    ThrowHelper.ArgumentException("Cannot add this inline as it as already attached to another container (inline.Parent != null)");

                if (value.ParentBlock != null)
                    ThrowHelper.ArgumentException("Cannot add this inline as it as already attached to another container (inline.ParentBlock != null)");

                value.ParentBlock = this;
            }

            if (_inline != null)
            {
                _inline.ParentBlock = null;
            }

            _inline = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Lines"/> must be processed
    /// as inline into the <see cref="Inline"/> property.
    /// </summary>
    public bool ProcessInlines { get; set; }

    /// <summary>
    /// Appends the specified line to this instance.
    /// </summary>
    /// <param name="slice">The slice.</param>
    /// <param name="column">The column.</param>
    /// <param name="line">The line.</param>
    /// <param name="sourceLinePosition"></param>
    /// <param name="trackTrivia">Whether to keep track of trivia such as whitespace, extra heading characters and unescaped string values.</param>
    public void AppendLine(ref StringSlice slice, int column, int line, int sourceLinePosition, bool trackTrivia)
    {
        AppendLine(ref slice, column, line, sourceLinePosition, trackTrivia, isInPartiallyConsumedTab: slice.CurrentChar == '\t' && CharHelper.IsAcrossTab(column));
    }

    // isInPartiallyConsumedTab: the line starts with a tab of which some columns were consumed (by a container marker or an
    // indent), so only its remaining columns belong to the line. A tab that starts at the column of the line is kept.
    internal void AppendLine(ref StringSlice slice, int column, int line, int sourceLinePosition, bool trackTrivia, bool isInPartiallyConsumedTab)
    {
        if (Lines.Lines is null)
        {
            Lines = new StringLineGroup(4, ProcessInlines);
        }

        var stringLine = new StringLine(ref slice, line, column, sourceLinePosition, slice.NewLine);

        if (isInPartiallyConsumedTab && !trackTrivia)
        {
            // We need to expand tabs to spaces
            var builder = new ValueStringBuilder(unsafe(stackalloc char[ValueStringBuilder.StackallocThreshold]));
            builder.Append(' ', CharHelper.AddTab(column) - column);
            builder.Append(slice.AsSpan().Slice(1));
            stringLine.Slice = new StringSlice(builder.ToString());
        }

        Lines.Add(ref stringLine);
        NewLine = slice.NewLine; // update newline, as it should be the last newline of the block
    }
}