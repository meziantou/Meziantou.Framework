namespace Meziantou.AspNetCore.Diagnostics;

/// <summary>Represents one middleware registration and its child branches.</summary>
public sealed class MiddlewarePipelineDebugMiddleware
{
    /// <summary>Gets the middleware name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the type declaring the delegate used to register the middleware, or <see langword="null"/> for a synthetic
    /// entry that does not correspond to a registered delegate.
    /// </summary>
    public string? DelegateType { get; init; }

    /// <summary>
    /// Gets the delegate method used to register the middleware, or <see langword="null"/> for a synthetic entry that
    /// does not correspond to a registered delegate.
    /// </summary>
    public string? DelegateMethod { get; init; }

    /// <summary>Gets the branch pipelines associated with this middleware.</summary>
    /// <remarks>
    /// <see cref="Microsoft.AspNetCore.Builder.IApplicationBuilder.New"/> gives no indication of which later
    /// registration owns the branch it creates, so branches are attributed to the next middleware registered. That is
    /// correct for <c>Map</c>, <c>MapWhen</c> and <c>UseWhen</c>, which register immediately after creating a branch.
    /// A branch created by hand and followed by an unrelated registration is attributed to that registration; one with
    /// no following registration is reported under a synthetic <c>(unattached branch)</c> entry.
    /// </remarks>
    public required IReadOnlyList<MiddlewarePipelineDebugPipeline> Branches { get; init; }
}
