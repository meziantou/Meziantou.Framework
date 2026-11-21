namespace Meziantou.Framework.Email.Internals.Cfb;

/// <summary>A storage to write in a compound file: its storages and its streams.</summary>
internal sealed class CfbWriterStorage
{
    public CfbWriterStorage(string name)
    {
        if (name.Length > 31)
        {
            throw new ArgumentException($"The name '{name}' is longer than the 31 characters of a compound file.", nameof(name));
        }

        Name = name;
    }

    public string Name { get; }

    /// <summary>Gets or sets the class identifier of the storage, which tells the application that created it.</summary>
    public Guid Clsid { get; set; }

    public List<CfbWriterStorage> Storages { get; } = [];

    public List<(string Name, byte[] Data)> Streams { get; } = [];

    public CfbWriterStorage AddStorage(string name)
    {
        var storage = new CfbWriterStorage(name);
        Storages.Add(storage);
        return storage;
    }

    public void AddStream(string name, byte[] data)
    {
        if (name.Length > 31)
        {
            throw new ArgumentException($"The name '{name}' is longer than the 31 characters of a compound file.", nameof(name));
        }

        Streams.Add((name, data));
    }
}
