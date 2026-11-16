namespace Meziantou.Framework.Pdf;

/// <summary>A change of a <see cref="PdfOptimizationPlan"/>. Set <see cref="IsEnabled"/> to <see langword="false"/> to leave it out when the plan is applied.</summary>
public sealed class PdfOptimizationAction
{
    private readonly Func<bool> _apply;

    internal PdfOptimizationAction(PdfOptimizationActionKind kind, string description, Func<bool> apply, int? pageIndex = null, int? objectNumber = null, string? resourceName = null)
    {
        Kind = kind;
        Description = description;
        PageIndex = pageIndex;
        ObjectNumber = objectNumber;
        ResourceName = resourceName;
        _apply = apply;
    }

    /// <summary>Gets the kind of change.</summary>
    public PdfOptimizationActionKind Kind { get; }

    /// <summary>Gets a description of the change, such as "Remove the unused font /F2 of the page 1".</summary>
    public string Description { get; }

    /// <summary>Gets the index of the page the change concerns, or <see langword="null"/> when it concerns the document.</summary>
    public int? PageIndex { get; }

    /// <summary>Gets the number of the indirect object the change concerns, or <see langword="null"/> when it is not an indirect object.</summary>
    public int? ObjectNumber { get; }

    /// <summary>Gets the name of the resource the change concerns, for <see cref="PdfOptimizationActionKind.PruneUnusedResource"/>.</summary>
    public string? ResourceName { get; }

    /// <summary>Gets or sets a value indicating whether the change is made when the plan is applied. The default is <see langword="true"/>.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <inheritdoc/>
    public override string ToString() => Description;

    /// <summary>Makes the change.</summary>
    /// <returns><see langword="true"/> when the document changed; <see langword="false"/> when the document no longer has what the action changes.</returns>
    internal bool Apply() => _apply();
}
