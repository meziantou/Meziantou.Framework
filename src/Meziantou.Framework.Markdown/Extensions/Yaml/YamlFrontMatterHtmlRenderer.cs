// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;

namespace Meziantou.Framework.Markdown.Extensions.Yaml;

/// <summary>
/// Empty renderer for a <see cref="YamlFrontMatterBlock"/>
/// </summary>
/// <seealso cref="HtmlObjectRenderer{YamlFrontMatterBlock}" />
public class YamlFrontMatterHtmlRenderer : HtmlObjectRenderer<YamlFrontMatterBlock>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(HtmlRenderer renderer, YamlFrontMatterBlock obj)
    {
    }
}
