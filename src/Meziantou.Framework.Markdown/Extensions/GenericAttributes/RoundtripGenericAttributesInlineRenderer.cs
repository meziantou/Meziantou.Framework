using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Extensions.GenericAttributes;

/// <summary>
/// A roundtrip renderer for a <see cref="GenericAttributesInline"/>.
/// </summary>
internal sealed class RoundtripGenericAttributesInlineRenderer : RoundtripObjectRenderer<GenericAttributesInline>
{
    protected override void Write(RoundtripRenderer renderer, GenericAttributesInline obj)
    {
        var text = obj.SourceText.AsSpan();

        // The changed attributes of the object replace the first ones of the text, and the others are written as empty attributes.
        // Removing them could change the blocks: a setext heading whose content only has attributes would have none.
        if (obj.Target is { } target && renderer.GetChangedAttributes(target) is { } changedAttributes)
        {
            // The attributes of a table cell are the ones of the paragraph that the table is parsed from, which the table copies
            ContainerInline root = obj.Parent!;
            while (root.Parent is not null)
            {
                root = root.Parent;
            }

            for (Block? block = root.ParentBlock; block is not null; block = block.Parent)
            {
                if (block is Extensions.Tables.Table)
                {
                    AttributesTrivia.ThrowCannotWrite(target, "the attributes written in a table are the ones of the paragraph before it, which the table copies");
                }
            }

            // The text before them is the same, but it could read other attributes
            var attributes = AttributesTrivia.ReplaceAttributes(target, changedAttributes.Take(), text);
            var textBefore = renderer.GetLeafText();
            AttributesTrivia.CheckAttributesAfterText(target, textBefore, attributes, readsFirstBrace: false, checkBlocks: false);
            AttributesTrivia.CheckReplacedAttributes(target, textBefore, text.TrimEnd("\r\n"), attributes);
            renderer.WriteLines(attributes);
            text = text[text.TrimEnd("\r\n").Length..];
        }

        // The attributes can be followed by the line break that ends their line
        renderer.WriteLines(text);
    }
}
