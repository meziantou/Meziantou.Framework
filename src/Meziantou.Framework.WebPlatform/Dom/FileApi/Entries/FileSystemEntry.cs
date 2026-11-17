using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom.FileApi;

/// <summary>https://wicg.github.io/entries-api/#callbackdef-errorcallback</summary>
/// <seealso href="https://wicg.github.io/entries-api/#callbackdef-errorcallback"/>
[IdlInterface("ErrorCallback", "https://wicg.github.io/entries-api/#callbackdef-errorcallback")]
public delegate void ErrorCallback(DomException err);

/// <summary>https://wicg.github.io/entries-api/#callbackdef-filesystementrycallback</summary>
/// <seealso href="https://wicg.github.io/entries-api/#callbackdef-filesystementrycallback"/>
[IdlInterface("FileSystemEntryCallback", "https://wicg.github.io/entries-api/#callbackdef-filesystementrycallback")]
public delegate void FileSystemEntryCallback(FileSystemEntry entry);

/// <summary>https://wicg.github.io/entries-api/#callbackdef-filesystementriescallback</summary>
/// <seealso href="https://wicg.github.io/entries-api/#callbackdef-filesystementriescallback"/>
[IdlInterface("FileSystemEntriesCallback", "https://wicg.github.io/entries-api/#callbackdef-filesystementriescallback")]
public delegate void FileSystemEntriesCallback(IReadOnlyList<FileSystemEntry> entries);

/// <summary>https://wicg.github.io/entries-api/#callbackdef-filecallback</summary>
/// <seealso href="https://wicg.github.io/entries-api/#callbackdef-filecallback"/>
[IdlInterface("FileCallback", "https://wicg.github.io/entries-api/#callbackdef-filecallback")]
public delegate void FileCallback(File file);

/// <summary>The options of <see cref="FileSystemDirectoryEntry.GetFile"/> and <see cref="FileSystemDirectoryEntry.GetDirectory"/> (https://wicg.github.io/entries-api/#dictdef-filesystemflags).</summary>
/// <seealso href="https://wicg.github.io/entries-api/#dictdef-filesystemflags"/>
[IdlInterface("FileSystemFlags", "https://wicg.github.io/entries-api/#dictdef-filesystemflags")]
public sealed class FileSystemFlags
{
    /// <summary>Gets whether the entry is created: the dropped files are read-only, so a creation fails with a <c>SecurityError</c>.</summary>
    /// <seealso href="https://wicg.github.io/entries-api/#dom-filesystemflags-create"/>
    [IdlMember("create", "https://wicg.github.io/entries-api/#dom-filesystemflags-create")]
    public bool Create { get; init; }

    /// <summary>Gets whether the creation fails when the entry exists (unused: the entries cannot be created).</summary>
    /// <seealso href="https://wicg.github.io/entries-api/#dom-filesystemflags-exclusive"/>
    [IdlMember("exclusive", "https://wicg.github.io/entries-api/#dom-filesystemflags-exclusive")]
    public bool Exclusive { get; init; }
}

/// <summary>
/// The file system of the files and the directories of a drag data store (https://wicg.github.io/entries-api/#api-domfilesystem):
/// its root directory contains the files of the store (the dropped files and directories, or the files that a script
/// added to a <see cref="DataTransfer"/>). It is read-only.
/// </summary>
/// <seealso href="https://wicg.github.io/entries-api/#api-domfilesystem"/>
[IdlInterface("FileSystem", "https://wicg.github.io/entries-api/#api-domfilesystem")]
public sealed class FileSystem
{
    internal FileSystem(GlobalScope global, Func<IReadOnlyList<DragDataEntry>> rootEntries, long dropTime)
    {
        Global = global;
        _rootEntries = rootEntries;
        DropTime = dropTime;
        Name = "dropped-files-" + Guid.NewGuid().ToString("N");
        Root = new FileSystemDirectoryEntry(this, [], directory: null);
    }

    private readonly Func<IReadOnlyList<DragDataEntry>> _rootEntries;

    internal GlobalScope Global { get; }

    /// <summary>The entries of the root directory: the files and the directories of the drag data store.</summary>
    internal IReadOnlyList<DragDataEntry> RootEntries => _rootEntries();

    internal long DropTime { get; }

    /// <summary>Gets the name of the file system (unique for each drop).</summary>
    /// <seealso href="https://wicg.github.io/entries-api/#dom-filesystem-name"/>
    [IdlMember("name", "https://wicg.github.io/entries-api/#dom-filesystem-name")]
    public string Name { get; }

    /// <summary>Gets the root directory: it contains the dropped files and directories.</summary>
    /// <seealso href="https://wicg.github.io/entries-api/#dom-filesystem-root"/>
    [IdlMember("root", "https://wicg.github.io/entries-api/#dom-filesystem-root")]
    public FileSystemDirectoryEntry Root { get; }

