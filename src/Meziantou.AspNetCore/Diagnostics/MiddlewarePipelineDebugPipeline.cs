namespace Meziantou.AspNetCore.Diagnostics;

/// <summary>Represents a middleware pipeline.</summary>
public sealed record MiddlewarePipelineDebugPipeline
{
    /// <summary>Gets the middlewares registered in this pipeline.</summary>
    public required IReadOnlyList<MiddlewarePipelineDebugMiddleware> Middlewares { get; init; }
}
