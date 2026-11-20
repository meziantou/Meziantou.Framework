using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Layout;

/// <summary>A node of the structure tree of a flow document, created by the layout and written as a structure element.</summary>
internal sealed class StructNode(string type, StructNode? parent)
{
    public string Type { get; } = type;

    public StructNode? Parent { get; } = parent;

    public List<StructNode> Children { get; } = [];

    public string? AlternateDescription { get; set; }

    public List<(string Owner, string Name, PdfObject Value)> Attributes { get; } = [];

    public PdfStructureElement? Element { get; set; }

    public StructNode Add(string childType)
    {
        var child = new StructNode(childType, this);
        Children.Add(child);
        return child;
    }
}
