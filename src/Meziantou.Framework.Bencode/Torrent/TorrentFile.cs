using System.Security.Cryptography;

namespace Meziantou.Framework.Bencode.Torrent;

public sealed class TorrentFile
{
    private static readonly BencodeString InfoKey = CreateKey("info");
    private static readonly BencodeString AnnounceKey = CreateKey("announce");
    private static readonly BencodeString AnnounceListKey = CreateKey("announce-list");
    private static readonly BencodeString CommentKey = CreateKey("comment");
    private static readonly BencodeString CreatedByKey = CreateKey("created by");
    private static readonly BencodeString CreationDateKey = CreateKey("creation date");
    private static readonly BencodeString UrlListKey = CreateKey("url-list");
    private static readonly BencodeString HttpSeedsKey = CreateKey("httpseeds");
    private static readonly BencodeString NodesKey = CreateKey("nodes");
    private static readonly BencodeString EncodingKey = CreateKey("encoding");
    private static readonly BencodeString PublisherKey = CreateKey("publisher");
    private static readonly BencodeString PublisherUrlKey = CreateKey("publisher-url");

    public string? Announce { get; set; }

    public IReadOnlyList<IReadOnlyList<string>>? AnnounceList { get; set; }

    public string? Comment { get; set; }

    public string? CreatedBy { get; set; }

    public DateTimeOffset? CreationDate { get; set; }

    /// <summary>Web seed URLs from the 'url-list' key (BEP 19).</summary>
    /// <remarks>BEP 19 allows a bare string as well as a list; both are read into this list, and it is always written back as a list.</remarks>
    public IReadOnlyList<string>? UrlList { get; set; }

    /// <summary>Web seed URLs from the 'httpseeds' key (BEP 17).</summary>
    public IReadOnlyList<string>? HttpSeeds { get; set; }

    /// <summary>DHT bootstrap nodes from the 'nodes' key (BEP 5).</summary>
    public IReadOnlyList<TorrentNode>? Nodes { get; set; }

    /// <summary>The 'encoding' key naming the code page the text fields were written with.</summary>
    public string? Encoding { get; set; }

    public string? Publisher { get; set; }

    /// <summary>The 'publisher-url' key. Kept as text because the metainfo value is not guaranteed to be a well-formed URI.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The value comes from the torrent and may not parse as a Uri; 'announce' is exposed the same way.")]
    public string? PublisherUrl { get; set; }

    private TorrentInfo _info = new();
    private ReadOnlyMemory<byte> _infoBytes;

    /// <summary>The metainfo dictionary. Replacing it discards the parsed bytes, so the info-hash is recomputed from the model.</summary>
    public TorrentInfo Info
    {
        get => _info;
        set
        {
            _info = value;
            _infoBytes = default;
        }
    }

    public static TorrentFile Parse(ReadOnlySpan<byte> data)
    {
        var root = BencodeDocument.Parse(data).Root;
        var result = Parse(root);

        if (root is BencodeDictionary rootDictionary
            && rootDictionary.TryGetValue(InfoKey, out var infoValue)
            && infoValue is BencodeDictionary { SourceLength: > 0 } infoDictionary)
        {
            result._infoBytes = data.Slice(infoDictionary.SourceOffset, infoDictionary.SourceLength).ToArray();
        }

        return result;
    }

    public static async ValueTask<TorrentFile> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // The whole file is buffered so the info dictionary can be hashed from the bytes it was parsed from.
        // The decoded model is the same size anyway, so this does not change the order of memory used.
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    public static bool TryParse(ReadOnlySpan<byte> data, [NotNullWhen(true)] out TorrentFile? result)
    {
        try
        {
            result = Parse(data);
            return true;
        }
        catch (FormatException)
        {
            result = null;
            return false;
        }
    }

    public byte[] ToUtf8ByteArray(bool canonical = true)
    {
        var root = ToBencodeDictionary();
        return root.ToUtf8ByteArray(canonical);
    }

    public async ValueTask WriteToAsync(Stream stream, bool canonical = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var data = ToUtf8ByteArray(canonical);
        await stream.WriteAsync(data.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Computes the BitTorrent v1 info-hash.</summary>
    /// <remarks>For a parsed torrent this hashes the exact bytes the 'info' dictionary was read from, so keys this model does not represent still contribute to the hash.</remarks>
    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "BitTorrent v1 info-hash requires SHA-1.")]
    public byte[] GetInfoHashSha1()
    {
        return SHA1.HashData(GetInfoBytes().Span);
    }

    /// <summary>Computes the SHA-256 of the 'info' dictionary.</summary>
    /// <remarks>For a parsed torrent this hashes the exact bytes the 'info' dictionary was read from, so keys this model does not represent still contribute to the hash.</remarks>
    public byte[] GetInfoHashSha256()
    {
        return SHA256.HashData(GetInfoBytes().Span);
    }