    /// <summary>A new entry for a dropped file or directory (https://wicg.github.io/entries-api/#dom-datatransferitem-webkitgetasentry).</summary>
    internal FileSystemEntry GetTopLevelEntry(DragDataEntry entry) => CreateEntry([entry.Name], entry);

    internal FileSystemEntry CreateEntry(string[] path, DragDataEntry? item) => item switch
    {
        DragDataFile file => new FileSystemFileEntry(this, path, file),
        _ => new FileSystemDirectoryEntry(this, path, (DragDataDirectory?)item),
    };

    /// <summary>
    /// Evaluates a path (https://wicg.github.io/entries-api/#evaluate-a-path): false when there is no entry at the path;
    /// <paramref name="item"/> is null for the root directory.
    /// </summary>
    internal bool TryEvaluate(IReadOnlyList<string> path, out DragDataEntry? item)
    {
        item = null;
        var entries = RootEntries;
        foreach (var name in path)
        {
            DragDataEntry? child = null;
            foreach (var entry in entries)
            {
                if (entry.Name == name)
                {
                    child = entry;
                    break;
                }
            }

            if (child is null)
                return false;

            item = child;
            if (child is DragDataDirectory directory)
            {
                entries = directory.Entries;
            }
            else
            {
                entries = [];
            }
        }

        return true;
    }

    /// <summary>Whether <paramref name="item"/> is still in the directory of its path (several entries of a directory can have the same name).</summary>
    internal bool Contains(IReadOnlyList<string> path, DragDataEntry item)
    {
        if (path.Count == 0)
            return false;

        var parentPath = path.Take(path.Count - 1).ToArray();
        if (!TryEvaluate(parentPath, out var parent))
            return false;

        IReadOnlyList<DragDataEntry> entries = parent switch
        {
            null => RootEntries,
            DragDataDirectory directory => directory.Entries,
            _ => [],
        };

        foreach (var entry in entries)
        {
            if (ReferenceEquals(entry, item))
                return true;
        }

        return false;
    }

    /// <summary>Invokes a callback from a task of the window (the callbacks of the API are asynchronous): its exceptions are reported.</summary>
    internal void QueueCallback(Action callback)
    {
        Global.QueueGlobalTask(() =>
        {
            try
            {
                callback();
            }
#pragma warning disable CA1031 // The exceptions of the callbacks are reported
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ScriptErrors.Report(ex, Global);
            }
        });
    }

    internal void QueueError(ErrorCallback? errorCallback, DomException error)
    {
        if (errorCallback is not null)
        {
            QueueCallback(() => errorCallback(error));
        }
    }
}

/// <summary>A file or a directory of a <see cref="FileSystem"/> (https://wicg.github.io/entries-api/#api-entry).</summary>
/// <seealso href="https://wicg.github.io/entries-api/#api-entry"/>
[IdlInterface("FileSystemEntry", "https://wicg.github.io/entries-api/#api-entry")]
public abstract class FileSystemEntry
{
    private protected FileSystemEntry(FileSystem filesystem, string[] path)
    {
        Filesystem = filesystem;
        Path = path;
    }

    /// <summary>The names of the path of the entry from the root directory.</summary>
    internal string[] Path { get; }

    /// <summary>Gets whether the entry is a file.</summary>
    /// <seealso href="https://wicg.github.io/entries-api/#dom-filesystementry-isfile"/>
    [IdlMember("isFile", "https://wicg.github.io/entries-api/#dom-filesystementry-isfile")]
    public abstract bool IsFile { get; }

    /// <summary>Gets whether the entry is a directory.</summary>
    /// <seealso href="https://wicg.github.io/entries-api/#dom-filesystementry-isdirectory"/>
    [IdlMember("isDirectory", "https://wicg.github.io/entries-api/#dom-filesystementry-isdirectory")]
    public bool IsDirectory => !IsFile;

    /// <summary>Gets the name of the entry (the empty string for the root directory).</summary>
    /// <seealso href="https://wicg.github.io/entries-api/#dom-filesystementry-name"/>
    [IdlMember("name", "https://wicg.github.io/entries-api/#dom-filesystementry-name")]
    public string Name => Path.Length == 0 ? "" : Path[^1];

    /// <summary>Gets the full path of the entry: <c>/</c> followed by the names of its path, e.g. <c>/photos/a.jpg</c>.</summary>
    /// <seealso href="https://wicg.github.io/entries-api/#dom-filesystementry-fullpath"/>
    [IdlMember("fullPath", "https://wicg.github.io/entries-api/#dom-filesystementry-fullpath")]
    public string FullPath => EntriesPath.ToFullPath(Path);

