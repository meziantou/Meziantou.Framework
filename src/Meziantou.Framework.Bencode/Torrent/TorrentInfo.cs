namespace Meziantou.Framework.Bencode.Torrent;

public sealed class TorrentInfo
{
    private const int PiecesRootLength = 32;
    private const int MinV2PieceLength = 16 * 1024;

    private static readonly BencodeString NameKey = TorrentField.CreateKey("name");
    private static readonly BencodeString PieceLengthKey = TorrentField.CreateKey("piece length");
    private static readonly BencodeString PiecesKey = TorrentField.CreateKey("pieces");
    private static readonly BencodeString PrivateKey = TorrentField.CreateKey("private");
    private static readonly BencodeString LengthKey = TorrentField.CreateKey("length");
    private static readonly BencodeString FilesKey = TorrentField.CreateKey("files");
    private static readonly BencodeString PathKey = TorrentField.CreateKey("path");
    private static readonly BencodeString SourceKey = TorrentField.CreateKey("source");
    private static readonly BencodeString Md5SumKey = TorrentField.CreateKey("md5sum");
    private static readonly BencodeString MetaVersionKey = TorrentField.CreateKey("meta version");
    private static readonly BencodeString FileTreeKey = TorrentField.CreateKey("file tree");
    private static readonly BencodeString PiecesRootKey = TorrentField.CreateKey("pieces root");
    private static readonly BencodeString FileTreeFileKey = new(ReadOnlyMemory<byte>.Empty);

    private static readonly HashSet<BencodeString> ModeledKeys = TorrentField.CreateKeySet(NameKey, PieceLengthKey, PiecesKey, PrivateKey, LengthKey, FilesKey, SourceKey, Md5SumKey, MetaVersionKey, FileTreeKey);
    private static readonly HashSet<BencodeString> FileModeledKeys = TorrentField.CreateKeySet(LengthKey, PathKey, Md5SumKey);
    private static readonly HashSet<BencodeString> FileTreeEntryModeledKeys = TorrentField.CreateKeySet(LengthKey, PiecesRootKey);

    public string Name { get; set; } = "";

    public long PieceLength { get; set; }

    /// <summary>The SHA-1 hashes of the pieces (BitTorrent v1). Empty for a BitTorrent v2 only torrent.</summary>
    public ReadOnlyMemory<byte> Pieces { get; set; }

    public bool IsPrivate { get; set; }

    public long? Length { get; set; }

    public IReadOnlyList<TorrentInfoFile>? Files { get; set; }

    /// <summary>The 'source' key many private trackers add to make the info-hash unique to them.</summary>
    public string? Source { get; set; }

    /// <summary>The 'md5sum' of a single-file torrent's content, when the torrent carries one.</summary>
    public string? Md5Sum { get; set; }

    /// <summary>The 'meta version' key (BEP 52): 2 for a BitTorrent v2 or hybrid torrent.</summary>
    public int? MetaVersion { get; set; }

    /// <summary>The files of the BitTorrent v2 'file tree' key (BEP 52), in the order of the tree.</summary>
    public IReadOnlyList<TorrentFileTreeEntry>? FileTree { get; set; }

    /// <summary>The keys of the 'info' dictionary this model does not represent. They are written back as they are.</summary>
    public IDictionary<BencodeString, BencodeValue> AdditionalKeys { get; } = new Dictionary<BencodeString, BencodeValue>();

    internal static TorrentInfo Parse(BencodeDictionary dictionary, Encoding? encoding)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        var info = new TorrentInfo
        {
            Name = GetRequiredString(dictionary, NameKey, "name", encoding),
            PieceLength = GetRequiredInteger(dictionary, PieceLengthKey, "piece length"),
        };

        if (dictionary.TryGetValue(PiecesKey, out var piecesValue))
        {
            if (piecesValue is not BencodeString pieces)
                throw new FormatException("The 'pieces' field must be a string.");

            info.Pieces = pieces.Value;
        }

        if (dictionary.TryGetValue(PrivateKey, out var privateValue))
        {
            if (privateValue is not BencodeInteger privateInteger)
                throw new FormatException("The 'private' field must be an integer.");

            info.IsPrivate = !privateInteger.BigIntegerValue.IsZero;
        }

        if (dictionary.TryGetValue(SourceKey, out var sourceValue))
        {
            if (sourceValue is not BencodeString sourceText)
                throw new FormatException("The 'source' field must be a string.");

            info.Source = TorrentField.ToText(sourceText, "source", encoding);
        }

        if (dictionary.TryGetValue(Md5SumKey, out var md5Value))
        {
            if (md5Value is not BencodeString md5Text)
                throw new FormatException("The 'md5sum' field must be a string.");

            info.Md5Sum = TorrentField.ToText(md5Text, "md5sum", encoding);
        }

