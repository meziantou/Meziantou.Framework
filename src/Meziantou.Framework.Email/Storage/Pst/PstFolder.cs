using Meziantou.Framework.Email.Internals;
using Meziantou.Framework.Email.Internals.Ltp;
using Meziantou.Framework.Email.Internals.Ndb;

namespace Meziantou.Framework.Email.Storage;

/// <summary>A folder of an Outlook data file: its subfolders, its items and its associated (hidden) items.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../../tests/Meziantou.Framework.Email.Tests/PstFileTests.cs" ref:region="PstFolder_EnumerateItems" ref:dedent="true">
/// using var file = PstFile.Open(path);
/// var subjects = new List&lt;string&gt;();
/// void Visit(PstFolder folder)
/// {
///     foreach (var item in folder.EnumerateItems())
///     {
///         subjects.Add($"{folder.Path}: {item.Subject}");
///     }
///
///     foreach (var child in folder.Folders)
///     {
///         Visit(child);
///     }
/// }
///
/// Visit(file.RootFolder);
/// Assert.Contains("Top of Outlook data file/Inbox/tmp: original email", subjects);
/// </code>
/// </example>
public sealed class PstFolder
{
    private readonly PstContext _context;
    private IReadOnlyList<PstFolder>? _folders;

    private PstFolder(PstContext context, NdbNode node, PropertyContext pc, PstFolder? parent)
    {
        _context = context;
        Node = node;
        Parent = parent;
        Properties = new MapiPropertyCollection(new PstPropertySource(context, pc.Heap, node), pc.Properties);
    }

    internal NdbNode Node { get; }

    /// <summary>Gets the identifier of the node of the folder in the file.</summary>
    public uint NodeId => Node.Nid;

    /// <summary>Gets the parent folder, or <see langword="null"/> for the root folder.</summary>
    public PstFolder? Parent { get; }

    /// <summary>Gets the properties of the folder.</summary>
    public MapiPropertyCollection Properties { get; }

    /// <summary>Gets the display name of the folder.</summary>
    public string? DisplayName => Properties.GetString(PropertyIds.DisplayName);

    /// <summary>Gets the class of the items the folder contains, such as <c>IPF.Note</c> (mail), <c>IPF.Contact</c>, <c>IPF.Appointment</c> or <c>IPF.Task</c>.</summary>
    public string? ContainerClass => Properties.GetString(PropertyIds.ContainerClass);

    /// <summary>Gets the number of items, as stored in the folder.</summary>
    public int ContentCount => Properties.GetInt32(PropertyIds.ContentCount) ?? 0;

    /// <summary>Gets the number of unread items, as stored in the folder.</summary>
    public int UnreadCount => Properties.GetInt32(PropertyIds.ContentUnreadCount) ?? 0;

    /// <summary>Gets whether the folder has subfolders, as stored in the folder.</summary>
    public bool HasSubfolders => Properties.GetBoolean(PropertyIds.Subfolders) ?? false;

    /// <summary>Gets whether the folder is a search folder, whose items are the results of a search in other folders.</summary>
    public bool IsSearchFolder => Node.NidType == NidType.SearchFolder;

    /// <summary>Gets the path of the folder: the display names of the folders from the root folder (excluded), separated by <c>/</c>.</summary>
    public string Path
    {
        get
        {
            var names = new List<string>();
            for (var folder = this; folder?.Parent is not null; folder = folder.Parent)
            {
                names.Add(folder.DisplayName ?? "");
                if (names.Count > _context.Options.MaxTreeDepth)
                {
                    break;
                }
            }

            names.Reverse();
            return string.Join('/', names);
        }
    }

    /// <summary>Gets the subfolders.</summary>
    /// <exception cref="InvalidEmailDataException">The hierarchy table of the folder is malformed.</exception>
    public IReadOnlyList<PstFolder> Folders => _folders ??= LoadFolders();

    /// <summary>Returns a subfolder from its display name, ignoring case.</summary>
    /// <param name="name">The display name of the subfolder.</param>
    /// <returns>The subfolder, or <see langword="null"/> when the folder has no subfolder with this name.</returns>
    public PstFolder? GetFolder(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var folder in Folders)
        {
            if (string.Equals(folder.DisplayName, name, StringComparison.OrdinalIgnoreCase))
            {
                return folder;
            }
        }

