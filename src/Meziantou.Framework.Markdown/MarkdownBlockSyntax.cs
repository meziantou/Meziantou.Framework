using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Markdown;

/// <summary>A block of a document: a paragraph, a heading, a list, a block quote, a code block...</summary>
public abstract class MarkdownBlockSyntax : MarkdownSyntaxNode
{
    private protected MarkdownBlockSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the generic attributes of the block, which the HTML renderer writes on its element.</summary>
    /// <returns>The attributes, or <see cref="MarkdownGenericAttributes.Empty"/> when the block has none.</returns>
    /// <remarks>
    /// The attributes are read with <see cref="MarkdownExtensions.GenericAttributes"/>, from every <c>{...}</c> that is for the
    /// block: the ones at the end of the first line of a heading or of a fenced block, at the end of a paragraph, or on the line
    /// before the block. As in the HTML renderer, the id of the last ones wins, the classes are added, and a property is kept
    /// from the first attributes that have it. Change them with
    /// <see cref="MarkdownSyntaxTree.WithGenericAttributes(MarkdownBlockSyntax, MarkdownGenericAttributes?)"/>.
    /// </remarks>
    public MarkdownGenericAttributes GetGenericAttributes()
    {
        if (FirstAncestorOrSelf<MarkdownDocumentSyntax>() is not { HasGenericAttributes: true } document)
            return MarkdownGenericAttributes.Empty;

        var found = new List<MarkdownGenericAttributesSyntax>();
        _ = MarkdownGenericAttributesSyntax.FindTargets(document, (attributes, target) =>
        {
            if (target == this)
            {
                found.Add(attributes);
            }
        });

        return MergeGenericAttributes(found);
    }

    // The attributes of a block, from the {...} that are for it, in the order of the text
    internal static MarkdownGenericAttributes MergeGenericAttributes(List<MarkdownGenericAttributesSyntax> found)
    {
        string? id = null;
        List<string>? blockLineClasses = null;
        var classes = new List<string>();
        var properties = new List<KeyValuePair<string, string?>>();
        foreach (var attributes in found)
        {
            id = attributes.Id ?? id;
            if (attributes.IsBlockLine)
            {
                blockLineClasses = [.. attributes.Classes];
            }
            else
            {
                classes.AddRange(attributes.Classes);
            }

            var existingCount = properties.Count;
            foreach (var property in attributes.Properties)
            {
                if (properties.FindIndex(0, existingCount, existing => string.Equals(existing.Key, property.Key, StringComparison.Ordinal)) < 0)
                {
                    properties.Add(property);
                }
            }
        }

        // The classes of the first line of a block come first, as the renderer writes them
        if (blockLineClasses is not null)
        {
            classes.InsertRange(0, blockLineClasses);
        }

        return id is null && classes.Count == 0 && properties.Count == 0 ? MarkdownGenericAttributes.Empty : new MarkdownGenericAttributes(id, classes, properties);
    }
}
