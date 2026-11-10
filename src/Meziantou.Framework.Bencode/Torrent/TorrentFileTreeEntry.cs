namespace Meziantou.Framework.Bencode.Torrent;

/// <summary>A file of the BitTorrent v2 'file tree' (BEP 52).</summary>
public sealed class TorrentFileTreeEntry
{
    /// <summary>The segments of the path of the file in the file tree.</summary>
    public IReadOnlyList<string> Path { get; set; } = [];

    /// <summary>The size of the file, in bytes.</summary>
    public long Length { get; set; }

    /// <summary>The 'pieces root' key: the root of the SHA-256 merkle tree of the file, 32 bytes. Empty for an empty file.</summary>
    public ReadOnlyMemory<byte> PiecesRoot { get; set; }

    /// <summary>The keys of the file this model does not represent, such as 'attr' (BEP 47). They are written back as they are.</summary>
    public IDictionary<BencodeString, BencodeValue> AdditionalKeys { get; } = new Dictionary<BencodeString, BencodeValue>();
}
