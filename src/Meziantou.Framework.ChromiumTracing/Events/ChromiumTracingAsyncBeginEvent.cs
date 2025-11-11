using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents the beginning of an asynchronous operation. Must be paired with a <see cref="ChromiumTracingAsyncEndEvent"/> with the same Id.</summary>
/// <example>
/// <code>
/// await writer.WriteEventAsync(new ChromiumTracingAsyncBeginEvent
/// {
///     Name = "Async Task",
///     Category = "async",
///     Timestamp = DateTimeOffset.UtcNow,
///     Id = 123,
///     ProcessId = Environment.ProcessId
/// });
/// </code>
/// </example>
public sealed class ChromiumTracingAsyncBeginEvent : ChromiumTracingAsyncEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "b";
}
