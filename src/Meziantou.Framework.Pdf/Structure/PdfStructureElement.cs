using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// An element of the structure tree of a tagged document (ISO 32000-2, 14.7.2): a paragraph, a heading, a figure, a
/// table... Its content is the marked content the content builder tags with it
/// (<see cref="PdfContentBuilder.BeginStructureElement"/>), its annotations and its child elements.
/// </summary>
public sealed class PdfStructureElement
{
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
