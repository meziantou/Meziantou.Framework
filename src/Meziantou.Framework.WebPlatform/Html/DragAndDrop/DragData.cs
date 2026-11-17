using Meziantou.Framework.WebPlatform.Dom.FileApi;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The data of a drag from outside the user agent (e.g. files dragged from the file manager of the operating system): the
/// files and the directories, and the strings (e.g. the text dragged from another application). Start the drag with
/// <see cref="PointerInput.BeginDrag"/> or drop it at once with <see cref="UserInput.Drop"/>.
/// </summary>
public sealed class DragData
{
    private readonly List<DragDataEntry> _entries = [];
    private readonly List<KeyValuePair<string, string>> _strings = [];
    private string _effectAllowed = "all";

    /// <summary>Gets the files and the directories that are dragged, in the order they were added.</summary>
    public IReadOnlyList<DragDataEntry> Entries => _entries;

    /// <summary>Gets the strings that are dragged (their type, e.g. <c>text/plain</c>, and their data), in the order they were added.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Strings => _strings;

    /// <summary>
    /// Gets or sets the operations that the source of the drag allows: the <c>effectAllowed</c> of the
    /// <c>DataTransfer</c> objects (<c>none</c>, <c>copy</c>, <c>copyLink</c>, <c>copyMove</c>, <c>link</c>,
    /// <c>linkMove</c>, <c>move</c> or <c>all</c>, the default).
    /// </summary>
    public string EffectAllowed
    {
        get => _effectAllowed;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value is not ("none" or "copy" or "copyLink" or "copyMove" or "link" or "linkMove" or "move" or "all"))
                throw new ArgumentException($"'{value}' is not a valid effectAllowed value", nameof(value));

            _effectAllowed = value;
        }
    }

    /// <summary>Adds a file.</summary>
    /// <param name="name">The name of the file (it cannot contain <c>/</c> or <c>\</c>, and cannot be <c>.</c> or <c>..</c>).</param>
    /// <param name="content">The bytes of the file (not copied).</param>
    /// <param name="type">The media type of the file, or null for the type of its extension (an empty type for an unknown extension).</param>
    /// <param name="lastModified">The last modification date of the file, or null for the current time of the browsing context.</param>
    public DragDataFile AddFile(string name, ReadOnlyMemory<byte> content, string? type = null, DateTimeOffset? lastModified = null)
        => DragDataDirectory.Add(_entries, new DragDataFile(name, content, type, lastModified));

    /// <summary>Adds an empty directory: add its files with <see cref="DragDataDirectory.AddFile"/> and <see cref="DragDataDirectory.AddDirectory"/>.</summary>
    /// <param name="name">The name of the directory (see <see cref="AddFile"/>).</param>
    public DragDataDirectory AddDirectory(string name) => DragDataDirectory.Add(_entries, new DragDataDirectory(name));

    /// <summary>
    /// Adds a file or a directory of the disk, with its files and its subdirectories. The content is read when this method
    /// is called (a snapshot). The symbolic links to directories are not followed.
    /// </summary>
    /// <param name="path">The path of the file or of the directory.</param>
    /// <exception cref="FileNotFoundException">There is no file or directory at <paramref name="path"/>.</exception>
    public DragDataEntry AddFromDisk(string path) => DragDataDirectory.Add(_entries, DragDataEntry.ReadFromDisk(path));

    /// <summary>Sets the string of a type (e.g. <c>text/plain</c>, <c>text/uri-list</c> or <c>text/html</c>): it replaces the string of the same type.</summary>
    /// <param name="type">The type of the string (it is converted to ASCII lowercase).</param>
    /// <param name="data">The string.</param>
    public void SetString(string type, string data)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(data);
        type = Infra.InfraAscii.ToAsciiLowercase(type);
        _strings.RemoveAll(s => s.Key == type);
        _strings.Add(new KeyValuePair<string, string>(type, data));
    }
}

/// <summary>A file or a directory of a <see cref="DragData"/>.</summary>
public abstract class DragDataEntry
{
    /// <param name="name">The name of the entry.</param>
    /// <param name="validate">Whether the name is validated: the name of a File object that a script created is not.</param>
    private protected DragDataEntry(string name, bool validate = true)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (validate && (!FileSystemHandle.IsValidFileName(name) || name.Contains('\0', StringComparison.Ordinal)))
            throw new ArgumentException($"'{name}' is not a valid file name", nameof(name));

