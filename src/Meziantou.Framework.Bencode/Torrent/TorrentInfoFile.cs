namespace Meziantou.Framework.Bencode.Torrent;

public sealed class TorrentInfoFile
{
    public IReadOnlyList<string> Path { get; set; } = [];

    public long Length { get; set; }

    /// <summary>The 'md5sum' of this file, when the torrent carries one.</summary>
    public string? Md5Sum { get; set; }
}
