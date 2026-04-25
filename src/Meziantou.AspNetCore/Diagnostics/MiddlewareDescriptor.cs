namespace Meziantou.AspNetCore.Diagnostics;

internal sealed class MiddlewareDescriptor
{
    public required string Name { get; init; }

    public required string DelegateType { get; init; }

    public required string DelegateMethod { get; init; }

    public List<MiddlewarePipelineDescriptor> Branches { get; } = [];
}
