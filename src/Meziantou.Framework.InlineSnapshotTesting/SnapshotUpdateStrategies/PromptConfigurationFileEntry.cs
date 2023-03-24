using System.Text.Json.Serialization;

namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed class PromptConfigurationFileEntry
{
    public ProcessInfo? Process { get; set; }
    public string? File { get; set; }
    public string? Folder { get; set; }
    public PromptConfigurationMode Mode { get; set; }
    public DateTimeOffset ExpirationDate { get; set; }

    [JsonIgnore]
    public bool IsExpired => ExpirationDate < DateTimeOffset.UtcNow;
}
