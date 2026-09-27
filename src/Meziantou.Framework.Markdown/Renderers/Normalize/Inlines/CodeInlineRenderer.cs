// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Renderers.Normalize.Inlines;

/// <summary>
/// A Normalize renderer for a <see cref="CodeInline"/>.
/// </summary>
/// <seealso cref="NormalizeObjectRenderer{CodeInline}" />
public class CodeInlineRenderer : NormalizeObjectRenderer<CodeInline>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(NormalizeRenderer renderer, CodeInline obj)
    {
        var delimiter = obj.Delimiter == '\0' ? '`' : obj.Delimiter;
        string content = renderer.EscapeTablePipes ? obj.Content.Replace("|", "\\|", StringComparison.Ordinal) : obj.Content;

        var longestRun = 0;
        var hasRunOfOriginalCount = false;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] != delimiter)
            {
                continue;
            }

            var count = 1;
            while (i + 1 < content.Length && content[i + 1] == delimiter)
            {
                count++;
                i++;
            }

            longestRun = Math.Max(longestRun, count);
            hasRunOfOriginalCount |= count == obj.DelimiterCount;
        }

        // A literal backtick run of the same length would pair with a shorter fence, so keep the original fence in that case
        var delimiterCount = longestRun + 1;
        if (delimiterCount != obj.DelimiterCount && obj.DelimiterCount > 0 && !hasRunOfOriginalCount && HasLiteralDelimiter(renderer, obj, delimiter))
        {
            delimiterCount = obj.DelimiterCount;
        }

        // The parser strips one space on each side when both are present, and a delimiter next to the fence would lengthen it
        var pad = content.Length == 0
            || content[0] == delimiter
            || content[^1] == delimiter
            || (content[0] == ' ' && content[^1] == ' ' && content.AsSpan().ContainsAnyExcept(' '));

        renderer.Write(delimiter, delimiterCount);
        if (pad)
        {
            renderer.Write(' ');
        }

        renderer.Write(content);
        if (pad && content.Length != 0)
        {
            renderer.Write(' ');
        }

        renderer.Write(delimiter, delimiterCount);
    }

    private static bool HasLiteralDelimiter(NormalizeRenderer renderer, CodeInline obj, char delimiter)
    {
        var root = obj.Parent;
        while (root?.Parent is not null)
        {
            root = root.Parent;
        }

        if (root is null)
        {
            return false;
        }

        // Computed once for all the code spans of the tree
        if (renderer.LiteralDelimiterCache is { } cache && cache.Root == root && cache.Delimiter == delimiter)
        {
            return cache.Result;
        }

        var result = false;
        foreach (var literal in root.FindDescendants<LiteralInline>())
        {
            // The text of an expanded autolink is written with entities for its backticks
            if (literal.Content.AsSpan().Contains(delimiter) && !(literal.Parent is LinkInline { IsAutoLink: true } link && LinkInlineRenderer.IsExpandedAutoLink(link)))
            {
                result = true;
                break;
            }
        }

        renderer.LiteralDelimiterCache = (root, delimiter, result);
        return result;
    }
}
