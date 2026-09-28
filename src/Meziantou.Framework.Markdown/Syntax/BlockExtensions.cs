// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

namespace Meziantou.Framework.Markdown.Syntax;

/// <summary>
/// Extensions for <see cref="Block"/>
/// </summary>
public static class BlockExtensions
{
    /// <summary>
    /// Finds the deepest block that contains a position, as <see cref="ContainsPosition"/> defines it.
    /// </summary>
    /// <returns>The deepest block containing the position, or <c>null</c> when <paramref name="rootBlock"/> does not contain it.</returns>
    public static Block? FindBlockAtPosition(this Block rootBlock, int position)
    {
        var contains = rootBlock.CompareToPosition(position) == 0;
        if (!(rootBlock is ContainerBlock blocks) || blocks.Count == 0 || !contains)
        {
            return contains ? rootBlock : null;
        }

        var lowerIndex = 0;
        var upperIndex = blocks.Count - 1;

        // binary search on lines
        Block? block = null;
        while (lowerIndex <= upperIndex)
        {
            int midIndex = (upperIndex - lowerIndex) / 2 + lowerIndex;
            block = blocks[midIndex];
            int comparison = block.CompareToPosition(position);
            if (comparison == 0)
            {
                break;
            }

            block = null;
            if (comparison < 0)
                lowerIndex = midIndex + 1;
            else
                upperIndex = midIndex - 1;
        }

        if (block is null)
        {
            return rootBlock;
        }

        // Recursively go deep into the block
        return FindBlockAtPosition(block, position);
    }

    /// <summary>
    /// Performs the find closest line operation.
    /// </summary>
    public static int FindClosestLine(this MarkdownDocument root, int line)
    {
        var closestBlock = root.FindClosestBlock(line);
        return closestBlock?.Line ?? 0;
    }

    /// <summary>
    /// Performs the find closest block operation.
    /// </summary>
    public static Block? FindClosestBlock(this Block rootBlock, int line)
    {
        if (!(rootBlock is ContainerBlock blocks) || blocks.Count == 0)
        {
            return rootBlock.Line == line ? rootBlock : null;
        }

        var lowerIndex = 0;
        var upperIndex = blocks.Count - 1;

        // binary search on lines
        while (lowerIndex <= upperIndex)
        {
            int midIndex = (upperIndex - lowerIndex) / 2 + lowerIndex;
            var block = blocks[midIndex];
            int comparison = block.Line.CompareTo(line);
            if (comparison == 0)
            {
                return block;
            }
            if (comparison < 0)
                lowerIndex = midIndex + 1;
            else
                upperIndex = midIndex - 1;
        }

        // If we are between two lines, try to find the best spot
        if (lowerIndex > 0 && lowerIndex < blocks.Count)
        {
            var prevBlock = blocks[lowerIndex - 1].FindClosestBlock(line) ?? blocks[lowerIndex - 1];
            var nextBlock = blocks[lowerIndex].FindClosestBlock(line) ?? blocks[lowerIndex];

            if (prevBlock.Line == line)
            {
                return prevBlock;
            }

            if (nextBlock.Line == line)
            {
                return nextBlock;
            }

            // we calculate the position of the current line relative to the line found and previous line
            var prevLine = prevBlock.Line;
            var nextLine = nextBlock.Line;

            var middle = (line - prevLine) * 1.0 / (nextLine - prevLine);
            // If  relative position < 0.5, we select the previous line, otherwise we select the line found
            return middle < 0.5 ? prevBlock : nextBlock;
        }

        if (lowerIndex == 0)
        {
            var prevBlock = blocks[lowerIndex].FindClosestBlock(line) ?? blocks[lowerIndex];
            return prevBlock;
        }

        if (lowerIndex == blocks.Count)
        {
            var prevBlock = blocks[lowerIndex - 1].FindClosestBlock(line) ?? blocks[lowerIndex - 1];
            return prevBlock;
        }

        return null;
    }


    /// <summary>
    /// Determines whether a position is in a block: from its first character to the position just after its last character,
    /// where a caret at the end of the block is.
    /// </summary>
    public static bool ContainsPosition(this Block block, int position)
    {
        return CompareToPosition(block, position) == 0;
    }

    /// <summary>
    /// Compares a block to a position.
    /// </summary>
    /// <returns>1 when the block is after the position, -1 when it is before, and 0 when it contains it (see <see cref="ContainsPosition"/>).</returns>
    public static int CompareToPosition(this Block block, int position)
    {
        return position < block.Span.Start ? 1 : position > block.Span.End + 1 ? -1 : 0;
    }
}
