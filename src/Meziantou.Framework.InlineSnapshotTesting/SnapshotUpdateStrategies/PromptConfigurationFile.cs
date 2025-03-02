using System.Text.Json;

namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed class PromptConfigurationFile : IDisposable
{
    public static string DefaultFilePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "meziantou.framework.inlinesnapshot", "settings.json");

    private Stream? _fileStream;

    public PromptConfigurationMode? DefaultMode { get; set; }

    public List<PromptConfigurationFileEntry>? Entries { get; set; }

    public void Dispose() => _fileStream?.Dispose();

    public void Save()
    {
        if (_fileStream is null)
            throw new InvalidOperationException();

        _fileStream.Seek(0, SeekOrigin.Begin);
        _fileStream.SetLength(0);
        JsonSerializer.Serialize(_fileStream, this);
    }

    public static PromptConfigurationFile LoadFromDefaultPath()
    {
        return LoadFromPath(DefaultFilePath);
    }

    public static PromptConfigurationFile LoadFromPath(string filePath)
    {
        var fs = OpenConfigurationFile(filePath);
        return LoadFromJsonStream(fs);
    }

    public static PromptConfigurationFile LoadFromJsonStream(Stream stream)
    {
        var data = Load(stream);
        data._fileStream = stream;
        return data;

        static PromptConfigurationFile Load(Stream stream)
        {
            try
            {
                return JsonSerializer.Deserialize<PromptConfigurationFile>(stream) ?? new PromptConfigurationFile();
            }
            catch
            {
                return new PromptConfigurationFile();
            }
        }
    }

    private static FileStream OpenConfigurationFile(string filePath)
    {
        while (true)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                return File.Open(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch
            {
                Thread.Sleep(15);
            }
        }
    }
}
