// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Parsers.Inlines;

namespace Meziantou.Framework.Markdown.Syntax.Inlines;

/// <summary>
/// A delimiter used for parsing emphasis.
/// </summary>
/// <seealso cref="DelimiterInline" />
public class EmphasisDelimiterInline : DelimiterInline
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmphasisDelimiterInline" /> class.
    /// </summary>
    /// <param name="parser">The parser.</param>
    /// <param name="descriptor">The descriptor.</param>
    /// <param name="content">The source text of the delimiter run, used when the delimiter is rendered as a literal.</param>
    internal EmphasisDelimiterInline(InlineParser parser, EmphasisDescriptor descriptor, StringSlice content) : base(parser)
    {
        Descriptor = descriptor;
        DelimiterChar = descriptor.Character;
        Content = content;
    }

    /// <summary>
    /// Gets the descriptor for this emphasis.
    /// </summary>
    public EmphasisDescriptor Descriptor { get; }

    /// <summary>
    /// The delimiter character found.
    /// </summary>
    public char DelimiterChar { get; }

    /// <summary>
    /// The number of delimiter characters found for this delimiter.
    /// </summary>
    public int DelimiterCount { get; set; }

    // The length of the delimiter run in the source, used by the rule of 3. Unlike DelimiterCount, it does not change
    // as the delimiter is consumed.
    internal int RunLength { get; init; }

    /// <summary>
    /// The content as a <see cref="StringSlice"/>.
    /// </summary>
    public StringSlice Content;

    /// <summary>
    /// Performs the to literal operation.
    /// </summary>
    public override string ToLiteral()
    {
        if (DelimiterCount == 1)
        {
            return DelimiterChar switch
            {
                '*' => "*",
                '_' => "_",
                '~' => "~",
                '^' => "^",
                '+' => "+",
                '=' => "=",
                _ => DelimiterChar.ToString()
            };
        }

        return new string(DelimiterChar, DelimiterCount);
    }

    /// <summary>
    /// Performs the as literal inline operation.
    /// </summary>
    public LiteralInline AsLiteralInline()
    {
        return new LiteralInline()
        {
            Content = Content,
            IsClosed = true,
            Span = Span,
            Line = Line,
            Column = Column
        };
    }
}
