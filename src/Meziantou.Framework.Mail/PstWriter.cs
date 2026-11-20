using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Mail.Internals;
using Meziantou.Framework.Mail.Internals.Ltp;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail;

/// <summary>Writes a new Outlook data file. The items are written when they are added; the folders, the indexes and the header when the writer is completed.</summary>
/// <remarks>The writer creates new files: it does not modify an existing file. Use <see cref="PstMessageBuilder.FromMessage"/> to copy items from another file.</remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Mail.Tests/PstWriterTests.cs" ref:region="PstWriter_Create" ref:dedent="true">
/// using (var writer = PstWriter.Create(path, new PstWriterOptions { DisplayName = "Archive" }))
/// {
///     var inbox = writer.RootFolder.CreateFolder("Inbox", "IPF.Note");
///     var message = new PstMessageBuilder
///     {
///         Subject = "Hello",
///         Body = "Hello from Meziantou.Framework.Mail",
///         SenderName = "Alice",
///         SenderEmailAddress = "alice@example.com",
///         SenderAddressType = "SMTP",
///         ClientSubmitTime = new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc),
///     };
///     message.Recipients.Add(new PstRecipientBuilder("Bob", "bob@example.com"));
///     message.Attachments.Add(PstAttachmentBuilder.FromContent("notes.txt", "Some notes"u8.ToArray(), "text/plain"));
///     inbox.AddItem(message);
///
///     var contacts = writer.RootFolder.CreateFolder("Contacts", "IPF.Contact");
///     contacts.AddItem(new PstContactBuilder { GivenName = "Bob", Surname = "Smith", Email1Address = "bob@example.com" });
/// }
///
/// using var file = PstFile.Open(path);
/// var item = file.GetFolder("Inbox")!.EnumerateItems().Single();
/// Assert.Equal("Hello", item.Subject);
/// Assert.Equal("bob@example.com", item.Recipients.Single().EmailAddress);
/// Assert.Equal("notes.txt", item.Attachments.Single().Name);
/// </code>
/// </example>
public sealed class PstWriter : IDisposable
{
    private const uint IpmSubtreeNid = 0x8022;
    private const uint SearchRootNid = 0x8042;
    private const uint DeletedItemsNid = 0x8062;
    private const uint SpamSearchFolderNid = 0x2223;
    private const uint SearchDomainObjectNid = 0x261;

    private static readonly (ushort Id, PstPropertyType Type)[] HierarchyColumns =
    [
        (0x0E30, PstPropertyType.Integer32), (0x0E33, PstPropertyType.Integer64), (0x0E34, PstPropertyType.Binary), (0x0E38, PstPropertyType.Integer32),
        (PropertyIds.DisplayName, PstPropertyType.Unicode), (PropertyIds.ContentCount, PstPropertyType.Integer32), (PropertyIds.ContentUnreadCount, PstPropertyType.Integer32),
        (PropertyIds.Subfolders, PstPropertyType.Boolean), (PropertyIds.ContainerClass, PstPropertyType.Unicode), (0x6635, PstPropertyType.Integer32), (0x6636, PstPropertyType.Integer32),
    ];

    private static readonly (ushort Id, PstPropertyType Type)[] ContentsColumns =
    [
        (PropertyIds.Importance, PstPropertyType.Integer32), (PropertyIds.MessageClass, PstPropertyType.Unicode), (PropertyIds.Sensitivity, PstPropertyType.Integer32),
        (PropertyIds.Subject, PstPropertyType.Unicode), (PropertyIds.ClientSubmitTime, PstPropertyType.Time), (PropertyIds.SentRepresentingName, PstPropertyType.Unicode),
        (0x0057, PstPropertyType.Boolean), (0x0058, PstPropertyType.Boolean), (PropertyIds.ConversationTopic, PstPropertyType.Unicode), (PropertyIds.ConversationIndex, PstPropertyType.Binary),
        (PropertyIds.DisplayCc, PstPropertyType.Unicode), (PropertyIds.DisplayTo, PstPropertyType.Unicode), (PropertyIds.MessageDeliveryTime, PstPropertyType.Time),
        (PropertyIds.MessageFlags, PstPropertyType.Integer32), (PropertyIds.MessageSize, PstPropertyType.Integer32), (PropertyIds.MessageStatus, PstPropertyType.Integer32),
        (0x0E30, PstPropertyType.Integer32), (0x0E33, PstPropertyType.Integer64), (0x0E34, PstPropertyType.Binary), (0x0E38, PstPropertyType.Integer32),
        (0x0E3C, PstPropertyType.Binary), (0x0E3D, PstPropertyType.Binary), (0x1097, PstPropertyType.Integer32), (PropertyIds.LastModificationTime, PstPropertyType.Time),
        (0x65C6, PstPropertyType.Integer32),
    ];

