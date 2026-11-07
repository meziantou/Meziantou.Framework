// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Extensions.Tables;

/// <summary>
/// The delimiter used to separate the columns of a pipe table.
/// </summary>
/// <seealso cref="DelimiterInline" />
public class PipeTableDelimiterInline : DelimiterInline
{
    /// <summary>
    /// Initializes a new instance of the PipeTableDelimiterInline class.
    /// </summary>
    public PipeTableDelimiterInline(InlineParser parser) : base(parser)
    {
    }

    /// <summary>
    /// Gets or sets the index of line where this delimiter was found relative to the current block.
    /// </summary>
    public int LocalLineIndex { get; set; }

    // The index of this pipe in the delimiters of the table being parsed, or -1 once it is removed from them
    internal int TableDelimiterIndex { get; set; } = -1;

    /// <summary>
    /// Performs the to literal operation.
    /// </summary>
    public override string ToLiteral()
    {
        return "|";
    }
}
