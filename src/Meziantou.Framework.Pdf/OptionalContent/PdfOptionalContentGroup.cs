using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// An optional content group (ISO 32000-2, 8.11.2): a layer, which viewers let the user show or hide. The content of a
/// group is drawn between <see cref="PdfContentBuilder.BeginOptionalContent(PdfOptionalContentGroup)"/> and
/// <see cref="PdfContentBuilder.EndMarkedContent"/>; an annotation belongs to one with <see cref="PdfAnnotation.OptionalContent"/>.
/// </summary>
public sealed class PdfOptionalContentGroup
{
    private static readonly PdfName IntentName = PdfName.Create("Intent");

    internal PdfOptionalContentGroup(PdfDocument document, PdfDictionary dictionary, PdfReference reference)
    {
        Document = document;
        Dictionary = dictionary;
        Reference = reference;
    }

    /// <summary>Gets the group dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets the reference to the group.</summary>
    public PdfReference Reference { get; }

    /// <summary>Gets or sets the name of the group (<c>/Name</c>), which viewers show in their list of layers.</summary>
    public string Name
    {
        get => Dictionary.GetText(PdfNames.Name) ?? "";
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Dictionary.SetText(PdfNames.Name, value);
        }
    }

    /// <summary>Gets the intents of the group (<c>/Intent</c>): <c>View</c>, the default, <c>Design</c>, or names of applications.</summary>
    public IReadOnlyList<string> Intents => Dictionary.GetResolved(IntentName) switch
    {
        PdfName name => [name.Value],
        PdfArray array => [.. array.Take(64).Select(item => (PdfReference.ResolveIfReference(item) as PdfName)?.Value).OfType<string>()],
        _ => ["View"],
    };

    /// <summary>Gets or sets a value indicating whether the group is visible when the document is opened: its state in the default configuration (<see cref="PdfOptionalContentProperties.DefaultConfiguration"/>).</summary>
    public bool IsVisible
    {
        get => Document.OptionalContent.DefaultConfiguration?.IsVisible(this) ?? true;
        set => Document.OptionalContent.GetOrCreateDefaultConfiguration().SetVisible(this, value);
    }

    internal PdfDocument Document { get; }

    /// <inheritdoc/>
    public override bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] object? obj) => obj is PdfOptionalContentGroup other && ReferenceEquals(other.Dictionary, Dictionary);

    /// <inheritdoc/>
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Dictionary);
}
