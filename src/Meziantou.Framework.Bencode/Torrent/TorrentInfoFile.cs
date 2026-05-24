namespace Meziantou.Framework.Bencode.Torrent;

public sealed class TorrentInfoFile
{
    public IReadOnlyList<string> Path { get; set; } = [];

    public long Length { get; set; }
}
