namespace Meziantou.Framework.Bencode.Torrent;

public sealed class TorrentInfoFile
{
    public IReadOnlyList<string> Path { get; set; } = [];

    public long Length { get; set; }

    /// <summary>The 'md5sum' of this file, when the torrent carries one.</summary>
    public string? Md5Sum { get; set; }

    /// <summary>The keys of the file this model does not represent, such as 'attr' (BEP 47). They are written back as they are.</summary>
    public IDictionary<BencodeString, BencodeValue> AdditionalKeys { get; } = new Dictionary<BencodeString, BencodeValue>();
}