    /// <summary>Gets the file system of the entry.</summary>
    /// <seealso href="https://wicg.github.io/entries-api/#dom-filesystementry-filesystem"/>
    [IdlMember("filesystem", "https://wicg.github.io/entries-api/#dom-filesystementry-filesystem")]
    public FileSystem Filesystem { get; }

    /// <summary>Calls <paramref name="successCallback"/> (from a task) with the parent directory of the entry, the root directory for the root directory.</summary>
    /// <seealso href="https://wicg.github.io/entries-api/#dom-filesystementry-getparent"/>
    [IdlMember("getParent", "https://wicg.github.io/entries-api/#dom-filesystementry-getparent")]
    public void GetParent(FileSystemEntryCallback? successCallback = null, ErrorCallback? errorCallback = null)
    {
        string[] parentPath = Path.Length == 0 ? [] : Path[..^1];
        if (!Filesystem.TryEvaluate(parentPath, out var item))
        {
            Filesystem.QueueError(errorCallback, DomException.NotFound("The parent directory does not exist"));
            return;
        }

        if (successCallback is not null)
        {
            var parent = Filesystem.CreateEntry(parentPath, item);
            Filesystem.QueueCallback(() => successCallback(parent));
        }
    }
}

/// <summary>A directory of a <see cref="FileSystem"/> (https://wicg.github.io/entries-api/#api-directoryentry).</summary>
/// <seealso href="https://wicg.github.io/entries-api/#api-directoryentry"/>
[IdlInterface("FileSystemDirectoryEntry", "https://wicg.github.io/entries-api/#api-directoryentry")]
public sealed class FileSystemDirectoryEntry : FileSystemEntry
{
    internal FileSystemDirectoryEntry(FileSystem filesystem, string[] path, DragDataDirectory? directory)
        : base(filesystem, path)
    {
        Directory = directory;
    }

    /// <summary>The dropped directory, null for the root directory.</summary>
    internal DragDataDirectory? Directory { get; }

    /// <inheritdoc/>
    public override bool IsFile => false;

    /// <summary>Returns a reader of the entries of the directory.</summary>
    /// <seealso href="https://wicg.github.io/entries-api/#dom-filesystemdirectoryentry-createreader"/>
    [IdlMember("createReader", "https://wicg.github.io/entries-api/#dom-filesystemdirectoryentry-createreader")]
    public FileSystemDirectoryReader CreateReader() => new(this);

    /// <summary>
    /// Calls <paramref name="successCallback"/> (from a task) with the file at <paramref name="path"/>, relative to the
    /// directory or absolute (e.g. <c>photos/a.jpg</c>, <c>../b.txt</c>, <c>/c.txt</c>). <paramref name="errorCallback"/> gets a
    /// <c>TypeMismatchError</c> for an invalid path or a directory, a <c>NotFoundError</c> when there is no entry, and a
    /// <c>SecurityError</c> when <see cref="FileSystemFlags.Create"/> is set.
    /// </summary>
    /// <seealso href="https://wicg.github.io/entries-api/#dom-filesystemdirectoryentry-getfile"/>
    [IdlMember("getFile", "https://wicg.github.io/entries-api/#dom-filesystemdirectoryentry-getfile")]
    public void GetFile(string? path = null, FileSystemFlags? options = null, FileSystemEntryCallback? successCallback = null, ErrorCallback? errorCallback = null)
        => GetEntry(path, options, isFile: true, successCallback, errorCallback);

    /// <summary>Calls <paramref name="successCallback"/> (from a task) with the directory at <paramref name="path"/> (see <see cref="GetFile"/>).</summary>
    /// <seealso href="https://wicg.github.io/entries-api/#dom-filesystemdirectoryentry-getdirectory"/>
    [IdlMember("getDirectory", "https://wicg.github.io/entries-api/#dom-filesystemdirectoryentry-getdirectory")]
    public void GetDirectory(string? path = null, FileSystemFlags? options = null, FileSystemEntryCallback? successCallback = null, ErrorCallback? errorCallback = null)
        => GetEntry(path, options, isFile: false, successCallback, errorCallback);