    private static readonly (ushort Id, PstPropertyType Type)[] AssociatedContentsColumns =
    [
        (PropertyIds.MessageClass, PstPropertyType.Unicode), (PropertyIds.MessageFlags, PstPropertyType.Integer32), (PropertyIds.MessageStatus, PstPropertyType.Integer32),
        (PropertyIds.DisplayName, PstPropertyType.Unicode), (0x6800, PstPropertyType.Unicode), (0x6803, PstPropertyType.Boolean), (0x6805, PstPropertyType.MultipleInteger32),
        (0x7003, PstPropertyType.Integer32), (0x7004, PstPropertyType.Binary), (0x7005, PstPropertyType.Binary), (0x7006, PstPropertyType.Unicode), (0x7007, PstPropertyType.Integer32),
    ];

    private static readonly (ushort Id, PstPropertyType Type)[] SearchContentsColumns =
    [
        (PropertyIds.Importance, PstPropertyType.Integer32), (PropertyIds.MessageClass, PstPropertyType.Unicode), (PropertyIds.Sensitivity, PstPropertyType.Integer32),
        (PropertyIds.MessageFlags, PstPropertyType.Integer32), (PropertyIds.MessageStatus, PstPropertyType.Integer32), (PropertyIds.Subject, PstPropertyType.Unicode),
        (PropertyIds.SentRepresentingName, PstPropertyType.Unicode), (0x0057, PstPropertyType.Boolean), (PropertyIds.DisplayCc, PstPropertyType.Unicode),
        (PropertyIds.DisplayTo, PstPropertyType.Unicode), (0x0E05, PstPropertyType.Unicode), (PropertyIds.MessageDeliveryTime, PstPropertyType.Time),
        (PropertyIds.MessageSize, PstPropertyType.Integer32), (0x0E2A, PstPropertyType.Boolean), (PropertyIds.LastModificationTime, PstPropertyType.Time), (0x67F1, PstPropertyType.Integer32),
    ];

    private static readonly (ushort Id, PstPropertyType Type)[] RecipientColumns =
    [
        (PropertyIds.RecipientType, PstPropertyType.Integer32), (0x0E0F, PstPropertyType.Boolean), (PropertyIds.RecordKey, PstPropertyType.Binary), (0x0FFE, PstPropertyType.Integer32),
        (PropertyIds.EntryId, PstPropertyType.Binary), (PropertyIds.DisplayName, PstPropertyType.Unicode), (PropertyIds.AddressType, PstPropertyType.Unicode),
        (PropertyIds.EmailAddress, PstPropertyType.Unicode), (PropertyIds.SearchKey, PstPropertyType.Binary), (PropertyIds.DisplayType, PstPropertyType.Integer32),
        (0x39FF, PstPropertyType.Unicode), (0x3A40, PstPropertyType.Boolean),
    ];

    private static readonly (ushort Id, PstPropertyType Type)[] AttachmentColumns =
    [
        (PropertyIds.AttachSize, PstPropertyType.Integer32), (PropertyIds.AttachFilename, PstPropertyType.Unicode), (PropertyIds.AttachMethod, PstPropertyType.Integer32),
        (PropertyIds.RenderingPosition, PstPropertyType.Integer32),
    ];

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly NdbWriter _ndb;
    private readonly NameToIdMapWriter _names = new();
    private readonly Encoding _string8Encoding;
    private readonly bool _ansi;
    private readonly byte[] _storeId;
    private readonly DateTime _now;
    private readonly string _storeDisplayName;
    private readonly PstWriterFolder _rootFolder;
    private readonly PstWriterFolder _searchRoot;
    private readonly PstWriterFolder _spamSearchFolder;
    private uint _searchKeyCounter;
    private bool _completed;
    private bool _disposed;

