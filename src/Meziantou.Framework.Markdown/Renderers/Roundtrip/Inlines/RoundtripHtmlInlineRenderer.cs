// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Renderers.Roundtrip.Inlines;

/// <summary>
/// A Normalize renderer for a <see cref="HtmlInline"/>.
/// </summary>
public class RoundtripHtmlInlineRenderer : RoundtripObjectRenderer<HtmlInline>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(RoundtripRenderer renderer, HtmlInline obj)
    {
        // A tag can span lines, which start with the indent of the containers
        renderer.WriteLines(obj.Tag);
    }
}
