using System.IO.Pipelines;
using System.Security.Cryptography;

namespace Meziantou.Framework.Bencode.Torrent;

public sealed class TorrentFile
{
    private const int PiecesRootLength = 32;

    private static readonly BencodeString InfoKey = TorrentField.CreateKey("info");
    private static readonly BencodeString AnnounceKey = TorrentField.CreateKey("announce");
    private static readonly BencodeString AnnounceListKey = TorrentField.CreateKey("announce-list");
    private static readonly BencodeString CommentKey = TorrentField.CreateKey("comment");
    private static readonly BencodeString CreatedByKey = TorrentField.CreateKey("created by");
    private static readonly BencodeString CreationDateKey = TorrentField.CreateKey("creation date");
    private static readonly BencodeString UrlListKey = TorrentField.CreateKey("url-list");
    private static readonly BencodeString HttpSeedsKey = TorrentField.CreateKey("httpseeds");
    private static readonly BencodeString NodesKey = TorrentField.CreateKey("nodes");
    private static readonly BencodeString EncodingKey = TorrentField.CreateKey("encoding");
    private static readonly BencodeString PublisherKey = TorrentField.CreateKey("publisher");
    private static readonly BencodeString PublisherUrlKey = TorrentField.CreateKey("publisher-url");
    private static readonly BencodeString PieceLayersKey = TorrentField.CreateKey("piece layers");

    private static readonly HashSet<BencodeString> ModeledKeys = TorrentField.CreateKeySet(
        InfoKey, AnnounceKey, AnnounceListKey, CommentKey, CreatedByKey, CreationDateKey, UrlListKey, HttpSeedsKey, NodesKey, EncodingKey, PublisherKey, PublisherUrlKey, PieceLayersKey);

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

    /// <summary>The 'encoding' key naming the code page the text fields are written with.</summary>
    /// <remarks>
    /// When it names a known encoding other than UTF-8, such as <c>GBK</c> or <c>windows-1251</c>, the text fields of the
    /// torrent are read and written with that encoding. An unknown name is ignored: the text fields are UTF-8.
    /// </remarks>
    public string? Encoding { get; set; }

    public string? Publisher { get; set; }

    /// <summary>The 'publisher-url' key. Kept as text because the metainfo value is not guaranteed to be a well-formed URI.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The value comes from the torrent and may not parse as a Uri; 'announce' is exposed the same way.")]
    public string? PublisherUrl { get; set; }

    /// <summary>The BitTorrent v2 'piece layers' key (BEP 52): the piece hashes of each file larger than a piece.</summary>
    public IReadOnlyList<TorrentPieceLayer>? PieceLayers { get; set; }

    /// <summary>The keys of the root dictionary this model does not represent. They are written back as they are.</summary>
    public IDictionary<BencodeString, BencodeValue> AdditionalKeys { get; } = new Dictionary<BencodeString, BencodeValue>();

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

