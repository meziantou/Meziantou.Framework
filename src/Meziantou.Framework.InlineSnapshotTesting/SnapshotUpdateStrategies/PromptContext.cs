namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed record PromptContext(string FilePath, string? ProcessName, int? ProcessId);
