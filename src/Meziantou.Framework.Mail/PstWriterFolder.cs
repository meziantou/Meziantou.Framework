using Meziantou.Framework.Mail.Internals;
using Meziantou.Framework.Mail.Internals.Ltp;

namespace Meziantou.Framework.Mail;

/// <summary>A folder of a file a <see cref="PstWriter"/> writes. The items added to it are written immediately; the folder itself is written by <see cref="PstWriter.Complete"/>.</summary>
public sealed class PstWriterFolder
{
    private readonly PstWriter _writer;
    private readonly List<PstWriterFolder> _folders = [];

    internal PstWriterFolder(PstWriter writer, uint nid, PstWriterFolder? parent, string displayName, string? containerClass, bool isSearchFolder = false)
    {
        _writer = writer;
        NodeId = nid;
        Parent = parent;
        IsSearchFolder = isSearchFolder;
        DisplayName = displayName;
        ContainerClass = containerClass;
    }

    internal uint NodeId { get; }

    internal bool IsSearchFolder { get; }

    internal List<(uint Nid, IReadOnlyList<WriterProperty> Cells)> Items { get; } = [];

    internal List<(uint Nid, IReadOnlyList<WriterProperty> Cells)> AssociatedItems { get; } = [];

    internal int UnreadCount { get; set; }

    /// <summary>Gets the parent folder, or <see langword="null"/> for the root folder of the file.</summary>
    public PstWriterFolder? Parent { get; }

    /// <summary>Gets the properties of the folder. The display name, the container class and the counts are set by the writer.</summary>
    public PstPropertyBag Properties { get; } = new();

    /// <summary>Gets or sets the display name of the folder.</summary>
    public string DisplayName
    {
        get => Properties.GetValue<string>(PropertyIds.DisplayName) ?? "";
        set => Properties.SetValue(PropertyIds.DisplayName, PstPropertyType.Unicode, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Gets or sets the class of the items of the folder, such as <c>IPF.Note</c>, <c>IPF.Contact</c>, <c>IPF.Appointment</c> or <c>IPF.Task</c>.</summary>
    public string? ContainerClass
    {
        get => Properties.GetValue<string>(PropertyIds.ContainerClass);
        set => Properties.SetValue(PropertyIds.ContainerClass, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets the subfolders.</summary>
    public IReadOnlyList<PstWriterFolder> Folders => _folders;

    /// <summary>Gets the number of items added to the folder.</summary>
    public int ItemCount => Items.Count;

    /// <summary>Creates a subfolder.</summary>
    /// <param name="displayName">The display name of the folder.</param>
    /// <param name="containerClass">The class of the items of the folder, such as <c>IPF.Note</c>, or <see langword="null"/>.</param>
    /// <returns>The new folder.</returns>
    /// <exception cref="InvalidOperationException">The writer is completed, or the folder is a search folder.</exception>
    public PstWriterFolder CreateFolder(string displayName, string? containerClass = null)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        _writer.EnsureWritable();
        if (IsSearchFolder)
        {
            throw new InvalidOperationException("A search folder cannot have subfolders.");
        }

        var folder = new PstWriterFolder(_writer, _writer.AllocateFolderNid(), this, displayName, containerClass);
        _folders.Add(folder);
        return folder;
    }

    /// <summary>Writes an item to the folder. The builder can be reused or modified after the call: the item is already written. A content set from a stream that cannot seek is read once: set it again before writing the builder again.</summary>
    /// <param name="item">The item.</param>
    /// <exception cref="InvalidOperationException">The writer is completed, or the folder is a search folder.</exception>
    /// <exception cref="ArgumentException">A property has a value that its type cannot store.</exception>
    public void AddItem(PstMessageBuilder item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _writer.EnsureWritable();
        if (IsSearchFolder)
        {
            throw new InvalidOperationException("A search folder cannot contain items.");
        }

        _writer.WriteItem(this, item);
    }

    /// <summary>Returns the display name of the folder.</summary>
    /// <returns>The display name of the folder.</returns>
    public override string ToString() => DisplayName;
}