        Name = name;
    }

    /// <summary>Gets the name of the file or of the directory.</summary>
    public string Name { get; }

    internal static DragDataEntry ReadFromDisk(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            var directory = new DragDataDirectory(GetName(fullPath));
            ReadDirectory(directory, fullPath);
            return directory;
        }

        if (System.IO.File.Exists(fullPath))
            return ReadFile(fullPath);

        throw new FileNotFoundException($"There is no file or directory at '{fullPath}'", fullPath);
    }

    private static string GetName(string fullPath) => Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));

    private static DragDataFile ReadFile(string fullPath)
        => new(GetName(fullPath), System.IO.File.ReadAllBytes(fullPath), FileMediaTypes.GetMediaType(fullPath), new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(fullPath)));

    private static void ReadDirectory(DragDataDirectory directory, string fullPath)
    {
        foreach (var info in new DirectoryInfo(fullPath).EnumerateFileSystemInfos().OrderBy(i => i.Name, StringComparer.Ordinal))
        {
            if (info is DirectoryInfo subdirectory)
            {
                if (subdirectory.LinkTarget is not null)
                    continue;

                ReadDirectory(directory.AddDirectory(subdirectory.Name), subdirectory.FullName);
            }
            else
            {
                DragDataDirectory.Add(directory.EntriesList, ReadFile(info.FullName));
            }
        }
    }
}

/// <summary>A file of a <see cref="DragData"/>.</summary>
public sealed class DragDataFile : DragDataEntry
{
    internal DragDataFile(string name, ReadOnlyMemory<byte> content, string? type, DateTimeOffset? lastModified)
        : base(name)
    {
        Content = content;
        Type = type ?? FileMediaTypes.GetMediaType(name);
        LastModified = lastModified;
        LastModifiedMilliseconds = lastModified?.ToUnixTimeMilliseconds();
    }

    /// <summary>A file of a File object of a drag data store (e.g. a file that a script added to a <c>DataTransfer</c>).</summary>
    internal DragDataFile(FileApi.File file)
        : base(file.Name, validate: false)
    {
        Content = file.Data;
        Type = file.Type;
        // The lastModified of a File can be out of the range of DateTimeOffset (e.g. new File([], 'a', { lastModified: 1e15 }))
        LastModifiedMilliseconds = file.LastModified;
        if (file.LastModified >= DateTimeOffset.MinValue.ToUnixTimeMilliseconds() && file.LastModified <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            LastModified = DateTimeOffset.FromUnixTimeMilliseconds(file.LastModified);
        }
        SourceFile = file;
    }

    /// <summary>The File object of the file, when the file is a File object of a drag data store.</summary>
    internal FileApi.File? SourceFile { get; }

    /// <summary>Gets the bytes of the file.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>Gets the media type of the file.</summary>
    public string Type { get; }

    /// <summary>Gets the last modification date of the file, or null for the time of the drop.</summary>
    public DateTimeOffset? LastModified { get; }

    /// <summary>The last modification date of the file in milliseconds since the Unix epoch (the value of a File object, which may be out of the range of <see cref="DateTimeOffset"/>), or null.</summary>
    internal long? LastModifiedMilliseconds { get; }

    /// <summary>Creates the File object of the file, for a drop at <paramref name="now"/> (milliseconds since the Unix epoch).</summary>
    internal FileApi.File CreateFile(long now, string? relativePath = null)
    {
        if (SourceFile is not null && relativePath is null)
            return SourceFile;

        var file = FileApi.File.Create(Content.ToArray(), Infra.InfraAscii.ToAsciiLowercase(Type), Name, LastModifiedMilliseconds ?? now);
        file.WebkitRelativePathValue = relativePath;
        return file;
    }
}

/// <summary>A directory of a <see cref="DragData"/>, with its files and its subdirectories.</summary>
public sealed class DragDataDirectory : DragDataEntry
{
    internal DragDataDirectory(string name)
        : base(name)
    {
    }

    internal List<DragDataEntry> EntriesList { get; } = [];

    /// <summary>Gets the files and the subdirectories of the directory.</summary>
    public IReadOnlyList<DragDataEntry> Entries => EntriesList;

    /// <summary>Adds a file to the directory (see <see cref="DragData.AddFile"/>).</summary>
    public DragDataFile AddFile(string name, ReadOnlyMemory<byte> content, string? type = null, DateTimeOffset? lastModified = null)
        => Add(EntriesList, new DragDataFile(name, content, type, lastModified));

    /// <summary>Adds an empty subdirectory to the directory.</summary>
    public DragDataDirectory AddDirectory(string name) => Add(EntriesList, new DragDataDirectory(name));

    /// <summary>Adds a file or a directory of the disk to the directory (see <see cref="DragData.AddFromDisk"/>).</summary>
    public DragDataEntry AddFromDisk(string path) => Add(EntriesList, ReadFromDisk(path));

    internal static T Add<T>(List<DragDataEntry> entries, T entry)
        where T : DragDataEntry
    {
        if (entries.Exists(e => e.Name == entry.Name))
            throw new ArgumentException($"There is already an entry named '{entry.Name}'", nameof(entry));

        entries.Add(entry);
        return entry;
    }
}