        if (dictionary.TryGetValue(LengthKey, out var lengthValue))
        {
            if (lengthValue is not BencodeInteger lengthInteger)
                throw new FormatException("The 'length' field must be an integer.");

            info.Length = TorrentField.ToInt64(lengthInteger, "length");
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

                    path.Add(TorrentField.ToText(segmentString, "path", encoding));
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

                    file.Md5Sum = TorrentField.ToText(fileMd5Text, "md5sum", encoding);
                }

                TorrentField.ReadAdditionalKeys(fileDictionary, FileModeledKeys, file.AdditionalKeys);
                files.Add(file);
            }

            info.Files = files;
        }

        if (dictionary.TryGetValue(MetaVersionKey, out var metaVersionValue))
        {
            if (metaVersionValue is not BencodeInteger metaVersionInteger)
                throw new FormatException("The 'meta version' field must be an integer.");

            if (!metaVersionInteger.TryGetInt64(out var metaVersion) || metaVersion is < int.MinValue or > int.MaxValue)
                throw new FormatException("The 'meta version' field is out of range.");

            info.MetaVersion = (int)metaVersion;
        }

        if (dictionary.TryGetValue(FileTreeKey, out var fileTreeValue))
        {
            if (fileTreeValue is not BencodeDictionary fileTree)
                throw new FormatException("The 'file tree' field must be a dictionary.");

            var entries = new List<TorrentFileTreeEntry>();
            ReadFileTree(fileTree, [], entries, encoding);
            info.FileTree = entries;
        }

        TorrentField.ReadAdditionalKeys(dictionary, ModeledKeys, info.AdditionalKeys);
        info.Validate();
        return info;
    }

    internal BencodeDictionary ToBencodeDictionary(Encoding? encoding)
    {
        Validate();

        var dictionary = new BencodeDictionary
        {
            { NameKey, TorrentField.FromText(Name, "name", encoding) },
            { PieceLengthKey, new BencodeInteger(PieceLength) },
        };

        if (!Pieces.IsEmpty)
        {
            dictionary.Add(PiecesKey, new BencodeString(Pieces.ToArray()));
        }

        if (IsPrivate)
        {
            dictionary.Add(PrivateKey, new BencodeInteger(1));
        }

        if (Source is not null)
        {
            dictionary.Add(SourceKey, TorrentField.FromText(Source, "source", encoding));
        }

        if (Md5Sum is not null)
        {
            dictionary.Add(Md5SumKey, TorrentField.FromText(Md5Sum, "md5sum", encoding));
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
                var path = new BencodeList(file.Path.Select(segment => (BencodeValue)TorrentField.FromText(segment, "path", encoding)));
                var fileDictionary = new BencodeDictionary
                {
                    { LengthKey, new BencodeInteger(file.Length) },
                    { PathKey, path },
                };

                if (file.Md5Sum is not null)
                {
                    fileDictionary.Add(Md5SumKey, TorrentField.FromText(file.Md5Sum, "md5sum", encoding));
                }

                TorrentField.WriteAdditionalKeys(fileDictionary, file.AdditionalKeys, FileModeledKeys);
                files.Add(fileDictionary);
            }

            dictionary.Add(FilesKey, files);
        }

        if (MetaVersion.HasValue)
        {
            dictionary.Add(MetaVersionKey, new BencodeInteger(MetaVersion.Value));
        }

        if (FileTree is not null)
        {
            dictionary.Add(FileTreeKey, WriteFileTree(FileTree, encoding));
        }

        TorrentField.WriteAdditionalKeys(dictionary, AdditionalKeys, ModeledKeys);
        return dictionary;
    }

    /// <summary>Flattens the 'file tree': a dictionary whose keys are path segments, and whose files are a dictionary with a single empty key.</summary>
    private static void ReadFileTree(BencodeDictionary directory, List<string> path, List<TorrentFileTreeEntry> entries, Encoding? encoding)
    {
        if (directory.Count is 0)
            throw new FormatException("A 'file tree' directory must contain at least one entry.");

        foreach (var (key, value) in directory)
        {
            if (key.Value.IsEmpty)
                throw new FormatException("A 'file tree' entry cannot be both a file and a directory.");

            if (value is not BencodeDictionary node)
                throw new FormatException("Each 'file tree' entry must be a dictionary.");

            var segment = TorrentField.ToText(key, "file tree", encoding);
            ValidatePathSegment(segment, "file tree");
            List<string> childPath = [.. path, segment];

            if (!node.TryGetValue(FileTreeFileKey, out var fileValue))
            {
                ReadFileTree(node, childPath, entries, encoding);
                continue;
            }

            if (node.Count is not 1)
                throw new FormatException("A 'file tree' entry cannot be both a file and a directory.");

            if (fileValue is not BencodeDictionary fileDictionary)
                throw new FormatException("A 'file tree' file must be a dictionary.");

            var entry = new TorrentFileTreeEntry
            {
                Path = childPath,
                Length = GetRequiredInteger(fileDictionary, LengthKey, "length"),
            };

            if (fileDictionary.TryGetValue(PiecesRootKey, out var piecesRootValue))
            {
                if (piecesRootValue is not BencodeString piecesRoot)
                    throw new FormatException("The 'pieces root' field must be a string.");

                entry.PiecesRoot = piecesRoot.Value;
            }

            TorrentField.ReadAdditionalKeys(fileDictionary, FileTreeEntryModeledKeys, entry.AdditionalKeys);
            entries.Add(entry);
        }
    }

    private static BencodeDictionary WriteFileTree(IReadOnlyList<TorrentFileTreeEntry> entries, Encoding? encoding)
    {
        var root = new BencodeDictionary();
        foreach (var entry in entries)
        {
            var directory = root;
            for (var i = 0; i < entry.Path.Count; i++)
            {
                var key = TorrentField.FromText(entry.Path[i], "file tree", encoding);
                var isFile = i == entry.Path.Count - 1;
                if (directory.TryGetValue(key, out var existing))
                {
                    if (isFile || existing is not BencodeDictionary existingDirectory || existingDirectory.ContainsKey(FileTreeFileKey))
                        throw new FormatException("Two 'file tree' entries have the same path, or a file is also a directory.");

                    directory = existingDirectory;
                    continue;
                }

                if (isFile)
                {
                    var fileDictionary = new BencodeDictionary
                    {
                        { LengthKey, new BencodeInteger(entry.Length) },
                    };

                    if (!entry.PiecesRoot.IsEmpty)
                    {
                        fileDictionary.Add(PiecesRootKey, new BencodeString(entry.PiecesRoot.ToArray()));
                    }

                    TorrentField.WriteAdditionalKeys(fileDictionary, entry.AdditionalKeys, FileTreeEntryModeledKeys);
                    directory.Add(key, new BencodeDictionary { { FileTreeFileKey, fileDictionary } });
                }
                else
                {
                    var child = new BencodeDictionary();
                    directory.Add(key, child);
                    directory = child;
                }
            }
        }

        return root;
    }

    private void Validate()
    {
        if (string.IsNullOrEmpty(Name))
            throw new FormatException("Torrent info must contain a non-empty name.");

        ValidatePathSegment(Name, "name");

        if (PieceLength <= 0)
            throw new FormatException("Torrent info must contain a positive piece length.");

        if (FileTree is not null && MetaVersion is not 2)
            throw new FormatException("A torrent with a 'file tree' must have a 'meta version' of 2.");

        if (MetaVersion is 2 && FileTree is null)
            throw new FormatException("A torrent with a 'meta version' of 2 must have a 'file tree'.");

        // A BitTorrent v2 only torrent has none of the v1 keys; a v1 or hybrid torrent has all of them
        var hasV1 = FileTree is null || !Pieces.IsEmpty || Length.HasValue || Files is not null;
        if (hasV1)
        {
            ValidateV1();
        }

        if (FileTree is not null)
        {
            ValidateV2(FileTree);
        }
    }

    private void ValidateV1()
    {
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

                ValidatePath(file.Path, "path");
            }
        }
    }

    private void ValidateV2(IReadOnlyList<TorrentFileTreeEntry> fileTree)
    {
        // BEP 52: the piece length is a power of two of at least 16 KiB
        if (PieceLength < MinV2PieceLength || (PieceLength & (PieceLength - 1)) != 0)
            throw new FormatException("The piece length of a BitTorrent v2 torrent must be a power of two of at least 16 KiB.");

        if (fileTree.Count is 0)
            throw new FormatException("The 'file tree' field must contain at least one file.");

        foreach (var entry in fileTree)
        {
            if (entry is null)
                throw new FormatException("File tree entries cannot be null.");

            if (entry.Length < 0)
                throw new FormatException("File tree lengths cannot be negative.");

            if (entry.Length > 0 ? entry.PiecesRoot.Length != PiecesRootLength : !entry.PiecesRoot.IsEmpty)
                throw new FormatException("The 'pieces root' of a file must be 32 bytes, and absent for an empty file.");

            ValidatePath(entry.Path, "file tree");
        }
    }

    private static void ValidatePath(IReadOnlyList<string>? path, string fieldName)
    {
        if (path is null || path.Count == 0)
            throw new FormatException("Each torrent file must contain at least one path segment.");

        foreach (var segment in path)
        {
            ValidatePathSegment(segment, fieldName);
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

        return TorrentField.ToInt64(integer, fieldName);
    }

    private static string GetRequiredString(BencodeDictionary dictionary, BencodeString key, string fieldName, Encoding? encoding)
    {
        if (!dictionary.TryGetValue(key, out var value) || value is not BencodeString text)
            throw new FormatException($"The required '{fieldName}' field is missing or not a string.");

        return TorrentField.ToText(text, fieldName, encoding);
    }
}