    /// <summary>Parses a torrent from a stream, as the data arrives. The stream is left open.</summary>
    public static async ValueTask<TorrentFile> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var reader = new BencodeReader(stream, leaveOpen: true);
        await using (reader.ConfigureAwait(false))
        {
            return await ParseAsync(reader, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Parses a torrent from a <see cref="PipeReader"/>, as the data arrives. Complete the reader yourself.</summary>
    public static async ValueTask<TorrentFile> ParseAsync(PipeReader reader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var bencodeReader = new BencodeReader(reader);
        await using (bencodeReader.ConfigureAwait(false))
        {
            return await ParseAsync(bencodeReader, cancellationToken).ConfigureAwait(false);
        }
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

    /// <summary>Computes the SHA-256 of the 'info' dictionary, which is the BitTorrent v2 info-hash.</summary>
    /// <remarks>For a parsed torrent this hashes the exact bytes the 'info' dictionary was read from, so keys this model does not represent still contribute to the hash.</remarks>
    public byte[] GetInfoHashSha256()
    {
        return SHA256.HashData(GetInfoBytes().Span);
    }

    /// <summary>Returns the bytes to hash: the ones the torrent was parsed from when available, otherwise a canonical encoding of the model.</summary>
    /// <remarks>
    /// Re-encoding a parsed torrent can change the bytes: the keys may not have been sorted, and an explicit 'private 0'
    /// is not written. Any change changes the hash, which is the torrent's identity.
    /// </remarks>
    private ReadOnlyMemory<byte> GetInfoBytes()
    {
        return _infoBytes.IsEmpty ? Info.ToBencodeDictionary(TorrentField.ResolveEncoding(Encoding)).ToUtf8ByteArray(canonical: true) : _infoBytes;
    }

    /// <summary>Reads the root dictionary, keeping a copy of the bytes of the 'info' dictionary to compute the info-hash.</summary>
    private static async ValueTask<TorrentFile> ParseAsync(BencodeReader reader, CancellationToken cancellationToken)
    {
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (reader.TokenType is not BencodeTokenType.StartDictionary)
        {
            // Read the whole value so invalid bencode is reported as such
            await reader.ReadValueAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            reader.MarkFailed();
            throw new FormatException("Torrent metainfo root must be a bencode dictionary.");
        }

        var root = new BencodeDictionary();
        ReadOnlyMemory<byte> infoBytes = default;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && reader.TokenType is BencodeTokenType.Key)
        {
            var key = new BencodeString(reader.GetBytes());
            var isInfo = key.Equals(InfoKey);
            if (isInfo)
            {
                reader.StartCapture();
            }

            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var value = await reader.ReadValueAsync(cancellationToken).ConfigureAwait(false);
            if (isInfo)
            {
                infoBytes = reader.StopCapture();
            }

            try
            {
                root.Add(key, value);
            }
            catch (ArgumentException ex)
            {
                reader.MarkFailed();
                throw new FormatException("Duplicate bencode dictionary key.", ex);
            }
        }

        // The end of the root dictionary: the next read checks there is no trailing data
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        var result = Parse(root);
        if (root.TryGetValue(InfoKey, out var infoValue) && infoValue is BencodeDictionary)
        {
            result._infoBytes = infoBytes;
        }

        return result;
    }

    private static TorrentFile Parse(BencodeValue root)
    {
        if (root is not BencodeDictionary dictionary)
            throw new FormatException("Torrent metainfo root must be a bencode dictionary.");

        if (!dictionary.TryGetValue(InfoKey, out var infoValue) || infoValue is not BencodeDictionary infoDictionary)
            throw new FormatException("Torrent metainfo must contain an 'info' dictionary.");

        // The encoding of the text fields, so it is read first
        string? encodingName = null;
        if (dictionary.TryGetValue(EncodingKey, out var encodingValue))
        {
            if (encodingValue is not BencodeString encodingText)
                throw new FormatException("The 'encoding' field must be a string.");

            encodingName = TorrentField.ToText(encodingText, "encoding", encoding: null);
        }

        var encoding = TorrentField.ResolveEncoding(encodingName);
        var result = new TorrentFile
        {
            Encoding = encodingName,
            Info = TorrentInfo.Parse(infoDictionary, encoding),
        };

        if (dictionary.TryGetValue(AnnounceKey, out var announceValue))
        {
            if (announceValue is not BencodeString announceText)
                throw new FormatException("The 'announce' field must be a string.");

            result.Announce = TorrentField.ToText(announceText, "announce", encoding);
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

                    urls.Add(TorrentField.ToText(urlText, "announce-list", encoding));
                }

                tiers.Add(urls);
            }

            result.AnnounceList = tiers;
        }

        if (dictionary.TryGetValue(CommentKey, out var commentValue))
        {
            if (commentValue is not BencodeString commentText)
                throw new FormatException("The 'comment' field must be a string.");

            result.Comment = TorrentField.ToText(commentText, "comment", encoding);
        }

        if (dictionary.TryGetValue(CreatedByKey, out var createdByValue))
        {
            if (createdByValue is not BencodeString createdByText)
                throw new FormatException("The 'created by' field must be a string.");

            result.CreatedBy = TorrentField.ToText(createdByText, "created by", encoding);
        }

        if (dictionary.TryGetValue(UrlListKey, out var urlListValue))
        {
            // BEP 19 allows either a single URL or a list of them.
            result.UrlList = urlListValue switch
            {
                BencodeString single => [TorrentField.ToText(single, "url-list", encoding)],
                BencodeList list => ReadStringList(list, "url-list", encoding),
                _ => throw new FormatException("The 'url-list' field must be a string or a list of strings."),
            };
        }

        if (dictionary.TryGetValue(HttpSeedsKey, out var httpSeedsValue))
        {
            if (httpSeedsValue is not BencodeList httpSeeds)
                throw new FormatException("The 'httpseeds' field must be a list.");

            result.HttpSeeds = ReadStringList(httpSeeds, "httpseeds", encoding);
        }

        if (dictionary.TryGetValue(NodesKey, out var nodesValue))
        {
            if (nodesValue is not BencodeList nodes)
                throw new FormatException("The 'nodes' field must be a list.");

            result.Nodes = ReadNodes(nodes, encoding);
        }

        if (dictionary.TryGetValue(PublisherKey, out var publisherValue))
        {
            if (publisherValue is not BencodeString publisherText)
                throw new FormatException("The 'publisher' field must be a string.");

            result.Publisher = TorrentField.ToText(publisherText, "publisher", encoding);
        }

        if (dictionary.TryGetValue(PublisherUrlKey, out var publisherUrlValue))
        {
            if (publisherUrlValue is not BencodeString publisherUrlText)
                throw new FormatException("The 'publisher-url' field must be a string.");

            result.PublisherUrl = TorrentField.ToText(publisherUrlText, "publisher-url", encoding);
        }

        if (dictionary.TryGetValue(CreationDateKey, out var creationDateValue))
        {
            if (creationDateValue is not BencodeInteger creationDateInteger)
                throw new FormatException("The 'creation date' field must be an integer.");

            try
            {
                result.CreationDate = DateTimeOffset.FromUnixTimeSeconds(TorrentField.ToInt64(creationDateInteger, "creation date"));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new FormatException("The 'creation date' value is out of range.", ex);
            }
        }

        if (dictionary.TryGetValue(PieceLayersKey, out var pieceLayersValue))
        {
            if (pieceLayersValue is not BencodeDictionary pieceLayers)
                throw new FormatException("The 'piece layers' field must be a dictionary.");

            var layers = new List<TorrentPieceLayer>(pieceLayers.Count);
            foreach (var (piecesRoot, hashesValue) in pieceLayers)
            {
                if (hashesValue is not BencodeString hashes)
                    throw new FormatException("Each 'piece layers' entry must be a string.");

                layers.Add(new TorrentPieceLayer { PiecesRoot = piecesRoot.Value, Hashes = hashes.Value });
            }

            result.PieceLayers = layers;
            ValidatePieceLayers(layers);
        }

        TorrentField.ReadAdditionalKeys(dictionary, ModeledKeys, result.AdditionalKeys);
        return result;
    }

