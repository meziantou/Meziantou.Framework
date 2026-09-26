// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.Yaml;

/// <summary>
/// A YAML frontmatter block.
/// </summary>
/// <seealso cref="CodeBlock" />
public class YamlFrontMatterBlock : CodeBlock
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlFrontMatterBlock"/> class.
    /// </summary>
    /// <param name="parser">The parser.</param>
    public YamlFrontMatterBlock(BlockParser parser) : base(parser)
    {
    }
}