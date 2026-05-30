namespace Meziantou.Framework.Templating;

public sealed record class FileReference
{
    public FileReference(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    public string Path { get; }
}
