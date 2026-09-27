// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.AutoIdentifiers;

/// <summary>
/// A link reference definition to a <see cref="HeadingBlock"/> stored at the <see cref="MarkdownDocument"/> level.
/// </summary>
/// <seealso cref="LinkReferenceDefinition" />
public class HeadingLinkReferenceDefinition : LinkReferenceDefinition
{
    /// <summary>
    /// Initializes a new instance of the HeadingLinkReferenceDefinition class.
    /// </summary>
    public HeadingLinkReferenceDefinition(HeadingBlock headling)
    {
        Heading = headling;
        // Created implicitly, so it must not resolve inside another still-open
        // link bracket, e.g. [Some text [Heading]](url).
        AllowResolutionInsideOpenLink = false;
    }

    /// <summary>
    /// Gets or sets the heading related to this link reference definition.
    /// </summary>
    public HeadingBlock Heading { get; set; }

    // A reference gets "#" and the identifier of the heading as URL. The identifier is generated when the inlines of the
    // heading are processed, often after the reference, so until then the length of the heading is charged instead: the
    // identifier comes from its text, and escaping only makes it longer by a small factor.
    internal override long ExpansionLength => base.ExpansionLength + 1 + Math.Max(Heading.TryGetAttributes()?.Id?.Length ?? 0, Heading.Span.Length);
}
