namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>The content of the headers of an archive.</summary>
internal sealed class SevenZipDatabase
{
    public List<SevenZipFolder> Folders { get; } = [];

    public List<SevenZipFileItem> Files { get; } = [];
}
