namespace Meziantou.Framework.Bencode.Torrent;

public sealed class TorrentInfo
{
    private static readonly BencodeString NameKey = CreateKey("name");
    private static readonly BencodeString PieceLengthKey = CreateKey("piece length");
    private static readonly BencodeString PiecesKey = CreateKey("pieces");
    private static readonly BencodeString PrivateKey = CreateKey("private");
    private static readonly BencodeString LengthKey = CreateKey("length");
    private static readonly BencodeString FilesKey = CreateKey("files");
    private static readonly BencodeString PathKey = CreateKey("path");
    private static readonly BencodeString[] ModelledKeys = [NameKey, PieceLengthKey, PiecesKey, PrivateKey, LengthKey, FilesKey];

    private BencodeDictionary? _source;

    public string Name { get; set; } = "";

    public long PieceLength { get; set; }

    public ReadOnlyMemory<byte> Pieces { get; set; }

    public bool IsPrivate { get; set; }

    public long? Length { get; set; }

    public IReadOnlyList<TorrentInfoFile>? Files { get; set; }

    internal static TorrentInfo Parse(BencodeDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        var name = GetRequiredString(dictionary, NameKey, "name");
        var pieceLength = GetRequiredInteger(dictionary, PieceLengthKey, "piece length");
        var pieces = GetRequiredByteString(dictionary, PiecesKey, "pieces").Value;

        var info = new TorrentInfo
        {
            Name = name,
            PieceLength = pieceLength,
            Pieces = pieces,
        };

        if (dictionary.TryGetValue(PrivateKey, out var privateValue))
        {
            if (privateValue is not BencodeInteger privateInteger)
                throw new FormatException("The 'private' field must be an integer.");

            info.IsPrivate = privateInteger.Value != 0;
        }

        if (dictionary.TryGetValue(LengthKey, out var lengthValue))
        {
            if (lengthValue is not BencodeInteger lengthInteger)
                throw new FormatException("The 'length' field must be an integer.");

            info.Length = lengthInteger.Value;
        }

        if (dictionary.TryGetValue(FilesKey, out var filesValue))
        {
            if (filesValue is not BencodeList filesList)
                throw new FormatException("The 'files' field must be a list.");

            var files = new List<TorrentInfoFile>();
            foreach (var fileValue in filesList)
            {
                if (fileValue is not BencodeDictionary fileDictionary)
                    throw new FormatException("Each torrent file entry must be a dictionary.");

                var fileLength = GetRequiredInteger(fileDictionary, LengthKey, "length");
                if (!fileDictionary.TryGetValue(PathKey, out var pathValue) || pathValue is not BencodeList pathList)
                    throw new FormatException("Each torrent file entry must contain a 'path' list.");

                var path = new List<string>();
                foreach (var segmentValue in pathList)
                {
                    if (segmentValue is not BencodeString segmentString)
                        throw new FormatException("Each path segment must be a bencode string.");

                    path.Add(TorrentField.ToText(segmentString, "path"));
                }

                files.Add(new TorrentInfoFile
                {
                    Length = fileLength,
                    Path = path,
                });
            }

            info.Files = files;
        }

        info._source = dictionary;
        info.Validate();
        return info;
    }

    internal BencodeDictionary ToBencodeDictionary()
    {
        Validate();

        var modelled = new List<KeyValuePair<BencodeString, BencodeValue>>
        {
            new(NameKey, new BencodeString(Encoding.UTF8.GetBytes(Name))),
            new(PieceLengthKey, new BencodeInteger(PieceLength)),
            new(PiecesKey, new BencodeString(Pieces.ToArray())),
        };

        if (IsPrivate)
        {
            modelled.Add(new(PrivateKey, new BencodeInteger(1)));
        }

        if (Length.HasValue)
        {
            modelled.Add(new(LengthKey, new BencodeInteger(Length.Value)));
        }
        else if (Files is not null)
        {
            var files = new BencodeList();
            foreach (var file in Files)
            {
                if (file is null)
                    throw new FormatException("Torrent file entries cannot be null.");

                var path = new BencodeList(file.Path.Select(segment => (BencodeValue)new BencodeString(Encoding.UTF8.GetBytes(segment))));
                files.Add(new BencodeDictionary
                {
                    { LengthKey, new BencodeInteger(file.Length) },
                    { PathKey, path },
                });
            }

            modelled.Add(new(FilesKey, files));
        }

        return TorrentDictionaryMerge.Merge(_source, modelled, ModelledKeys);
    }

    private void Validate()
    {
        if (string.IsNullOrEmpty(Name))
            throw new FormatException("Torrent info must contain a non-empty name.");

        ValidatePathSegment(Name, "name");

        if (PieceLength <= 0)
            throw new FormatException("Torrent info must contain a positive piece length.");

        if (Pieces.IsEmpty)
            throw new FormatException("Torrent info must contain piece hashes.");

        if (Pieces.Length % 20 != 0)
            throw new FormatException("The pieces field length must be a multiple of 20 bytes.");

        var hasLength = Length.HasValue;
        var hasFiles = Files is not null;
        if (hasLength == hasFiles)
            throw new FormatException("Torrent info must contain either 'length' or 'files', but not both.");

        if (Length is < 0)
            throw new FormatException("The 'length' field cannot be negative.");

        if (Files is not null)
        {
            if (Files.Count == 0)
                throw new FormatException("The 'files' field must contain at least one entry.");

            foreach (var file in Files)
            {
                if (file is null)
                    throw new FormatException("Torrent file entries cannot be null.");

                if (file.Length < 0)
                    throw new FormatException("Torrent file lengths cannot be negative.");

                if (file.Path is null || file.Path.Count == 0)
                    throw new FormatException("Each torrent file must contain at least one path segment.");

                foreach (var segment in file.Path)
                {
                    ValidatePathSegment(segment, "path");
                }
            }
        }
    }

    /// <summary>Rejects the segments BEP 3 forbids, because clients build a file path out of these values.</summary>
    private static void ValidatePathSegment(string segment, string fieldName)
    {
        if (string.IsNullOrEmpty(segment))
            throw new FormatException($"The '{fieldName}' field cannot contain a null or empty segment.");

        if (segment is "." or "..")
            throw new FormatException($"The '{fieldName}' field cannot contain a '.' or '..' segment.");

        if (segment.AsSpan().ContainsAny('/', '\\'))
            throw new FormatException($"The '{fieldName}' field cannot contain a directory separator.");
    }

    private static long GetRequiredInteger(BencodeDictionary dictionary, BencodeString key, string fieldName)
    {
        if (!dictionary.TryGetValue(key, out var value) || value is not BencodeInteger integer)
            throw new FormatException($"The required '{fieldName}' field is missing or not an integer.");

        return integer.Value;
    }

    private static string GetRequiredString(BencodeDictionary dictionary, BencodeString key, string fieldName)
    {
        if (!dictionary.TryGetValue(key, out var value) || value is not BencodeString text)
            throw new FormatException($"The required '{fieldName}' field is missing or not a string.");

        return TorrentField.ToText(text, fieldName);
    }

    private static BencodeString GetRequiredByteString(BencodeDictionary dictionary, BencodeString key, string fieldName)
    {
        if (!dictionary.TryGetValue(key, out var value) || value is not BencodeString text)
            throw new FormatException($"The required '{fieldName}' field is missing or not a string.");

        return text;
    }

    private static BencodeString CreateKey(string value) => new(Encoding.UTF8.GetBytes(value));
}
