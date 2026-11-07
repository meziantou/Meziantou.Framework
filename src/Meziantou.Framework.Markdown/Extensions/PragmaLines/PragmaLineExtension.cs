// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Extensions.Footnotes;
using Meziantou.Framework.Markdown.Extensions.Yaml;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Extensions.PragmaLines;

/// <summary>
/// Extension to a span for each line containing the original line id (using id = pragma-line#line_number_zero_based)
/// </summary>
/// <seealso cref="IMarkdownExtension" />
public class PragmaLineExtension : IMarkdownExtension
{
    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        pipeline.DocumentProcessed -= PipelineOnDocumentProcessed;
        pipeline.DocumentProcessed += PipelineOnDocumentProcessed;
    }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }

    private static void PipelineOnDocumentProcessed(MarkdownDocument document)
    {
        // The id of a line must be unique, so it goes to the first block that starts on it: the outermost one, as the parents
        // are visited before their children
        var linesWithId = new HashSet<int>();
        for (var i = 0; i < document.Count; i++)
        {
            AddPragmas(document[i], ref i, linesWithId);
        }
    }

    private static void AddPragmas(Block block, ref int index, HashSet<int> linesWithId)
    {
        var attribute = block.GetAttributes();
        if (!linesWithId.Contains(block.Line))
        {
            var pragmaId = GetPragmaId(block);
            if (attribute.Id is null)
            {
                // The HTML renderers of some blocks do not write their attributes: the id goes to a block inside or after them
                if (RendersAttributes(block))
                {
                    attribute.Id = pragmaId;
                    linesWithId.Add(block.Line);
                }
            }
            else
            {
                linesWithId.Add(block.Line);

                var heading = block as HeadingBlock;

                // If we have a heading, we will try to add the tag inside it
                // otherwise we will add it just before
                var tag = $"<a id=\"{pragmaId}\"></a>";
                if (heading?.Inline?.FirstChild != null)
                {
                    heading.Inline.FirstChild.InsertBefore(new HtmlInline(tag));
                }
                else
                {
                    block.Parent!.Insert(index, new HtmlBlock(null) { Lines = new StringLineGroup(tag) });
                    index++;
                }
            }
        }

        if (block is ContainerBlock container)
        {
            for (int i = 0; i < container.Count; i++)
            {
                AddPragmas(container[i], ref i, linesWithId);
            }
        }
    }

    private static bool RendersAttributes(Block block) => block is not (BlankLineBlock or HtmlBlock or LinkReferenceDefinition or LinkReferenceDefinitionGroup or Footnote or FootnoteGroup or YamlFrontMatterBlock);

    private static string GetPragmaId(Block block)
    {
        return $"pragma-line-{block.Line}";
    }
}
