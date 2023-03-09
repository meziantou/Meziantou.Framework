namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed record PromptResult(PromptConfigurationMode Mode, TimeSpan? RememberPeriod, bool ApplyToAllFiles);