    private BencodeDictionary ToBencodeDictionary()
    {
        if (Info is null)
            throw new FormatException("Torrent file must contain a non-null info object.");

        var encoding = TorrentField.ResolveEncoding(Encoding);
        var dictionary = new BencodeDictionary
        {
            { InfoKey, Info.ToBencodeDictionary(encoding) },
        };

        if (Announce is not null)
        {
            dictionary.Add(AnnounceKey, TorrentField.FromText(Announce, "announce", encoding));
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

                    tierValues.Add(TorrentField.FromText(url, "announce-list", encoding));
                }

                tiers.Add(tierValues);
            }

            dictionary.Add(AnnounceListKey, tiers);
        }

        if (Comment is not null)
        {
            dictionary.Add(CommentKey, TorrentField.FromText(Comment, "comment", encoding));
        }

        if (CreatedBy is not null)
        {
            dictionary.Add(CreatedByKey, TorrentField.FromText(CreatedBy, "created by", encoding));
        }

        if (CreationDate.HasValue)
        {
            dictionary.Add(CreationDateKey, new BencodeInteger(CreationDate.Value.ToUnixTimeSeconds()));
        }

        if (UrlList is not null)
        {
            dictionary.Add(UrlListKey, WriteStringList(UrlList, "url-list", encoding));
        }

        if (HttpSeeds is not null)
        {
            dictionary.Add(HttpSeedsKey, WriteStringList(HttpSeeds, "httpseeds", encoding));
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

                nodes.Add(new BencodeList([TorrentField.FromText(node.Host, "nodes", encoding), new BencodeInteger(node.Port)]));
            }

            dictionary.Add(NodesKey, nodes);
        }

        if (Encoding is not null)
        {
            // The name of the encoding is ASCII, whatever the encoding it names
            dictionary.Add(EncodingKey, TorrentField.FromText(Encoding, "encoding", encoding: null));
        }

        if (Publisher is not null)
        {
            dictionary.Add(PublisherKey, TorrentField.FromText(Publisher, "publisher", encoding));
        }

        if (PublisherUrl is not null)
        {
            dictionary.Add(PublisherUrlKey, TorrentField.FromText(PublisherUrl, "publisher-url", encoding));
        }

        if (PieceLayers is not null)
        {
            ValidatePieceLayers(PieceLayers);
            var pieceLayers = new BencodeDictionary();
            foreach (var layer in PieceLayers)
            {
                try
                {
                    pieceLayers.Add(new BencodeString(layer.PiecesRoot.ToArray()), new BencodeString(layer.Hashes.ToArray()));
                }
                catch (ArgumentException ex)
                {
                    throw new FormatException("Two 'piece layers' entries have the same pieces root.", ex);
                }
            }

            dictionary.Add(PieceLayersKey, pieceLayers);
        }

        TorrentField.WriteAdditionalKeys(dictionary, AdditionalKeys, ModeledKeys);
        return dictionary;
    }

    private static void ValidatePieceLayers(IReadOnlyList<TorrentPieceLayer> layers)
    {
        foreach (var layer in layers)
        {
            if (layer is null)
                throw new FormatException("Piece layers cannot be null.");

            if (layer.PiecesRoot.Length != PiecesRootLength)
                throw new FormatException("The pieces root of a 'piece layers' entry must be 32 bytes.");

            if (layer.Hashes.IsEmpty || layer.Hashes.Length % 32 != 0)
                throw new FormatException("The hashes of a 'piece layers' entry must be a non-empty multiple of 32 bytes.");
        }
    }

    private static BencodeList WriteStringList(IReadOnlyList<string> values, string fieldName, Encoding? encoding)
    {
        var result = new BencodeList();
        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value))
                throw new FormatException($"A '{fieldName}' entry cannot be null or empty.");

            result.Add(TorrentField.FromText(value, fieldName, encoding));
        }

        return result;
    }

    private static List<string> ReadStringList(BencodeList list, string fieldName, Encoding? encoding)
    {
        var result = new List<string>(list.Count);
        foreach (var value in list)
        {
            if (value is not BencodeString text)
                throw new FormatException($"Each '{fieldName}' entry must be a string.");

            result.Add(TorrentField.ToText(text, fieldName, encoding));
        }

        return result;
    }

    private static List<TorrentNode> ReadNodes(BencodeList list, Encoding? encoding)
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

            if (!port.TryGetInt64(out var portValue) || portValue is < 0 or > 65535)
                throw new FormatException("The port of a 'nodes' entry is out of range.");

            result.Add(new TorrentNode
            {
                Host = TorrentField.ToText(host, "nodes", encoding),
                Port = (int)portValue,
            });
        }

        return result;
    }
}
