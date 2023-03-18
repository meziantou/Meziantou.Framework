using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meziantou.Framework.InlineSnapshotTesting.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;


internal sealed class PromptStrategy : SnapshotUpdateStrategy
{
    private static readonly string DefaultFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "meziantou.framework.inlinesnapshot", "settings.json");

    private readonly Prompt _prompt;

    public PromptStrategy(Prompt prompt)
    {
        _prompt = prompt;
    }

    internal string FilePath { get; set; } = DefaultFilePath;

    private SnapshotUpdateStrategy GetEffectiveStrategy(string path)
    {
        var context = PromptContext.Get(path);
        ConfigurationFile configuration;
        var folder = Path.GetDirectoryName(path);

        using var fs = OpenConfigurationFile();
        configuration = ConfigurationFile.LoadFromJsonStream(fs);
        if (configuration?.Entries != null)
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
        AppendEntry(fs, new ConfigurationFileEntry
        {
            Process = context.ParentProcessInfo,
            File = result.Scope == PromptConfigurationScope.CurrentFile ? path : null,
            Folder = result.Scope == PromptConfigurationScope.CurrentFolder ? folder : null,
            Mode = result.Mode,
            ExpirationDate = result.RememberPeriod == null && context.ParentProcessInfo != null ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.Add(result.RememberPeriod.Value),
        });
        return GetStrategy(result.Mode);
    }

    private FileStream OpenConfigurationFile()
    {
        var filePath = FilePath;
        while (true)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                return File.Open(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch
            {
                Thread.Sleep(15);
            }
        }
    }

    private static void AppendEntry(FileStream fs, ConfigurationFileEntry entry)
    {
        var configuration = ConfigurationFile.LoadFromJsonStream(fs);
        configuration.Entries ??= new List<ConfigurationFileEntry>();
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
        fs.Seek(0, SeekOrigin.Begin);
        fs.SetLength(0);
        JsonSerializer.Serialize(fs, configuration);
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

    public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path)
        => GetEffectiveStrategy(path).CanUpdateSnapshot(settings, path);

    public override bool MustReportError(InlineSnapshotSettings settings, string path)
        => GetEffectiveStrategy(path).MustReportError(settings, path);

    public override void UpdateFile(InlineSnapshotSettings settings, string targetFile, string tempFile)
        => GetEffectiveStrategy(targetFile).UpdateFile(settings, targetFile, tempFile);

    private sealed class ConfigurationFile
    {
        public List<ConfigurationFileEntry>? Entries { get; set; }

        public static ConfigurationFile LoadFromJsonStream(Stream stream)
        {
            try
            {
                return JsonSerializer.Deserialize<ConfigurationFile>(stream);
            }
            catch
            {
                return new ConfigurationFile();
            }
        }
    }

    private sealed class ConfigurationFileEntry
    {
        public ProcessInfo? Process { get; set; }
        public string? File { get; set; }
        public string? Folder { get; set; }
        public PromptConfigurationMode Mode { get; set; }
        public DateTimeOffset ExpirationDate { get; set; }

        [JsonIgnore]
        public bool IsExpired => ExpirationDate < DateTimeOffset.UtcNow;
    }
}