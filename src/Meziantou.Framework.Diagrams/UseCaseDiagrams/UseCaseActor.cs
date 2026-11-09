namespace Meziantou.Framework.Diagrams;

/// <summary>An actor of a use case diagram: a person or a system that takes part in use cases, drawn as a figure with its name below.</summary>
public sealed class UseCaseActor : UseCaseDiagramElement
{
    /// <summary>Initializes a new instance of the <see cref="UseCaseActor"/> class.</summary>
    /// <param name="id">The identifier, which is shown when there is no label.</param>
    /// <param name="label">The name shown, or <see langword="null"/> to show the identifier.</param>
    public UseCaseActor(string id, DiagramText? label = null)
        : base(id)
    {
        Label = label;
    }

    /// <summary>Gets or sets how the figure is drawn.</summary>
    public UseCaseActorKind Kind { get; set; }

    /// <summary>Gets or sets a value that indicates whether the actor is a business actor, drawn with a slash across its head. An <see cref="UseCaseActorKind.Awesome"/> actor cannot be one.</summary>
    public bool IsBusiness { get; set; }

    /// <summary>Gets or sets the stereotype, shown between guillemets above the name, or <see langword="null"/> for none.</summary>
    public string? Stereotype { get; set; }
}
