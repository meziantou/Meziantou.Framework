namespace Meziantou.Framework.Email.Internals.Cfb;

/// <summary>An entry of the directory of a compound file: a storage (a folder) or a stream (a file).</summary>
internal sealed class CfbEntry
{
    private readonly CompoundFile _file;
    private Dictionary<string, CfbEntry>? _children;

    internal CfbEntry(CompoundFile file, int id, string name, CfbEntryType type, uint startSector, long size, int left, int right, int child)
    {
        _file = file;
        Id = id;
        Name = name;
        Type = type;
        StartSector = startSector;
        Size = size;
        Left = left;
        Right = right;
        Child = child;
    }

    public int Id { get; }

    public string Name { get; }

    public CfbEntryType Type { get; }

    public bool IsStorage => Type is CfbEntryType.Storage or CfbEntryType.Root;

    public bool IsStream => Type == CfbEntryType.Stream;

    internal uint StartSector { get; }

    public long Size { get; }

    internal int Left { get; }

    internal int Right { get; }

    internal int Child { get; }

    /// <summary>Gets the entries of a storage, by name (the names of a storage are compared without case).</summary>
    public IReadOnlyDictionary<string, CfbEntry> Children => _children ??= _file.GetChildren(this);

    public CfbEntry? GetChild(string name) => Children.GetValueOrDefault(name);

    public byte[] ReadStream() => _file.ReadStream(this);

    public override string ToString() => Name;
}
