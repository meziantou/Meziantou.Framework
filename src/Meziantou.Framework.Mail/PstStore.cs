using System.Buffers.Binary;
using Meziantou.Framework.Mail.Internals;
using Meziantou.Framework.Mail.Internals.Ltp;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail;

/// <summary>The message store of an Outlook data file: its display name, its properties and its special folders.</summary>
public sealed class PstStore
{
    private readonly PstFile _file;

    internal PstStore(PstFile file)
    {
        _file = file;
        var node = file.Reader.GetNode(Nids.MessageStore) ?? throw new InvalidPstException("The file has no message store (node 0x21).");
        var pc = new PropertyContext(node);
        Properties = new PstPropertyCollection(new PstPropertySource(file.Context, pc.Heap, node), pc.Properties);
        RootFolder = PstFolder.Load(file.Context, Nids.RootFolder, parent: null) ?? throw new InvalidPstException("The file has no root folder (node 0x122).");
    }

    /// <summary>Gets the properties of the store.</summary>
    public PstPropertyCollection Properties { get; }

    /// <summary>Gets the display name of the store, the name Outlook shows for the file.</summary>
    public string? DisplayName => Properties.GetString(PropertyIds.DisplayName);

    /// <summary>Gets the record key of the store, a GUID that identifies it in the entry identifiers of its objects.</summary>
    public Guid? RecordKey => Properties.GetBinary(PropertyIds.RecordKey) is { Length: 16 } key ? new Guid(key) : null;

    /// <summary>Gets whether the file is protected by a password. Outlook asks for it, but the content of the file is not encrypted with it.</summary>
    public bool HasPassword => Properties.GetInt32(PropertyIds.PstPassword) is int value && value != 0;

    /// <summary>Gets the root folder of the store.</summary>
    public PstFolder RootFolder { get; }

    /// <summary>Gets the top of the folder hierarchy, the parent of Inbox and the other folders Outlook shows, or <see langword="null"/> when the store does not specify it.</summary>
    public PstFolder? IpmSubtree => GetFolder(PropertyIds.IpmSubtreeEntryId);

    /// <summary>Gets the Deleted Items folder, or <see langword="null"/> when the store does not specify it.</summary>
    public PstFolder? DeletedItems => GetFolder(PropertyIds.IpmWastebasketEntryId);

    /// <summary>Gets the root of the search folders, or <see langword="null"/> when the store does not specify it.</summary>
    public PstFolder? SearchRoot => GetFolder(PropertyIds.FinderEntryId);

    private PstFolder? GetFolder(ushort entryIdProperty)
    {
        var entryId = Properties.GetBinary(entryIdProperty);
        if (entryId is not { Length: 24 })
        {
            return null;
        }

        var nid = BinaryPrimitives.ReadUInt32LittleEndian(entryId.AsSpan(20));
        return FindFolder(RootFolder, nid, depth: 0, []);
    }

    private PstFolder? FindFolder(PstFolder folder, uint nid, int depth, HashSet<uint> visited)
    {
        if (!visited.Add(folder.NodeId))
        {
            return null;
        }

        if (folder.NodeId == nid)
        {
            return folder;
        }

        // The folder objects are created through their parent, so that each one knows its parent
        if (depth > _file.Context.Options.MaxTreeDepth)
        {
            return null;
        }

        foreach (var child in folder.Folders)
        {
            if (FindFolder(child, nid, depth + 1, visited) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
