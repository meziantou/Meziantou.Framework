// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.DefinitionLists;

/// <summary>
/// A definition list contains <see cref="DefinitionItem"/> children.
/// </summary>
/// <seealso cref="ContainerBlock" />
public class DefinitionList : ContainerBlock
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DefinitionList"/> class.
    /// </summary>
    /// <param name="parser">The parser used to create this block.</param>
    public DefinitionList(BlockParser parser) : base(parser)
    {
    }
}