using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// An element of the structure tree of a tagged document (ISO 32000-2, 14.7.2): a paragraph, a heading, a figure, a
/// table... Its content is the marked content the content builder tags with it
/// (<see cref="PdfContentBuilder.BeginStructureElement"/>), its annotations and its child elements.
/// </summary>
public sealed class PdfStructureElement
{
    private static readonly PdfName AName = PdfName.Create("A");

    private static readonly PdfName AltName = PdfName.Create("Alt");
    private static readonly PdfName ActualTextName = PdfName.Create("ActualText");
    private static readonly PdfName LangName = PdfName.Create("Lang");
    private static readonly PdfName TitleName = PdfName.Create("T");
    private static readonly PdfName ExpansionName = PdfName.Create("E");

    internal PdfStructureElement(PdfStructureTree tree, PdfDictionary dictionary, PdfReference reference)
    {
        Tree = tree;
        Dictionary = dictionary;
        Reference = reference;
    }

    /// <summary>Gets the structure type (<c>/S</c>): a standard type such as <c>P</c>, <c>H1</c> or <c>Figure</c>, or a type of the role map.</summary>
    public string Type => Dictionary.GetName(PdfNames.S)?.Value ?? "";

    /// <summary>Gets the dictionary of the element.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets or sets the alternate description (<c>/Alt</c>): the text that replaces a figure or a formula for the readers that cannot show it.</summary>
    public string? AlternateDescription
    {
        get => Dictionary.GetText(AltName);
        set => SetText(AltName, value);
    }

    /// <summary>Gets or sets the replacement text (<c>/ActualText</c>): the text the content of the element shows, such as the letters of a drawn initial.</summary>
    public string? ActualText
    {
        get => Dictionary.GetText(ActualTextName);
        set => SetText(ActualTextName, value);
    }

    /// <summary>Gets or sets the language of the content of the element (<c>/Lang</c>), a BCP 47 tag such as <c>en-US</c>, when it is not the language of the document.</summary>
    public string? Language
    {
        get => Dictionary.GetText(LangName);
        set => SetText(LangName, value);
    }

    /// <summary>Gets or sets the title of the element (<c>/T</c>).</summary>
    public string? Title
    {
        get => Dictionary.GetText(TitleName);
        set => SetText(TitleName, value);
    }

    /// <summary>Gets or sets the expansion of an abbreviation or an acronym (<c>/E</c>).</summary>
    public string? Expansion
    {
        get => Dictionary.GetText(ExpansionName);
        set => SetText(ExpansionName, value);
    }

    /// <summary>Gets the child elements.</summary>
    public IReadOnlyList<PdfStructureElement> Children => [.. Tree.GetChildren(Dictionary)];

    internal PdfStructureTree Tree { get; }

    internal PdfReference Reference { get; }

    /// <summary>Adds a child element.</summary>
    /// <param name="type">The structure type: a standard type such as <c>P</c>, <c>H1</c>, <c>Figure</c> or <c>Table</c>, or a type of <see cref="PdfStructureTree.RoleMap"/>.</param>
    /// <returns>The element.</returns>
    public PdfStructureElement AddElement(string type) => Tree.CreateElement(this, type);

    /// <summary>Adds an annotation of a page to the content of the element (an object reference, 14.7.5.3): a link of the text of the element, for instance.</summary>
    /// <param name="annotation">The annotation.</param>
    /// <exception cref="ArgumentException">The annotation is not on a page of the document.</exception>
    public void AddAnnotation(PdfAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        Tree.AddAnnotation(this, annotation);
    }

    /// <summary>
    /// Gets an attribute of the element (<c>/A</c>, ISO 32000-2, 14.7.6): the value of an entry of the attribute object of
    /// an owner, such as the <c>Scope</c> of the <c>Table</c> owner.
    /// </summary>
    /// <param name="owner">The owner of the attribute object (<c>/O</c>): <c>Layout</c>, <c>List</c>, <c>PrintField</c>, <c>Table</c>, ...</param>
    /// <param name="name">The name of the attribute.</param>
    /// <returns>The value, or <see langword="null"/> when the element has no such attribute.</returns>
    public PdfObject? GetAttribute(string owner, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return FindAttributeObject(owner)?.GetResolved(PdfName.Create(name));
    }

    /// <summary>Sets an attribute of the element (<c>/A</c>, 14.7.6), in the attribute object of its owner, which is created when the element has none.</summary>
    /// <param name="owner">The owner of the attribute object (<c>/O</c>): <c>Layout</c>, <c>List</c>, <c>PrintField</c>, <c>Table</c>, ...</param>
    /// <param name="name">The name of the attribute, such as <c>Scope</c>, <c>RowSpan</c> or <c>BBox</c>.</param>
    /// <param name="value">The value, or <see langword="null"/> to remove the attribute.</param>
    public void SetAttribute(string owner, string name, PdfObject? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var attributes = FindAttributeObject(owner);
        if (attributes is null)
        {
            if (value is null)
                return;

            attributes = new PdfDictionary { [PdfName.Create("O")] = PdfName.Create(owner) };
            switch (Dictionary.GetResolved(AName))
            {
                case null:
                    Dictionary[AName] = attributes;
                    break;
                case PdfArray array:
                    array.Add(attributes);
                    break;
                case var existing:
                    Dictionary[AName] = new PdfArray(existing, attributes);
                    break;
            }
        }

        attributes[PdfName.Create(name)] = value;
    }

    /// <summary>Finds the attribute object of an owner (14.7.6.1): <c>/A</c> is an attribute object, or an array of attribute objects and revision numbers.</summary>
    private PdfDictionary? FindAttributeObject(string owner)
    {
        var ownerName = PdfName.Create("O");
        switch (Dictionary.GetResolved(AName))
        {
            case PdfDictionary single when single.GetName(ownerName)?.Value == owner:
                return single;
            case PdfArray array:
                for (var i = 0; i < array.Count && i < 256; i++)
                {
                    if (array.GetResolved(i) is PdfDictionary item && item.GetName(ownerName)?.Value == owner)
                        return item;
                }

                break;
        }

        return null;
    }

    internal void AddKid(PdfObject kid)
    {
        if (Dictionary[PdfNames.K] is not PdfArray kids)
        {
            kids = [];
            if (Dictionary[PdfNames.K] is { } existing)
            {
                kids.Add(existing);
            }

            Dictionary[PdfNames.K] = kids;
        }

        kids.Add(kid);
    }

    private void SetText(PdfName key, string? value) => Dictionary[key] = value is null ? null : PdfString.FromText(value);
}
