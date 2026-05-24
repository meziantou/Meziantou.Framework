namespace Meziantou.Framework.Bencode.Torrent;

public sealed class TorrentInfo
{
    public string Name { get; set; } = "";

    public long PieceLength { get; set; }

    public ReadOnlyMemory<byte> Pieces { get; set; }

    public bool IsPrivate { get; set; }

    public long? Length { get; set; }

    public IReadOnlyList<TorrentInfoFile>? Files { get; set; }

    internal static TorrentInfo Parse(BencodeDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        var name = GetRequiredString(dictionary, "name");
        var pieceLength = GetRequiredInteger(dictionary, "piece length");
        var pieces = GetRequiredByteString(dictionary, "pieces").Value;

        var info = new TorrentInfo
        {
            Name = name,
            PieceLength = pieceLength,
            Pieces = pieces,
        };

        if (dictionary.TryGetValue("private", out var privateValue))
        {
            if (privateValue is not BencodeInteger privateInteger)
                throw new FormatException("The 'private' field must be an integer.");

            info.IsPrivate = privateInteger.Value != 0;
        }

        if (dictionary.TryGetValue("length", out var lengthValue))
        {
            if (lengthValue is not BencodeInteger lengthInteger)
                throw new FormatException("The 'length' field must be an integer.");

            info.Length = lengthInteger.Value;
        }

        if (dictionary.TryGetValue("files", out var filesValue))
        {
            if (filesValue is not BencodeList filesList)
                throw new FormatException("The 'files' field must be a list.");

            var files = new List<TorrentInfoFile>();
            foreach (var fileValue in filesList)
            {
                if (fileValue is not BencodeDictionary fileDictionary)
                    throw new FormatException("Each torrent file entry must be a dictionary.");

                var fileLength = GetRequiredInteger(fileDictionary, "length");
                if (!fileDictionary.TryGetValue("path", out var pathValue) || pathValue is not BencodeList pathList)
                    throw new FormatException("Each torrent file entry must contain a 'path' list.");

                var path = new List<string>();
                foreach (var segmentValue in pathList)
                {
                    if (segmentValue is not BencodeString segmentString)
                        throw new FormatException("Each path segment must be a bencode string.");

                    path.Add(segmentString.ToUtf8String());
                }

                files.Add(new TorrentInfoFile
                {
                    Length = fileLength,
                    Path = path,
                });
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
            { "name", new BencodeString(Encoding.UTF8.GetBytes(Name)) },
            { "piece length", new BencodeInteger(PieceLength) },
            { "pieces", new BencodeString(Pieces.ToArray()) },
        };

        if (IsPrivate)
        {
            dictionary.Add("private", new BencodeInteger(1));
        }

        if (Length.HasValue)
        {
            dictionary.Add("length", new BencodeInteger(Length.Value));
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
                    { "length", new BencodeInteger(file.Length) },
                    { "path", path },
                });
            }

            dictionary.Add("files", files);
        }

        return dictionary;
    }

    private void Validate()
    {
        if (string.IsNullOrEmpty(Name))
            throw new FormatException("Torrent info must contain a non-empty name.");

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
                    if (string.IsNullOrEmpty(segment))
                        throw new FormatException("Path segments cannot be null or empty.");
                }
            }
        }
    }

    private static long GetRequiredInteger(BencodeDictionary dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out var value) || value is not BencodeInteger integer)
            throw new FormatException($"The required '{key}' field is missing or not an integer.");

        return integer.Value;
    }

    private static string GetRequiredString(BencodeDictionary dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out var value) || value is not BencodeString text)
            throw new FormatException($"The required '{key}' field is missing or not a string.");

        return text.ToUtf8String();
    }

    private static BencodeString GetRequiredByteString(BencodeDictionary dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out var value) || value is not BencodeString text)
            throw new FormatException($"The required '{key}' field is missing or not a string.");

        return text;
    }
}
