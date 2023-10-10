using System.Diagnostics;

namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;


internal sealed class PromptStrategy : SnapshotUpdateStrategy
{
    private readonly Prompt _prompt;

    public PromptStrategy(Prompt prompt)
    {
        _prompt = prompt;
    }

    internal string FilePath { get; set; } = PromptConfigurationFile.DefaultFilePath;

    private SnapshotUpdateStrategy GetEffectiveStrategy(string path)
    {
        _prompt.OnSnapshotChanged();

        var context = PromptContext.Get(path);
        var folder = Path.GetDirectoryName(path);

        using var configuration = PromptConfigurationFile.LoadFromPath(FilePath);
        if (configuration.DefaultMode != null)
            return GetStrategy(configuration.DefaultMode.Value);

        if (configuration.Entries != null)
        {
            foreach (var entry in configuration.Entries)
            {
                if (entry.IsExpired)
                    continue;

                if (entry.File != null && entry.File != path)
                    continue;

                if (entry.Folder != null && entry.Folder != folder)
                    continue;

                if (entry.Process != null && (context.ParentProcessInfo == null || entry.Process != context.ParentProcessInfo))
                    continue;

                return GetStrategy(entry.Mode);
            }
        }

        var result = _prompt.Ask(context);
        AppendEntry(configuration, new PromptConfigurationFileEntry
        {
            Process = context.ParentProcessInfo,
            File = result.Scope == PromptConfigurationScope.CurrentFile ? path : null,
            Folder = result.Scope == PromptConfigurationScope.CurrentFolder ? folder : null,
            Mode = result.Mode,
            ExpirationDate = (result.RememberPeriod, context.ParentProcessInfo) switch
            {
                (null, not null) => DateTimeOffset.MaxValue,
                (not null, _) => DateTimeOffset.UtcNow.Add(result.RememberPeriod.Value),
                _ => DateTimeOffset.UtcNow,
            },
        });
        return GetStrategy(result.Mode);
    }

    private static void AppendEntry(PromptConfigurationFile configuration, PromptConfigurationFileEntry entry)
    {
        configuration.Entries ??= [];
        configuration.Entries.RemoveAll(entry => entry.IsExpired);
        if (configuration.Entries.Count > 100)
        {
            // Advanced cleanup
            configuration.Entries.RemoveAll(entry =>
            {
                if (entry.Process != null)
                {
                    try
                    {
                        Process.GetProcessById(entry.Process.ProcessId);
                    }
                    catch
                    {
                        return true;
                    }
                }

                return false;
            });
        }

        configuration.Entries.Insert(0, entry);

        // Overwrite the file
        configuration.Save();
    }

    private static SnapshotUpdateStrategy GetStrategy(PromptConfigurationMode mode)
    {
        return mode switch
        {
            PromptConfigurationMode.Disallow => Disallow,
            PromptConfigurationMode.MergeTool => MergeTool,
            PromptConfigurationMode.Overwrite => Overwrite,
            PromptConfigurationMode.OverwriteWithoutFailure => OverwriteWithoutFailure,
            _ => MergeTool,
        };
    }

    public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string expectSnapshot, string actualSnapshot)
        => GetEffectiveStrategy(path).CanUpdateSnapshot(settings, path, expectSnapshot, actualSnapshot);

    public override bool MustReportError(InlineSnapshotSettings settings, string path)
        => GetEffectiveStrategy(path).MustReportError(settings, path);

    public override void UpdateFile(InlineSnapshotSettings settings, string targetFile, string tempFile)
        => GetEffectiveStrategy(targetFile).UpdateFile(settings, targetFile, tempFile);
}
