namespace Meziantou.Framework.OpenTelemetryCollector.InMemory;

/// <summary>Configures how many received items <see cref="InMemoryOpenTelemetryHandler"/> keeps.</summary>
/// <remarks>
/// Every limit defaults to <see cref="int.MaxValue"/>, which means nothing is ever evicted and each received export
/// request is retained in full. That default suits a test that asserts on everything an application exported, but a
/// long running process must set explicit limits: once a limit is set, the oldest items are overwritten.
/// </remarks>
public sealed class InMemoryOpenTelemetryHandlerOptions
{
    /// <summary>Gets or sets the number of log export requests to keep. Defaults to <see cref="int.MaxValue"/>, which keeps all of them.</summary>
    public int MaximumLogCount { get; set; } = int.MaxValue;

    /// <summary>Gets or sets the number of trace export requests to keep. Defaults to <see cref="int.MaxValue"/>, which keeps all of them.</summary>
    public int MaximumTraceCount { get; set; } = int.MaxValue;

    /// <summary>Gets or sets the number of metric export requests to keep. Defaults to <see cref="int.MaxValue"/>, which keeps all of them.</summary>
    public int MaximumMetricCount { get; set; } = int.MaxValue;
}
