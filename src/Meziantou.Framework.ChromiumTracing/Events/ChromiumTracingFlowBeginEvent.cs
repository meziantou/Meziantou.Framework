using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents the beginning of a flow event.</summary>
/// <example>
/// <code>
/// await writer.WriteEventAsync(new ChromiumTracingFlowBeginEvent
/// {
///     Name = "Request Flow",
///     Category = "network",
///     Timestamp = DateTimeOffset.UtcNow,
///     ProcessId = Environment.ProcessId
/// });
/// </code>
/// </example>
public sealed class ChromiumTracingFlowBeginEvent : ChromiumTracingFlowEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "s";
}