    /// <summary>Returns the bytes to hash: the ones the torrent was parsed from when available, otherwise a canonical encoding of the model.</summary>
    /// <remarks>
    /// Re-encoding a parsed torrent would drop every 'info' key this model does not represent ('source', 'md5sum',
    /// an explicit 'private 0', the BitTorrent v2 keys), and dropping any of them changes the hash, which is the
    /// torrent's identity.
    /// </remarks>
    private ReadOnlyMemory<byte> GetInfoBytes()
    {
        return _infoBytes.IsEmpty ? Info.ToBencodeDictionary().ToUtf8ByteArray(canonical: true) : _infoBytes;
    }

    private static TorrentFile Parse(BencodeValue root)
    {
        if (root is not BencodeDictionary dictionary)
            throw new FormatException("Torrent metainfo root must be a bencode dictionary.");

        if (!dictionary.TryGetValue(InfoKey, out var infoValue) || infoValue is not BencodeDictionary infoDictionary)
            throw new FormatException("Torrent metainfo must contain an 'info' dictionary.");

        var result = new TorrentFile
        {
            Info = TorrentInfo.Parse(infoDictionary),
        };

        if (dictionary.TryGetValue(AnnounceKey, out var announceValue))
        {
            if (announceValue is not BencodeString announceText)
                throw new FormatException("The 'announce' field must be a string.");

            result.Announce = TorrentField.ToText(announceText, "announce");
        }

        if (dictionary.TryGetValue(AnnounceListKey, out var announceListValue))
        {
            if (announceListValue is not BencodeList announceTiers)
                throw new FormatException("The 'announce-list' field must be a list.");

            var tiers = new List<IReadOnlyList<string>>();
            foreach (var tierValue in announceTiers)
            {
                if (tierValue is not BencodeList tier)
                    throw new FormatException("Each announce-list entry must be a list.");

                var urls = new List<string>();
                foreach (var urlValue in tier)
                {
                    if (urlValue is not BencodeString urlText)
                        throw new FormatException("Each tracker URL must be a string.");

                    urls.Add(TorrentField.ToText(urlText, "announce-list"));
                }

                tiers.Add(urls);
            }

            result.AnnounceList = tiers;
        }

        if (dictionary.TryGetValue(CommentKey, out var commentValue))
        {
            if (commentValue is not BencodeString commentText)
                throw new FormatException("The 'comment' field must be a string.");

            result.Comment = TorrentField.ToText(commentText, "comment");
        }

        if (dictionary.TryGetValue(CreatedByKey, out var createdByValue))
        {
            if (createdByValue is not BencodeString createdByText)
                throw new FormatException("The 'created by' field must be a string.");

            result.CreatedBy = TorrentField.ToText(createdByText, "created by");
        }

        if (dictionary.TryGetValue(UrlListKey, out var urlListValue))
        {
            // BEP 19 allows either a single URL or a list of them.
            result.UrlList = urlListValue switch
            {
                BencodeString single => [TorrentField.ToText(single, "url-list")],
                BencodeList list => ReadStringList(list, "url-list"),
                _ => throw new FormatException("The 'url-list' field must be a string or a list of strings."),
            };
        }

        if (dictionary.TryGetValue(HttpSeedsKey, out var httpSeedsValue))
        {
            if (httpSeedsValue is not BencodeList httpSeeds)
                throw new FormatException("The 'httpseeds' field must be a list.");

            result.HttpSeeds = ReadStringList(httpSeeds, "httpseeds");
        }

        if (dictionary.TryGetValue(NodesKey, out var nodesValue))
        {
            if (nodesValue is not BencodeList nodes)
                throw new FormatException("The 'nodes' field must be a list.");

            result.Nodes = ReadNodes(nodes);
        }

        if (dictionary.TryGetValue(EncodingKey, out var encodingValue))
        {
            if (encodingValue is not BencodeString encodingText)
                throw new FormatException("The 'encoding' field must be a string.");

            result.Encoding = TorrentField.ToText(encodingText, "encoding");
        }

        if (dictionary.TryGetValue(PublisherKey, out var publisherValue))
        {
            if (publisherValue is not BencodeString publisherText)
                throw new FormatException("The 'publisher' field must be a string.");

            result.Publisher = TorrentField.ToText(publisherText, "publisher");
        }

        if (dictionary.TryGetValue(PublisherUrlKey, out var publisherUrlValue))
        {
            if (publisherUrlValue is not BencodeString publisherUrlText)
                throw new FormatException("The 'publisher-url' field must be a string.");

            result.PublisherUrl = TorrentField.ToText(publisherUrlText, "publisher-url");
        }

        if (dictionary.TryGetValue(CreationDateKey, out var creationDateValue))
        {
            if (creationDateValue is not BencodeInteger creationDateInteger)
                throw new FormatException("The 'creation date' field must be an integer.");

            try
            {
                result.CreationDate = DateTimeOffset.FromUnixTimeSeconds(creationDateInteger.Value);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new FormatException("The 'creation date' value is out of range.", ex);
            }
        }

        return result;
    }

