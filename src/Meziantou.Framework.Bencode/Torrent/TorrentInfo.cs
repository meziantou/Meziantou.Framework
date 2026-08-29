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
    private static readonly BencodeString SourceKey = CreateKey("source");
    private static readonly BencodeString Md5SumKey = CreateKey("md5sum");

    public string Name { get; set; } = "";

    public long PieceLength { get; set; }

    public ReadOnlyMemory<byte> Pieces { get; set; }

    public bool IsPrivate { get; set; }

    public long? Length { get; set; }

    public IReadOnlyList<TorrentInfoFile>? Files { get; set; }

    /// <summary>The 'source' key many private trackers add to make the info-hash unique to them.</summary>
    public string? Source { get; set; }

    /// <summary>The 'md5sum' of a single-file torrent's content, when the torrent carries one.</summary>
    public string? Md5Sum { get; set; }

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

        if (dictionary.TryGetValue(SourceKey, out var sourceValue))
        {
            if (sourceValue is not BencodeString sourceText)
                throw new FormatException("The 'source' field must be a string.");

            info.Source = TorrentField.ToText(sourceText, "source");
        }

        if (dictionary.TryGetValue(Md5SumKey, out var md5Value))
        {
            if (md5Value is not BencodeString md5Text)
                throw new FormatException("The 'md5sum' field must be a string.");

            info.Md5Sum = TorrentField.ToText(md5Text, "md5sum");
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

                var file = new TorrentInfoFile
                {
                    Length = fileLength,
                    Path = path,
                };

                if (fileDictionary.TryGetValue(Md5SumKey, out var fileMd5Value))
                {
                    if (fileMd5Value is not BencodeString fileMd5Text)
                        throw new FormatException("The 'md5sum' field must be a string.");

                    file.Md5Sum = TorrentField.ToText(fileMd5Text, "md5sum");
                }

                files.Add(file);
            }

            info.Files = files;
        }

        info.Validate();
        return info;
    }

    internal BencodeDictionary ToBencodeDictionary()
    {
        Validate();

        var dictionary = new BencodeDictionary
        {
            { NameKey, new BencodeString(Encoding.UTF8.GetBytes(Name)) },
            { PieceLengthKey, new BencodeInteger(PieceLength) },
            { PiecesKey, new BencodeString(Pieces.ToArray()) },
        };

        if (IsPrivate)
        {
            dictionary.Add(PrivateKey, new BencodeInteger(1));
        }

        if (Source is not null)
        {
            dictionary.Add(SourceKey, new BencodeString(Encoding.UTF8.GetBytes(Source)));
        }

        if (Md5Sum is not null)
        {
            dictionary.Add(Md5SumKey, new BencodeString(Encoding.UTF8.GetBytes(Md5Sum)));
        }

        if (Length.HasValue)
        {
            dictionary.Add(LengthKey, new BencodeInteger(Length.Value));
        }
        else if (Files is not null)
        {
            var files = new BencodeList();
            foreach (var file in Files)
            {
                if (file is null)
                    throw new FormatException("Torrent file entries cannot be null.");

                var path = new BencodeList(file.Path.Select(segment => (BencodeValue)new BencodeString(Encoding.UTF8.GetBytes(segment))));
                var fileDictionary = new BencodeDictionary
                {
                    { LengthKey, new BencodeInteger(file.Length) },
                    { PathKey, path },
                };

                if (file.Md5Sum is not null)
                {
                    fileDictionary.Add(Md5SumKey, new BencodeString(Encoding.UTF8.GetBytes(file.Md5Sum)));
                }

                files.Add(fileDictionary);
            }

            dictionary.Add(FilesKey, files);
        }

        return dictionary;
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
