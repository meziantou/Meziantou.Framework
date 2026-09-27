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
    /// <exception cref="ArgumentNullException"></exception>
    [SuppressMessage("Design", "MA0056:Do not call overridable members in constructor", Justification = "Kept for compatibility with Markdig")]
    public EmphasisDelimiterInline(InlineParser parser, EmphasisDescriptor descriptor) : base(parser)
    {
        if (descriptor is null)
            ThrowHelper.ArgumentNullException(nameof(descriptor));

        Descriptor = descriptor;
        DelimiterChar = descriptor.Character;
        Content = new StringSlice(ToLiteral());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EmphasisDelimiterInline" /> class.
    /// </summary>
    /// <param name="parser">The parser.</param>
    /// <param name="descriptor">The descriptor.</param>
    /// <param name="content">The content.</param>
    /// <exception cref="ArgumentNullException"></exception>
    internal EmphasisDelimiterInline(InlineParser parser, EmphasisDescriptor descriptor, StringSlice content) : base(parser)
    {
        if (descriptor is null)
            ThrowHelper.ArgumentNullException(nameof(descriptor));

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

    // The length of the delimiter run in the source, used by the rule of 3. It is unknown for a delimiter created by
    // another parser, which then only gives its DelimiterCount.
    internal int RunLength
    {
        get => field > 0 ? field : DelimiterCount;
        set;
    }

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
