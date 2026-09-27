// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Extensions.Footnotes;

/// <summary>
/// A inline link to a <see cref="Footnote"/>.
/// </summary>
/// <seealso cref="Inline" />
public class FootnoteLink : Inline
{
    /// <summary>
    /// Initializes a new instance of the FootnoteLink class.
    /// </summary>
    public FootnoteLink(Footnote footnote)
    {
        Footnote = footnote;
    }

    /// <summary>
    /// Gets or sets a value indicating whether this instance is back link (from a footnote to the link)
    /// </summary>
    public bool IsBackLink { get; set; }

    /// <summary>
    /// Gets or sets the global index number of this link.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the footnote this link refers to.
    /// </summary>
    public Footnote Footnote { get; set; }

    // With trivia, the label as written between the brackets of the reference, when it is known
    internal StringSlice LabelWithTrivia { get; set; }
}
