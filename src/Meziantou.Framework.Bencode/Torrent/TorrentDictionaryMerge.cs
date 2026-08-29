namespace Meziantou.Framework.Bencode.Torrent;

internal static class TorrentDictionaryMerge
{
    /// <summary>Rebuilds a metainfo dictionary from the model while carrying over the keys this model does not represent.</summary>
    /// <param name="source">The dictionary the torrent was parsed from, or <see langword="null"/> when it was constructed in memory.</param>
    /// <param name="modelled">The entries produced from the model, in the order they should be written.</param>
    /// <param name="modelledKeys">Every key the model owns, including the ones <paramref name="modelled"/> currently omits.</param>
    /// <remarks>
    /// Torrents routinely carry keys outside BEP 3 ('source', 'md5sum', 'url-list', 'nodes', the BitTorrent v2 keys).
    /// Rebuilding purely from the model silently drops them, so a parsed torrent cannot be written back out unchanged.
    /// </remarks>
    public static BencodeDictionary Merge(BencodeDictionary? source, IReadOnlyList<KeyValuePair<BencodeString, BencodeValue>> modelled, IReadOnlyList<BencodeString> modelledKeys)
    {
        var result = new BencodeDictionary();

        if (source is not null)
        {
            foreach (var entry in source)
            {
                var replacement = FindValue(modelled, entry.Key);
                if (replacement is not null)
                {
                    result.Add(entry.Key, replacement);
                }
                else if (!Contains(modelledKeys, entry.Key))
                {
                    result.Add(entry.Key, entry.Value);
                }

                // A modelled key the model no longer sets is intentionally dropped, so clearing a property removes it.
            }
        }

        foreach (var entry in modelled)
        {
            if (!result.ContainsKey(entry.Key))
            {
                result.Add(entry.Key, entry.Value);
            }
        }

        return result;
    }

    private static BencodeValue? FindValue(IReadOnlyList<KeyValuePair<BencodeString, BencodeValue>> entries, BencodeString key)
    {
        foreach (var entry in entries)
        {
            if (entry.Key.Equals(key))
                return entry.Value;
        }

        return null;
    }

    private static bool Contains(IReadOnlyList<BencodeString> keys, BencodeString key)
    {
        foreach (var candidate in keys)
        {
            if (candidate.Equals(key))
                return true;
        }

        return false;
    }
}