        return null;
    }

    internal PstFolder? GetFolder(ReadOnlySpan<string> names)
    {
        var folder = this;
        foreach (var name in names)
        {
            folder = folder.GetFolder(name);
            if (folder is null)
            {
                return null;
            }
        }

        return folder;
    }

    /// <summary>Enumerates the items of the folder (messages, contacts, appointments...). Each item is read when the enumeration reaches it. A search folder has no items when Outlook stores its results in a structure that [MS-PST] does not document.</summary>
    /// <returns>The items, as <see cref="MapiMessage"/> or one of its derived classes for the items Outlook types (<see cref="MapiContact"/>, <see cref="MapiAppointment"/>, <see cref="MapiTask"/>, <see cref="MapiDistributionList"/>).</returns>
    /// <exception cref="InvalidEmailDataException">The contents table of the folder or an item is malformed.</exception>
    public IEnumerable<MapiMessage> EnumerateItems() => EnumerateItems(IsSearchFolder ? NidType.SearchContentsTable : NidType.ContentsTable);

    /// <summary>Enumerates the associated items of the folder: the hidden items that store views, rules or settings.</summary>
    /// <returns>The associated items.</returns>
    /// <exception cref="InvalidEmailDataException">The associated contents table of the folder or an item is malformed.</exception>
    public IEnumerable<MapiMessage> EnumerateAssociatedItems() => EnumerateItems(NidType.AssociatedContentsTable);

    /// <summary>Returns the display name of the folder.</summary>
    /// <returns>The display name of the folder.</returns>
    public override string ToString() => DisplayName ?? "";

    internal static PstFolder? Load(PstContext context, uint nid, PstFolder? parent)
    {
        var node = context.Reader.GetNode(nid);
        if (node is null)
        {
            return null;
        }

        return new PstFolder(context, node, new PropertyContext(node), parent);
    }

    /// <summary>Returns the rows of a table of the folder (hierarchy, contents...), or <see langword="null"/> when the folder does not have it.</summary>
    internal TableContext? GetTable(NidType type)
    {
        var node = _context.Reader.GetNode(Nids.WithType(Node.Nid, type));
        if (node is null)
        {
            return null;
        }

        // Outlook can store the results of a search folder in a structure that [MS-PST] does not document (signature 0xAC)
        if (type == NidType.SearchContentsTable && new HeapOnNode(node).ClientSignature != TableContext.Signature)
        {
            return null;
        }

        return new TableContext(node);
    }

    private List<PstFolder> LoadFolders()
    {
        var result = new List<PstFolder>();
        var table = GetTable(NidType.HierarchyTable);
        if (table is null)
        {
            return result;
        }

        var seen = new HashSet<uint>();
        for (var i = 0; i < table.RowCount; i++)
        {
            var nid = table.GetRowId(i);
            if (nid == Node.Nid || !seen.Add(nid) || (NidType)(nid & 0x1F) is not (NidType.NormalFolder or NidType.SearchFolder))
            {
                continue;
            }

            if (Load(_context, nid, this) is { } folder)
            {
                result.Add(folder);
            }
        }

        return result;
    }

    /// <summary>Enumerates a function per row of the contents table, which reads its item, or returns <see langword="null"/> when the row has no item: a malformed item throws when its function is called, and the next items can still be read.</summary>
    internal IEnumerable<Func<MapiMessage?>> EnumerateItemLoaders()
    {
        var table = GetTable(IsSearchFolder ? NidType.SearchContentsTable : NidType.ContentsTable);
        if (table is null)
        {
            yield break;
        }

        for (var i = 0; i < table.RowCount; i++)
        {
            var nid = table.GetRowId(i);
            yield return () => LoadItem(nid);
        }
    }

    private IEnumerable<MapiMessage> EnumerateItems(NidType tableType)
    {
        var table = GetTable(tableType);
        if (table is null)
        {
            yield break;
        }

        for (var i = 0; i < table.RowCount; i++)
        {
            if (LoadItem(table.GetRowId(i)) is { } item)
            {
                yield return item;
            }
        }
    }

    private MapiMessage? LoadItem(uint nid)
    {
        // A search folder can keep the rows of deleted messages
        var node = _context.Reader.GetNode(nid);
        return node is null ? null : PstMessageSource.Load(_context, node, embeddingDepth: 0);
    }
}
