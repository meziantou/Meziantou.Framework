// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Globalization;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Normalize;

/// <summary>
/// A Normalize renderer for a <see cref="ListBlock"/>.
/// </summary>
/// <seealso cref="NormalizeObjectRenderer{ListBlock}" />
public class ListRenderer : NormalizeObjectRenderer<ListBlock>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(NormalizeRenderer renderer, ListBlock listBlock)
    {
        renderer.EnsureLine();
        var compact = renderer.CompactParagraph;
        renderer.CompactParagraph = !listBlock.IsLoose;
        for (var i = 0; i < listBlock.Count; i++)
        {
            var item = listBlock[i];
            var listItem = (ListItemBlock) item;
            renderer.EnsureLine();
            renderer.PushHangingIndent(GetMarker(renderer, listBlock, i));
            if (listItem.Count == 0)
            {
                renderer.Write(""); // trigger writing of indent
            }
            else
            {
                renderer.WriteChildren(listItem);
            }
            renderer.PopIndent();
            if (i + 1 < listBlock.Count && listBlock.IsLoose)
            {
                renderer.EnsureLine();
                renderer.WriteLine();
            }
        }
        renderer.CompactParagraph = compact;

        renderer.FinishBlock(true);
    }

    // Gets the marker, including the following space, of the item at the specified index
    internal static string GetMarker(NormalizeRenderer renderer, ListBlock listBlock, int index)
    {
        if (!listBlock.IsOrdered)
        {
            return $"{renderer.Options.ListItemCharacter ?? listBlock.BulletType} ";
        }

        var number = 0;
        if (listBlock.BulletType == '1')
        {
            if (listBlock.OrderedStart != null)
            {
                _ = int.TryParse(listBlock.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
            }

            number += index;
        }

        return $"{number.ToString(CultureInfo.InvariantCulture)}{listBlock.OrderedDelimiter} ";
    }
}
