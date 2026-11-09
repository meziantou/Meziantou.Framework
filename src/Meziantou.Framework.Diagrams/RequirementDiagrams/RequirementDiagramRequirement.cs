namespace Meziantou.Framework.Diagrams;

/// <summary>A requirement of a requirement diagram: a box with its kind, its name, its identifier, its text, its risk and how it is verified.</summary>
public sealed class RequirementDiagramRequirement : RequirementDiagramNode
{
    /// <summary>Initializes a new instance of the <see cref="RequirementDiagramRequirement"/> class.</summary>
    /// <param name="name">The name, unique in the diagram.</param>
    public RequirementDiagramRequirement(string name)
        : base(name)
    {
    }

    /// <summary>Gets or sets the kind of the requirement, shown above its name.</summary>
    public RequirementKind Kind { get; set; }

    /// <summary>Gets or sets the identifier of the requirement in its specification, such as <c>1.2.1</c>, or <see langword="null"/> for none.</summary>
    public string? RequirementId { get; set; }

    /// <summary>Gets or sets the text of the requirement, or <see langword="null"/> for none.</summary>
    public DiagramText? Text { get; set; }

    /// <summary>Gets or sets the risk, or <see langword="null"/> for none.</summary>
    public RequirementRisk? Risk { get; set; }

    /// <summary>Gets or sets how the requirement is verified, or <see langword="null"/> for none.</summary>
    public RequirementVerificationMethod? VerificationMethod { get; set; }
}
