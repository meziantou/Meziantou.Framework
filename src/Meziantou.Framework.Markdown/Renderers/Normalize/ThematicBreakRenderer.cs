// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Normalize;

/// <summary>
/// A Normalize renderer for a <see cref="ThematicBreakBlock"/>.
/// </summary>
/// <seealso cref="NormalizeObjectRenderer{ThematicBreakBlock}" />
public class ThematicBreakRenderer : NormalizeObjectRenderer<ThematicBreakBlock>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(NormalizeRenderer renderer, ThematicBreakBlock obj)
    {
        // A list item marker written on the same line, with the same character, would be part of the thematic break
        var thematicChar = renderer.GetListMarkersBefore(obj).Contains(obj.ThematicChar, StringComparison.Ordinal) ? '_' : obj.ThematicChar;
        renderer.WriteLine(new string(thematicChar, obj.ThematicCharCount));

        renderer.FinishBlock(renderer.Options.EmptyLineAfterThematicBreak);
    }
}
