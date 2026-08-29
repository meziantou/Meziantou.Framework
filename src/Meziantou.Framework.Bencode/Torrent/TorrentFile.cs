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
    private static readonly BencodeString[] ModelledKeys = [InfoKey, AnnounceKey, AnnounceListKey, CommentKey, CreatedByKey, CreationDateKey];

    private BencodeDictionary? _source;

    public string? Announce { get; set; }

    public IReadOnlyList<IReadOnlyList<string>>? AnnounceList { get; set; }

    public string? Comment { get; set; }

    public string? CreatedBy { get; set; }

    public DateTimeOffset? CreationDate { get; set; }

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

    public static bool TryParse(ReadOnlySpan<byte> data, out TorrentFile? result)
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

        result._source = dictionary;

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

        var modelled = new List<KeyValuePair<BencodeString, BencodeValue>>
        {
            new(InfoKey, Info.ToBencodeDictionary()),
        };

        if (Announce is not null)
        {
            modelled.Add(new(AnnounceKey, new BencodeString(Encoding.UTF8.GetBytes(Announce))));
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

                    tierValues.Add(new BencodeString(Encoding.UTF8.GetBytes(url)));
                }

                tiers.Add(tierValues);
            }

            modelled.Add(new(AnnounceListKey, tiers));
        }

        if (Comment is not null)
        {
            modelled.Add(new(CommentKey, new BencodeString(Encoding.UTF8.GetBytes(Comment))));
        }

        if (CreatedBy is not null)
        {
            modelled.Add(new(CreatedByKey, new BencodeString(Encoding.UTF8.GetBytes(CreatedBy))));
        }

        if (CreationDate.HasValue)
        {
            modelled.Add(new(CreationDateKey, new BencodeInteger(CreationDate.Value.ToUnixTimeSeconds())));
        }

        return TorrentDictionaryMerge.Merge(_source, modelled, ModelledKeys);
    }

    private static BencodeString CreateKey(string value) => new(Encoding.UTF8.GetBytes(value));
}
