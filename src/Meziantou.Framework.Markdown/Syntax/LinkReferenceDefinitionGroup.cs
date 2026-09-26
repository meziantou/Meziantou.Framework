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

    /// <summary>
    /// Performs the set operation.
    /// </summary>
    public void Set(string label, LinkReferenceDefinition link)
    {
        if (link is null) ThrowHelper.ArgumentNullException(nameof(link));

        // A block has a single parent, so this is the same as Contains(link) without scanning all the definitions
        if (link.Parent != this)
        {
            Add(link);
            Links.TryAdd(label, link);
        }
    }

    /// <summary>
    /// Attempts to get.
    /// </summary>
    public bool TryGet(string label, [NotNullWhen(true)] out LinkReferenceDefinition? link)
    {
        return Links.TryGetValue(label, out link);
    }
}
