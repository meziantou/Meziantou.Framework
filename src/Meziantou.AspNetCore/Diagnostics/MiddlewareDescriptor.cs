namespace Meziantou.AspNetCore.Diagnostics;

internal sealed class MiddlewareDescriptor
{
    public required string Name { get; init; }

    /// <summary>Null for synthetic entries that do not correspond to a registered delegate.</summary>
    public string? DelegateType { get; init; }

    /// <summary>Null for synthetic entries that do not correspond to a registered delegate.</summary>
    public string? DelegateMethod { get; init; }

    public List<MiddlewarePipelineDescriptor> Branches { get; } = [];
}