    private BencodeDictionary ToBencodeDictionary()
    {
        if (Info is null)
            throw new FormatException("Torrent file must contain a non-null info object.");

        var dictionary = new BencodeDictionary
        {
            { InfoKey, Info.ToBencodeDictionary() },
        };

        if (Announce is not null)
        {
            dictionary.Add(AnnounceKey, ToBencodeString(Announce));
        }

        if (AnnounceList is not null)
        {
            var tiers = new BencodeList();
            foreach (var tier in AnnounceList)
            {
                if (tier is null)
                    throw new FormatException("Announce-list tiers cannot be null.");

                var tierValues = new BencodeList();
                foreach (var url in tier)
                {
                    if (string.IsNullOrEmpty(url))
                        throw new FormatException("Announce-list URLs cannot be null or empty.");

                    tierValues.Add(ToBencodeString(url));
                }

                tiers.Add(tierValues);
            }

            dictionary.Add(AnnounceListKey, tiers);
        }

        if (Comment is not null)
        {
            dictionary.Add(CommentKey, ToBencodeString(Comment));
        }

        if (CreatedBy is not null)
        {
            dictionary.Add(CreatedByKey, ToBencodeString(CreatedBy));
        }

        if (CreationDate.HasValue)
        {
            dictionary.Add(CreationDateKey, new BencodeInteger(CreationDate.Value.ToUnixTimeSeconds()));
        }

        if (UrlList is not null)
        {
            dictionary.Add(UrlListKey, WriteStringList(UrlList, "url-list"));
        }

        if (HttpSeeds is not null)
        {
            dictionary.Add(HttpSeedsKey, WriteStringList(HttpSeeds, "httpseeds"));
        }

        if (Nodes is not null)
        {
            var nodes = new BencodeList();
            foreach (var node in Nodes)
            {
                if (node is null)
                    throw new FormatException("Nodes cannot be null.");

                if (string.IsNullOrEmpty(node.Host))
                    throw new FormatException("A node host cannot be null or empty.");

                if (node.Port is < 0 or > 65535)
                    throw new FormatException("A node port is out of range.");

                nodes.Add(new BencodeList([ToBencodeString(node.Host), new BencodeInteger(node.Port)]));
            }

            dictionary.Add(NodesKey, nodes);
        }

        if (Encoding is not null)
        {
            dictionary.Add(EncodingKey, ToBencodeString(Encoding));
        }

        if (Publisher is not null)
        {
            dictionary.Add(PublisherKey, ToBencodeString(Publisher));
        }

        if (PublisherUrl is not null)
        {
            dictionary.Add(PublisherUrlKey, ToBencodeString(PublisherUrl));
        }

        return dictionary;
    }

    private static BencodeList WriteStringList(IReadOnlyList<string> values, string fieldName)
    {
        var result = new BencodeList();
        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value))
                throw new FormatException($"A '{fieldName}' entry cannot be null or empty.");

            result.Add(ToBencodeString(value));
        }

        return result;
    }

    private static List<string> ReadStringList(BencodeList list, string fieldName)
    {
        var result = new List<string>(list.Count);
        foreach (var value in list)
        {
            if (value is not BencodeString text)
                throw new FormatException($"Each '{fieldName}' entry must be a string.");

            result.Add(TorrentField.ToText(text, fieldName));
        }

        return result;
    }

    private static List<TorrentNode> ReadNodes(BencodeList list)
    {
        var result = new List<TorrentNode>(list.Count);
        foreach (var value in list)
        {
            if (value is not BencodeList { Count: 2 } node)
                throw new FormatException("Each 'nodes' entry must be a list holding a host and a port.");

            if (node[0] is not BencodeString host)
                throw new FormatException("The host of a 'nodes' entry must be a string.");

            if (node[1] is not BencodeInteger port)
                throw new FormatException("The port of a 'nodes' entry must be an integer.");

            if (port.Value is < 0 or > 65535)
                throw new FormatException("The port of a 'nodes' entry is out of range.");

            result.Add(new TorrentNode
            {
                Host = TorrentField.ToText(host, "nodes"),
                Port = (int)port.Value,
            });
        }

        return result;
    }

    private static BencodeString CreateKey(string value) => ToBencodeString(value);

    private static BencodeString ToBencodeString(string value) => new(System.Text.Encoding.UTF8.GetBytes(value));
}
