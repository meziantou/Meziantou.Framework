using System.Security.Cryptography;

namespace Meziantou.Framework.Bencode.Torrent;

public sealed class TorrentFile
{
    public string? Announce { get; set; }

    public IReadOnlyList<IReadOnlyList<string>>? AnnounceList { get; set; }

    public string? Comment { get; set; }

    public string? CreatedBy { get; set; }

    public DateTimeOffset? CreationDate { get; set; }

    public TorrentInfo Info { get; set; } = new();

    public static TorrentFile Parse(ReadOnlySpan<byte> data)
    {
        var root = BencodeDocument.Parse(data).Root;
        return Parse(root);
    }

    public static async ValueTask<TorrentFile> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var root = (await BencodeDocument.ParseAsync(stream, cancellationToken).ConfigureAwait(false)).Root;
        return Parse(root);
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

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "BitTorrent v1 info-hash requires SHA-1.")]
    public byte[] GetInfoHashSha1()
    {
        var data = Info.ToBencodeDictionary().ToUtf8ByteArray(canonical: true);
        return SHA1.HashData(data);
    }

    public byte[] GetInfoHashSha256()
    {
        var data = Info.ToBencodeDictionary().ToUtf8ByteArray(canonical: true);
        return SHA256.HashData(data);
    }

    private static TorrentFile Parse(BencodeValue root)
    {
        if (root is not BencodeDictionary dictionary)
            throw new FormatException("Torrent metainfo root must be a bencode dictionary.");

        if (!dictionary.TryGetValue("info", out var infoValue) || infoValue is not BencodeDictionary infoDictionary)
            throw new FormatException("Torrent metainfo must contain an 'info' dictionary.");

        var result = new TorrentFile
        {
            Info = TorrentInfo.Parse(infoDictionary),
        };

        if (dictionary.TryGetValue("announce", out var announceValue))
        {
            if (announceValue is not BencodeString announceText)
                throw new FormatException("The 'announce' field must be a string.");

            result.Announce = announceText.ToUtf8String();
        }

        if (dictionary.TryGetValue("announce-list", out var announceListValue))
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

                    urls.Add(urlText.ToUtf8String());
                }

                tiers.Add(urls);
            }

            result.AnnounceList = tiers;
        }

        if (dictionary.TryGetValue("comment", out var commentValue))
        {
            if (commentValue is not BencodeString commentText)
                throw new FormatException("The 'comment' field must be a string.");

            result.Comment = commentText.ToUtf8String();
        }

        if (dictionary.TryGetValue("created by", out var createdByValue))
        {
            if (createdByValue is not BencodeString createdByText)
                throw new FormatException("The 'created by' field must be a string.");

            result.CreatedBy = createdByText.ToUtf8String();
        }

        if (dictionary.TryGetValue("creation date", out var creationDateValue))
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
            { "info", Info.ToBencodeDictionary() },
        };

        if (Announce is not null)
        {
            dictionary.Add("announce", new BencodeString(Encoding.UTF8.GetBytes(Announce)));
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

            dictionary.Add("announce-list", tiers);
        }

        if (Comment is not null)
        {
            dictionary.Add("comment", new BencodeString(Encoding.UTF8.GetBytes(Comment)));
        }

        if (CreatedBy is not null)
        {
            dictionary.Add("created by", new BencodeString(Encoding.UTF8.GetBytes(CreatedBy)));
        }

        if (CreationDate.HasValue)
        {
            dictionary.Add("creation date", new BencodeInteger(CreationDate.Value.ToUnixTimeSeconds()));
        }

        return dictionary;
    }
}