    private PstWriter(Stream stream, bool leaveOpen, PstWriterOptions options)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        _ndb = new NdbWriter(stream, options.Format, options.Encryption);
        _ansi = options.Format == PstFormat.Ansi;
        _string8Encoding = PstContext.GetEncoding(options.CodePage);
        _storeId = (options.StoreId ?? Guid.NewGuid()).ToByteArray();
        // A date without a kind is UTC, as for the values of the properties (PropertyValueCodec.EncodeFileTime)
        _now = options.Now switch
        {
            { Kind: DateTimeKind.Local } now => now.ToUniversalTime(),
            { } now => DateTime.SpecifyKind(now, DateTimeKind.Utc),
            null => DateTime.UtcNow,
        };
        _storeDisplayName = options.DisplayName;

        // Outlook always maps the categories: some readers expect the string stream of the name-to-ID map to be non-empty
        _names.GetId(NamedProperties.Keywords.Name);

        foreach (var nid in new[] { Nids.MessageStore, Nids.NameToIdMap, Nids.RootFolder, Nids.SearchManagementQueue, Nids.SearchActivityList, SearchDomainObjectNid, IpmSubtreeNid, SearchRootNid, DeletedItemsNid, SpamSearchFolderNid })
        {
            _ndb.ReserveNid(nid);
        }

