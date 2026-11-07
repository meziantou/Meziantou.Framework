namespace Meziantou.Framework.Json;

/// <summary>Controls cancellation and resource limits for one JSONPath evaluation.</summary>
/// <remarks>Limits throw rather than return incomplete results. Options are read once at the start of evaluation.</remarks>
public sealed class JsonPathEvaluationOptions
{
    /// <summary>Gets or sets the token checked cooperatively during evaluation.</summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>Gets or sets the maximum number of evaluator checkpoints and navigator operations, or <see langword="null"/> for no limit.</summary>
    /// <remarks>This is a work counter, not a time limit. One custom navigator operation cannot be interrupted.</remarks>
    public long? MaximumWork { get; set; }

    /// <summary>Gets or sets the maximum number of final matches, including duplicates, or <see langword="null"/> for no limit.</summary>
    public int? MaximumResults { get; set; }

    /// <summary>Gets or sets the maximum number of nodes produced by any intermediate or filter-query segment, or <see langword="null"/> for no limit.</summary>
    public int? MaximumIntermediateResults { get; set; }
}
