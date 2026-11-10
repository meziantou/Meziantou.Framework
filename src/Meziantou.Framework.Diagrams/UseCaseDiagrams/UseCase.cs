namespace Meziantou.Framework.Diagrams;

/// <summary>A use case of a use case diagram: something the system does for its actors, drawn as an ellipse or a rectangle.</summary>
public sealed class UseCase : UseCaseDiagramElement
{
    /// <summary>Initializes a new instance of the <see cref="UseCase"/> class.</summary>
    /// <param name="id">The identifier, which is shown when there is no label.</param>
    /// <param name="label">The text shown, or <see langword="null"/> to show the identifier.</param>
    public UseCase(string id, DiagramText? label = null)
        : base(id)
    {
        Label = label;
    }

    /// <summary>Gets or sets the shape.</summary>
    public UseCaseShape Shape { get; set; }

    /// <summary>Gets or sets a value that indicates whether the use case is a business use case, drawn with a slash. A rectangle cannot be one.</summary>
    public bool IsBusiness { get; set; }

    /// <summary>Gets or sets the stereotype, shown between guillemets above the text, or <see langword="null"/> for none.</summary>
    public string? Stereotype { get; set; }
}
