namespace Meziantou.Framework.Bencode.Torrent;

/// <summary>An entry of the BitTorrent v2 'piece layers' key (BEP 52): the piece hashes of a file.</summary>
public sealed class TorrentPieceLayer
{
    /// <summary>The 'pieces root' of the file the hashes belong to, 32 bytes.</summary>
    public ReadOnlyMemory<byte> PiecesRoot { get; set; }

    /// <summary>The SHA-256 hashes of the pieces of the file, 32 bytes each, concatenated.</summary>
    public ReadOnlyMemory<byte> Hashes { get; set; }
}
