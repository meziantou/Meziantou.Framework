namespace Meziantou.AspNetCore.Diagnostics;

/// <summary>Represents one middleware registration and its child branches.</summary>
public sealed record MiddlewarePipelineDebugMiddleware
{
    /// <summary>Gets the middleware name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the delegate type used to register the middleware.</summary>
    public required string DelegateType { get; init; }

    /// <summary>Gets the delegate method used to register the middleware.</summary>
    public required string DelegateMethod { get; init; }

    /// <summary>Gets the branch pipelines associated with this middleware.</summary>
    public required IReadOnlyList<MiddlewarePipelineDebugPipeline> Branches { get; init; }
}