    private void GetEntry(string? path, FileSystemFlags? options, bool isFile, FileSystemEntryCallback? successCallback, ErrorCallback? errorCallback)
    {
        path ??= "";
        if (!EntriesPath.IsValid(path))
        {
            Filesystem.QueueError(errorCallback, new DomException($"'{path}' is not a valid path", "TypeMismatchError"));
            return;
        }

        if (options?.Create == true)
        {
            Filesystem.QueueError(errorCallback, new DomException("The dropped files are read-only", "SecurityError"));
            return;
        }

        var resolved = EntriesPath.Resolve(FullPath, path).ToArray();
        if (!Filesystem.TryEvaluate(resolved, out var item))
        {
            Filesystem.QueueError(errorCallback, DomException.NotFound($"There is no entry at '{EntriesPath.ToFullPath(resolved)}'"));
            return;
        }

        if ((item is DragDataFile) != isFile)
        {
            Filesystem.QueueError(errorCallback, new DomException(isFile ? "The entry is a directory" : "The entry is a file", "TypeMismatchError"));
            return;
        }

        if (successCallback is not null)
        {
            var entry = Filesystem.CreateEntry(resolved, item);
            Filesystem.QueueCallback(() => successCallback(entry));
        }
    }
}

/// <summary>A file of a <see cref="FileSystem"/> (https://wicg.github.io/entries-api/#api-fileentry).</summary>
/// <seealso href="https://wicg.github.io/entries-api/#api-fileentry"/>
[IdlInterface("FileSystemFileEntry", "https://wicg.github.io/entries-api/#api-fileentry")]
public sealed class FileSystemFileEntry : FileSystemEntry
{
    private readonly DragDataFile _file;

    internal FileSystemFileEntry(FileSystem filesystem, string[] path, DragDataFile file)
        : base(filesystem, path)
    {
        _file = file;
    }

    /// <inheritdoc/>
    public override bool IsFile => true;

    /// <summary>
    /// Calls <paramref name="successCallback"/> (from a task) with a <see cref="FileApi.File"/> of the content of the file, or
    /// <paramref name="errorCallback"/> with a <c>NotFoundError</c> when the file is no longer in its file system (a file
    /// that a script removed from its <c>DataTransfer</c>).
    /// </summary>
    /// <seealso href="https://wicg.github.io/entries-api/#dom-filesystemfileentry-file"/>
    [IdlMember("file", "https://wicg.github.io/entries-api/#dom-filesystemfileentry-file")]
    public void File(FileCallback successCallback, ErrorCallback? errorCallback = null)
    {
        ArgumentNullException.ThrowIfNull(successCallback);

        // https://wicg.github.io/entries-api/#dom-filesystemfileentry-file: the file is looked up again in its file system
        // (the files of a DataTransfer that a script changes can be removed). Several files can have the same name, so
        // the lookup is for this file, not for the first file of its name.
        if (!Filesystem.Contains(Path, _file))
        {
            Filesystem.QueueError(errorCallback, DomException.NotFound("The file does not exist"));
            return;
        }

        var file = _file.CreateFile(Filesystem.DropTime);
        Filesystem.QueueCallback(() => successCallback(file));
    }
}

/// <summary>
/// Reads the entries of a directory (https://wicg.github.io/entries-api/#api-directoryreader): the first call of
/// <see cref="ReadEntries"/> gets all the entries, the next ones get an empty list (the end of the directory).
/// </summary>
/// <seealso href="https://wicg.github.io/entries-api/#api-directoryreader"/>
[IdlInterface("FileSystemDirectoryReader", "https://wicg.github.io/entries-api/#api-directoryreader")]
public sealed class FileSystemDirectoryReader
{
    private readonly FileSystemDirectoryEntry _directory;
    private bool _reading;
    private bool _done;

    internal FileSystemDirectoryReader(FileSystemDirectoryEntry directory)
    {
        _directory = directory;
    }

    /// <summary>
    /// Calls <paramref name="successCallback"/> (from a task) with the next entries of the directory, or an empty list at
    /// the end. <paramref name="errorCallback"/> gets an <c>InvalidStateError</c> when a read is in progress.
    /// </summary>
    /// <seealso href="https://wicg.github.io/entries-api/#dom-filesystemdirectoryreader-readentries"/>
    [IdlMember("readEntries", "https://wicg.github.io/entries-api/#dom-filesystemdirectoryreader-readentries")]
    public void ReadEntries(FileSystemEntriesCallback successCallback, ErrorCallback? errorCallback = null)
    {
        ArgumentNullException.ThrowIfNull(successCallback);
        var filesystem = _directory.Filesystem;
        if (_reading)
        {
            filesystem.QueueError(errorCallback, DomException.InvalidState("The entries are being read"));
            return;
        }

        if (_done)
        {
            filesystem.QueueCallback(() => successCallback([]));
            return;
        }

        _reading = true;
        var entries = new List<FileSystemEntry>();
        foreach (var child in _directory.Directory?.Entries ?? filesystem.RootEntries)
        {
            entries.Add(filesystem.CreateEntry([.. _directory.Path, child.Name], child));
        }

        filesystem.QueueCallback(() =>
        {
            _reading = false;
            _done = true;
            successCallback(entries);
        });
    }
}
