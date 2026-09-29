// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.Footers;

/// <summary>
/// A block element for a footer.
/// </summary>
/// <seealso cref="ContainerBlock" />
public class FooterBlock : ContainerBlock, IQuoteLikeBlock
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FooterBlock"/> class.
    /// </summary>
    /// <param name="parser">The parser used to create this block.</param>
    public FooterBlock(BlockParser parser) : base(parser)
    {
    }

    /// <summary>
    /// Gets or sets the opening character used to match this footer (by default it is ^)
    /// </summary>
    public char OpeningCharacter { get; set; }

    // With trivia, the trivia of the marker of each line
    List<QuoteBlockLine> IQuoteLikeBlock.QuoteLines => GetOrSetDerivedTrivia<List<QuoteBlockLine>>();

    string IQuoteLikeBlock.Marker => OpeningCharacter == '^' ? "^^" : new string(OpeningCharacter, 2);
}