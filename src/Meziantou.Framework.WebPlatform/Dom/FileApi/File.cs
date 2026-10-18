namespace Meziantou.Framework.WebPlatform.Dom.FileApi;

/// <summary>https://w3c.github.io/FileAPI/#file-section</summary>
[IdlInterface("File", "https://w3c.github.io/FileAPI/#file-section")]
public sealed class File : Blob
{
    /// <summary>Creates a file from the given parts, name and options.</summary>
    [IdlMember("constructor", "https://w3c.github.io/FileAPI/#file-constructor")]
    public File(IReadOnlyList<object> fileBits, string fileName, FilePropertyBag? options = null)
        : base(ProcessParts(fileBits, options?.Endings), NormalizeType(options?.Type ?? ""))
    {
        ArgumentNullException.ThrowIfNull(fileName);
        Name = fileName;
        LastModified = options?.LastModified ?? GetCurrentTime();
    }

    internal File(Blob blob, string name)
        : base(ProcessParts([blob], endings: null), blob.Type)
    {
        Name = name;
        LastModified = GetCurrentTime();
    }

    /// <summary>A file with the same bytes, type and last modification date, and another name (https://xhr.spec.whatwg.org/#create-an-entry).</summary>
    internal File WithName(string name) => new(Content, Type, name, LastModified);

    /// <summary>Creates a file with the given bytes (not copied), type, name and last modification date (e.g. a file deserialized from a storage).</summary>
    internal static File Create(byte[] bytes, string type, string name, long lastModified) => new(bytes, type, name, lastModified);

    private File(byte[] bytes, string type, string name, long lastModified)
        : base(bytes, type)
    {
        Name = name;
        LastModified = lastModified;
    }

    /// <summary>The serialization steps of File (https://w3c.github.io/FileAPI/#file-section): the bytes, the type, the name and the last modification date.</summary>
    /// <summary>The current time, in milliseconds since the Unix epoch, measured with the clock of the current global object (<see cref="BrowsingContextOptions.TimeProvider"/>).</summary>
    private static long GetCurrentTime() => (GlobalScope.Current?.EventLoop.TimeProvider ?? TimeProvider.System).GetUtcNow().ToUnixTimeMilliseconds();

    private protected override Blob CloneForStructuredSerialization() => new File(Content, Type, Name, LastModified);

    /// <summary>
    /// For the snapshot of a file of the origin private file system (https://fs.spec.whatwg.org/#dom-filesystemfilehandle-getfile):
    /// whether its entry still exists. The browsers cannot read the snapshot of a removed file.
    /// </summary>
    internal Func<bool>? IsSnapshotReadable { get; set; }

    /// <summary>Gets the name of the file.</summary>
    [IdlMember("name", "https://w3c.github.io/FileAPI/#dfn-name")]
    public string Name { get; }

    /// <summary>Gets the last modification date of the file, in milliseconds since the Unix epoch.</summary>
    [IdlMember("lastModified", "https://w3c.github.io/FileAPI/#dfn-lastModified")]
    public long LastModified { get; }
}