        _rootFolder = new PstWriterFolder(this, Nids.RootFolder, parent: null, "", containerClass: null);
        IpmSubtree = AddSpecialFolder(_rootFolder, IpmSubtreeNid, options.RootFolderName);
        _searchRoot = AddSpecialFolder(_rootFolder, SearchRootNid, "Search Root");
        _spamSearchFolder = new PstWriterFolder(this, SpamSearchFolderNid, _rootFolder, "SPAM Search Folder 2", containerClass: null, isSearchFolder: true);
        SpecialChildren[_rootFolder].Add(_spamSearchFolder);
        DeletedItems = AddSpecialFolder(IpmSubtree, DeletedItemsNid, "Deleted Items");
    }

    /// <summary>Gets the top of the folder hierarchy ("Top of Personal Folders"), the folder that contains the folders Outlook shows.</summary>
    public PstWriterFolder RootFolder => IpmSubtree;

    /// <summary>Gets the Deleted Items folder.</summary>
    public PstWriterFolder DeletedItems { get; }

    private PstWriterFolder IpmSubtree { get; }

    private Dictionary<PstWriterFolder, List<PstWriterFolder>> SpecialChildren { get; } = [];

    /// <summary>Creates a file.</summary>
    /// <param name="path">The path of the file. An existing file is overwritten.</param>
    /// <param name="options">The options of the writer, or <see langword="null"/> for the default options.</param>
    /// <returns>The writer.</returns>
    public static PstWriter Create(string path, PstWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        return Create(stream, options, leaveOpen: false);
    }

    /// <summary>Creates a file in a stream.</summary>
    /// <param name="stream">A writable and seekable stream. The file starts at position 0.</param>
    /// <param name="options">The options of the writer, or <see langword="null"/> for the default options.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave the stream open when the writer is disposed.</param>
    /// <returns>The writer.</returns>
    /// <exception cref="ArgumentException">The stream is not writable or not seekable.</exception>
    public static PstWriter Create(Stream stream, PstWriterOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek || !stream.CanWrite)
        {
            throw new ArgumentException("The stream must be writable and seekable.", nameof(stream));
        }

        options ??= PstWriterOptions.Default;
        if (!Enum.IsDefined(options.Format) || !Enum.IsDefined(options.Encryption))
        {
            throw new ArgumentException("The format or the encryption of the options is not valid.", nameof(options));
        }

        return new PstWriter(stream, leaveOpen, options);
    }

    /// <summary>Writes the folders, the indexes and the header. The file is complete and valid after this call.</summary>
    /// <exception cref="InvalidOperationException">The writer is already completed, or the content is too large for the format.</exception>
    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new InvalidOperationException("The writer is already completed.");
        }

        _completed = true;
        WriteFolder(_rootFolder);
        WriteStore();
        WriteTemplates();
        foreach (var nid in new[] { Nids.SearchManagementQueue, Nids.SearchActivityList, SearchDomainObjectNid })
        {
            _ndb.AddNode(nid, 0, 0, 0);
        }

        var nameMap = new NodeContent();
        PropertyContextWriter.Write(nameMap, _names.CreateProperties().Select(property => new WriterProperty(property.Tag, property.Value)), _ndb.Layout.MaxLeafDataSize);
        AddNode(Nids.NameToIdMap, nameMap, 0);

        _ndb.Complete();
    }

    /// <summary>Completes the writer when it is not completed, and closes the stream unless it was created with <c>leaveOpen</c>.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (!_completed)
            {
                Complete();
            }
        }
        finally
        {
            _disposed = true;
            if (!_leaveOpen)
            {
                _stream.Dispose();
            }
        }
    }

    internal void EnsureWritable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new InvalidOperationException("The writer is completed.");
        }
    }

    internal uint AllocateFolderNid() => _ndb.AllocateNid(NidType.NormalFolder);

    internal void WriteItem(PstWriterFolder folder, PstMessageBuilder item)
    {
        var nid = _ndb.AllocateNid(item.IsAssociated ? NidType.AssociatedMessage : NidType.NormalMessage);
        var (content, properties) = SerializeMessage(item, depth: 0);
        AddNode(nid, content, folder.NodeId);

        var template = item.IsAssociated ? AssociatedContentsColumns : ContentsColumns;
        var cells = properties.Where(property => template.Any(column => column.Id == property.Tag.Id)).ToList();
        if (item.IsAssociated)
        {
            folder.AssociatedItems.Add((nid, cells));
        }
        else
        {
            folder.Items.Add((nid, cells));
            var flags = properties.Find(property => property.Tag.Id == PropertyIds.MessageFlags).Value;
            if (flags is not null && (BinaryPrimitives.ReadInt32LittleEndian(flags) & (int)PstMessageFlags.Read) == 0)
            {
                folder.UnreadCount++;
            }
        }
    }

    private PstWriterFolder AddSpecialFolder(PstWriterFolder parent, uint nid, string name)
    {
        var folder = new PstWriterFolder(this, nid, parent, name, containerClass: null);
        if (!SpecialChildren.TryGetValue(parent, out var children))
        {
            children = [];
            SpecialChildren[parent] = children;
        }

        children.Add(folder);
        return folder;
    }

    private IEnumerable<PstWriterFolder> GetChildren(PstWriterFolder folder) => (SpecialChildren.TryGetValue(folder, out var special) ? special : []).Concat(folder.Folders);

    /// <summary>Serializes a message: its property context, its recipient table and its attachments. Returns the content of its node and its properties.</summary>
    private (NodeContent Content, List<WriterProperty> Properties) SerializeMessage(PstMessageBuilder item, int depth)
    {
        if (depth > 32)
        {
            throw new InvalidOperationException("The embedded messages are nested too deeply.");
        }

        switch (item)
        {
            case PstContactBuilder contact:
                contact.Complete();
                break;
            case PstDistributionListBuilder list:
                list.Complete();
                break;
            case PstAppointmentBuilder appointment:
                appointment.Complete();
                break;
        }

        var node = new NodeContent();
        var properties = item.Properties.Encode(_names, _string8Encoding, _ansi);
        long size = 0;

        // The recipient table: a message always has one
        var recipientRows = new List<(uint RowId, IReadOnlyList<WriterProperty> Cells)>();
        var to = new List<string>();
        var cc = new List<string>();
        var bcc = new List<string>();
        for (var i = 0; i < item.Recipients.Count; i++)
        {
            var recipient = item.Recipients[i];
            var cells = recipient.Properties.Encode(_names, _string8Encoding, _ansi);
            SetDefault(cells, PropertyIds.RecipientType, PstPropertyType.Integer32, (int)PstRecipientType.To);
            SetDefault(cells, 0x0FFE, PstPropertyType.Integer32, 6); // MAPI_MAILUSER
            SetDefault(cells, PropertyIds.DisplayType, PstPropertyType.Integer32, 0);
            SetDefault(cells, PropertyIds.EntryId, PstPropertyType.Binary, EntryIds.CreateOneOff(recipient.DisplayName, recipient.AddressType, recipient.EmailAddress));
            var name = recipient.DisplayName ?? recipient.EmailAddress ?? "";
            (recipient.RecipientType switch { PstRecipientType.Cc => cc, PstRecipientType.Bcc => bcc, PstRecipientType.Originator => [], _ => to }).Add(name);
            recipientRows.Add(((uint)i, cells));
            size += cells.Sum(cell => (long)(cell.Value?.Length ?? 0));
        }

        var recipientTable = new NodeContent();
        TableContextWriter.Write(recipientTable, Columns(RecipientColumns), recipientRows, _ndb.Layout);
        node.Subnodes[Nids.RecipientTable] = recipientTable;

        // The attachments
        if (item.Attachments.Count > 0)
        {
            var attachmentRows = new List<(uint RowId, IReadOnlyList<WriterProperty> Cells)>();
            foreach (var attachment in item.Attachments)
            {
                var nid = node.AllocateSubnodeNid(NidType.Attachment);
                var (attachmentNode, attachmentProperties, attachmentSize) = SerializeAttachment(attachment, depth);
                node.Subnodes[nid] = attachmentNode;
                attachmentRows.Add((nid, attachmentProperties.Where(property => AttachmentColumns.Any(column => column.Id == property.Tag.Id)).ToList()));
                size += attachmentSize;
            }

            var attachmentTable = new NodeContent();
            TableContextWriter.Write(attachmentTable, Columns(AttachmentColumns), attachmentRows, _ndb.Layout);
            node.Subnodes[Nids.AttachmentTable] = attachmentTable;
        }

        // The properties Outlook requires ([MS-PST] 2.4.5.1.1), and the ones it computes from the recipients
        var flags = item.Flags;
        if (item.Attachments.Count > 0)
        {
            flags |= PstMessageFlags.HasAttachments;
        }

        if (item.IsAssociated)
        {
            flags |= PstMessageFlags.Associated;
        }

        Set(properties, PropertyIds.MessageFlags, PstPropertyType.Integer32, (int)flags);
        SetDefault(properties, PropertyIds.MessageStatus, PstPropertyType.Integer32, 0);
        var creationTime = item.CreationTime ?? _now;
        SetDefault(properties, PropertyIds.CreationTime, PstPropertyType.Time, creationTime);
        SetDefault(properties, PropertyIds.LastModificationTime, PstPropertyType.Time, item.LastModificationTime ?? creationTime);
        SetDefault(properties, PropertyIds.SearchKey, PstPropertyType.Binary, NextSearchKey());
        SetDefault(properties, PropertyIds.Importance, PstPropertyType.Integer32, (int)PstImportance.Normal);
        SetDefault(properties, PropertyIds.Sensitivity, PstPropertyType.Integer32, (int)PstSensitivity.Normal);
        if (_ansi)
        {
            SetDefault(properties, PropertyIds.MessageCodepage, PstPropertyType.Integer32, _string8Encoding.CodePage);
        }
        if (item.Recipients.Count > 0)
        {
            SetDefault(properties, PropertyIds.DisplayTo, StringType, string.Join("; ", to));
            SetDefault(properties, PropertyIds.DisplayCc, StringType, string.Join("; ", cc));
            SetDefault(properties, PropertyIds.DisplayBcc, StringType, string.Join("; ", bcc));
        }

        size += properties.Sum(property => (long)(property.Value?.Length ?? 0));
        Set(properties, PropertyIds.MessageSize, PstPropertyType.Integer32, (int)Math.Min(int.MaxValue, size));
        PropertyContextWriter.Write(node, properties, _ndb.Layout.MaxLeafDataSize);
        return (node, properties);
    }

    private (NodeContent Content, List<WriterProperty> Properties, long Size) SerializeAttachment(PstAttachmentBuilder attachment, int depth)
    {
        var node = new NodeContent();
        var properties = attachment.Properties.Encode(_names, _string8Encoding, _ansi);
        long size = properties.Sum(property => (long)(property.Value?.Length ?? 0));
        if (attachment.EmbeddedMessage is { } embedded)
        {
            var (content, embeddedProperties) = SerializeMessage(embedded, depth + 1);
            var nid = node.AllocateSubnodeNid(NidType.NormalMessage);
            node.Subnodes[nid] = content;
            var messageSize = embeddedProperties.Find(property => property.Tag.Id == PropertyIds.MessageSize).Value is { } bytes ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : 0;
            var reference = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(reference, nid);
            BinaryPrimitives.WriteInt32LittleEndian(reference.AsSpan(4), messageSize);
            properties.RemoveAll(property => property.Tag.Id == PropertyIds.AttachDataBinary);
            properties.Add(new WriterProperty(new PstPropertyTag(PropertyIds.AttachDataBinary, PstPropertyType.ObjectReference), reference));
            if (!attachment.IsCopy)
            {
                Set(properties, PropertyIds.AttachMethod, PstPropertyType.Integer32, (int)PstAttachmentMethod.EmbeddedMessage);
            }

            size += messageSize;
        }
        else
        {
            var content = properties.Find(property => property.Tag.Id == PropertyIds.AttachDataBinary);
            if (content.Stream is not null)
            {
                size += content.Stream.CanSeek ? content.Stream.Length - content.Stream.Position : 0;
            }

            if (!attachment.IsCopy)
            {
                SetDefault(properties, PropertyIds.AttachMethod, PstPropertyType.Integer32, content.Value is null && content.Stream is null ? (int)PstAttachmentMethod.None : (int)PstAttachmentMethod.ByValue);
            }
        }

        SetDefault(properties, PropertyIds.RenderingPosition, PstPropertyType.Integer32, -1);
        if (attachment.FileName is { } fileName)
        {
            SetDefault(properties, PropertyIds.AttachLongFilename, StringType, fileName);
            SetDefault(properties, PropertyIds.DisplayName, StringType, fileName);
            var extension = Path.GetExtension(fileName);
            if (!string.IsNullOrEmpty(extension))
            {
                SetDefault(properties, PropertyIds.AttachExtension, StringType, extension);
            }
        }

        Set(properties, PropertyIds.AttachSize, PstPropertyType.Integer32, (int)Math.Min(int.MaxValue, size));
        PropertyContextWriter.Write(node, properties, _ndb.Layout.MaxLeafDataSize);
        return (node, properties, size);
    }

    /// <summary>Returns a search key, unique in the file and derived from the identifier of the store, so that the same content gives the same file.</summary>
    private byte[] NextSearchKey()
    {
        var key = (byte[])_storeId.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(key.AsSpan(12), BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(12)) ^ ++_searchKeyCounter);
        return key;
    }

    private PstPropertyType StringType => _ansi ? PstPropertyType.String8 : PstPropertyType.Unicode;

    private List<PstPropertyTag> Columns((ushort Id, PstPropertyType Type)[] columns) => [.. columns.Select(column => new PstPropertyTag(column.Id, column.Type == PstPropertyType.Unicode ? StringType : column.Type))];

    private void Set(List<WriterProperty> properties, ushort id, PstPropertyType type, object value)
    {
        properties.RemoveAll(property => property.Tag.Id == id);
        if (type == PstPropertyType.Unicode)
        {
            type = StringType;
        }

        properties.Add(new WriterProperty(new PstPropertyTag(id, type), PropertyValueCodec.Encode(type, value, _string8Encoding)));
    }

    private void SetDefault(List<WriterProperty> properties, ushort id, PstPropertyType type, object value)
    {
        if (!properties.Exists(property => property.Tag.Id == id))
        {
            Set(properties, id, type, value);
        }
    }

    private void WriteFolder(PstWriterFolder folder)
    {
        var children = GetChildren(folder).ToList();
        foreach (var child in children)
        {
            WriteFolder(child);
        }

        var properties = GetFolderProperties(folder, children.Count > 0);
        var pc = new NodeContent();
        PropertyContextWriter.Write(pc, properties, _ndb.Layout.MaxLeafDataSize);
        AddNode(folder.NodeId, pc, folder.Parent?.NodeId ?? folder.NodeId);

        if (folder.IsSearchFolder)
        {
            _ndb.AddNode(Nids.WithType(folder.NodeId, NidType.SearchUpdateQueue), 0, 0, 0);
            AddTable(Nids.WithType(folder.NodeId, NidType.SearchContentsTable), SearchContentsColumns, []);
            return;
        }

        var hierarchyRows = children.Select(child => (child.NodeId, (IReadOnlyList<WriterProperty>)GetFolderProperties(child, GetChildren(child).Any()).Where(property => HierarchyColumns.Any(column => column.Id == property.Tag.Id)).ToList())).ToList();
        AddTable(Nids.WithType(folder.NodeId, NidType.HierarchyTable), HierarchyColumns, hierarchyRows);
        AddTable(Nids.WithType(folder.NodeId, NidType.ContentsTable), ContentsColumns, folder.Items);
        AddTable(Nids.WithType(folder.NodeId, NidType.AssociatedContentsTable), AssociatedContentsColumns, folder.AssociatedItems);
    }

    private List<WriterProperty> GetFolderProperties(PstWriterFolder folder, bool hasSubfolders)
    {
        var properties = folder.Properties.Encode(_names, _string8Encoding, _ansi);
        Set(properties, PropertyIds.ContentCount, PstPropertyType.Integer32, folder.Items.Count);
        Set(properties, PropertyIds.ContentUnreadCount, PstPropertyType.Integer32, folder.UnreadCount);
        Set(properties, PropertyIds.Subfolders, PstPropertyType.Boolean, hasSubfolders);
        SetDefault(properties, PropertyIds.DisplayName, PstPropertyType.Unicode, "");
        return properties;
    }

    private void AddTable(uint nid, (ushort Id, PstPropertyType Type)[] columns, IReadOnlyList<(uint RowId, IReadOnlyList<WriterProperty> Cells)> rows)
    {
        var content = new NodeContent();
        TableContextWriter.Write(content, Columns(columns), rows, _ndb.Layout);
        AddNode(nid, content, 0);
    }

    private void WriteStore()
    {
        byte[] EntryId(uint nid)
        {
            var entryId = new byte[24];
            _storeId.CopyTo(entryId, 4);
            BinaryPrimitives.WriteUInt32LittleEndian(entryId.AsSpan(20), nid);
            return entryId;
        }

        var properties = new List<WriterProperty>();
        Set(properties, PropertyIds.RecordKey, PstPropertyType.Binary, _storeId);
        Set(properties, PropertyIds.DisplayName, PstPropertyType.Unicode, _storeDisplayName);
        Set(properties, PropertyIds.IpmSubtreeEntryId, PstPropertyType.Binary, EntryId(IpmSubtreeNid));
        Set(properties, PropertyIds.IpmWastebasketEntryId, PstPropertyType.Binary, EntryId(DeletedItemsNid));
        Set(properties, PropertyIds.FinderEntryId, PstPropertyType.Binary, EntryId(SearchRootNid));
        Set(properties, PropertyIds.ValidFolderMask, PstPropertyType.Integer32, 0x89);
        Set(properties, PropertyIds.PstPassword, PstPropertyType.Integer32, 0);
        var content = new NodeContent();
        PropertyContextWriter.Write(content, properties, _ndb.Layout.MaxLeafDataSize);
        AddNode(Nids.MessageStore, content, 0);
    }

    private void WriteTemplates()
    {
        AddTable(Nids.HierarchyTableTemplate, HierarchyColumns, []);
        AddTable(Nids.ContentsTableTemplate, ContentsColumns, []);
        AddTable(Nids.AssociatedContentsTableTemplate, AssociatedContentsColumns, []);
        AddTable(Nids.SearchContentsTableTemplate, SearchContentsColumns, []);
        AddTable(Nids.AttachmentTable, AttachmentColumns, []);
        AddTable(Nids.RecipientTable, RecipientColumns, []);
    }

    private void AddNode(uint nid, NodeContent content, uint parent)
    {
        var (bidData, bidSub) = content.Write(_ndb);
        _ndb.AddNode(nid, bidData, bidSub, parent);
    }
}
