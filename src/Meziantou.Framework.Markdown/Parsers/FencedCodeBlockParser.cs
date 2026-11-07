// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Parsers;

/// <summary>
/// Parser for a <see cref="FencedCodeBlock"/>.
/// </summary>
/// <seealso cref="BlockParser" />
public class FencedCodeBlockParser : FencedBlockParserBase<FencedCodeBlock>
{
    /// <summary>
    /// Represents the default info prefix.
    /// </summary>
    public const string DefaultInfoPrefix = "language-";

    /// <summary>
    /// Initializes a new instance of the <see cref="FencedCodeBlockParser"/> class.
    /// </summary>
    public FencedCodeBlockParser()
    {
        OpeningCharacters = ['`', '~'];
        InfoPrefix = DefaultInfoPrefix;
    }

    /// <summary>
    /// Performs the create fenced block operation.
    /// </summary>
    protected override FencedCodeBlock CreateFencedBlock(BlockProcessor processor)
    {
        var codeBlock = new FencedCodeBlock(this)
        {
            IndentCount = processor.Indent,
        };

        if (processor.TrackTrivia)
        {
            codeBlock.LinesBefore = processor.TakeLinesBefore();
            codeBlock.TriviaBefore = processor.UseTrivia(processor.Start - 1);
            codeBlock.NewLine = processor.Line.NewLine;
        }

        return codeBlock;
    }

    /// <summary>
    /// Attempts to continue parsing the specified block.
    /// </summary>
    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        var result = base.TryContinue(processor, block);
        if (result == BlockState.Continue && !processor.TrackTrivia)
        {
            var fence = (FencedCodeBlock)block;
            // Remove as many columns of indentation as the opening fence had. A tab counts for its columns only, so it can
            // be partially removed.
            var indentCount = fence.IndentCount;
            while (indentCount > 0 && processor.CurrentChar.IsSpaceOrTab())
            {
                indentCount--;
                processor.NextColumn();
            }
        }

        return result;
    }
}
