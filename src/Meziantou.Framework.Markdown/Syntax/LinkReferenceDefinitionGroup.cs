// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.Framework.Markdown.Syntax;

/// <summary>
/// Contains all the <see cref="LinkReferenceDefinition"/> found in a document.
/// </summary>
/// <seealso cref="ContainerBlock" />
public class LinkReferenceDefinitionGroup : ContainerBlock
{
    private static readonly StringComparer UnicodeIgnoreCaseComparer = CultureInfo.InvariantCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkReferenceDefinitionGroup"/> class.
    /// </summary>
    public LinkReferenceDefinitionGroup() : base(null)
    {
        Links = new Dictionary<string, LinkReferenceDefinition>(UnicodeIgnoreCaseComparer);
    }

    /// <summary>
    /// Gets an association between a label and the corresponding <see cref="LinkReferenceDefinition"/>
    /// </summary>
    public Dictionary<string, LinkReferenceDefinition> Links { get; }

    // Definitions that stay children of this group while their parent is another block (see SetDetached)
    private HashSet<LinkReferenceDefinition>? _detachedLinks;

    /// <summary>
    /// Performs the set operation.
    /// </summary>
    public void Set(string label, LinkReferenceDefinition link)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (!IsChild(link))
        {
            Add(link);
            Links.TryAdd(label, link);
        }
    }

    /// <summary>
    /// Sets a definition that is also a block of the document, as the roundtrip parser does: the definition stays a child
    /// of this group, but it is detached from it so that it can be added to the document.
    /// </summary>
    internal void SetDetached(string label, LinkReferenceDefinition link)
    {
        Set(label, link);
        link.Parent = null;
        (_detachedLinks ??= new(ReferenceEqualityComparer.Instance)).Add(link);
    }

    // The children of this group have it as parent, except the detached ones, so this is the same as Contains(link)
    // without scanning all the definitions for a new one
    private bool IsChild(LinkReferenceDefinition link)
    {
        return link.Parent == this || (_detachedLinks is not null && _detachedLinks.Contains(link) && Contains(link));
    }

    /// <summary>
    /// Attempts to get.
    /// </summary>
    public bool TryGet(string label, [NotNullWhen(true)] out LinkReferenceDefinition? link)
    {
        return Links.TryGetValue(label, out link);
    }
}
