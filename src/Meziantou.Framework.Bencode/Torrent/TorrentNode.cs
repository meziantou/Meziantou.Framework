namespace Meziantou.Framework.Bencode.Torrent;

/// <summary>A DHT bootstrap node from the 'nodes' key (BEP 5).</summary>
public sealed class TorrentNode
{
    public string Host { get; set; } = "";

    public int Port { get; set; }
}
