using Meziantou.Framework.Mail.Internals;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail;

/// <summary>An Outlook data file (PST or OST) opened for reading. The objects it returns read the file when they are accessed, so the file must stay open while they are used.</summary>
/// <remarks>The instances are not thread-safe: use one instance per thread.</remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Mail.Tests/PstFileTests.cs" ref:region="PstFile_Open" ref:dedent="true">
/// using var file = PstFile.Open(path);
/// Assert.Equal(PstFormat.Unicode, file.Format);
/// Assert.Equal("Outlook Data File", file.Store.DisplayName);
///
/// var folder = file.GetFolder("Inbox/tmp");
/// Assert.NotNull(folder);
/// Assert.Equal(4, folder.ContentCount);
/// </code>
/// </example>
public sealed class PstFile : IDisposable
{
    private readonly NdbReader _reader;
    private PstStore? _store;

    private PstFile(NdbReader reader)
    {
        _reader = reader;
        Context = new PstContext(reader);
    }

    internal PstContext Context { get; }

    internal NdbReader Reader => _reader;

    /// <summary>Gets the format of the file.</summary>
    public PstFormat Format => _reader.Layout.Format;

    /// <summary>Gets the method that obfuscates the data blocks of the file.</summary>
    public PstEncryption Encryption => _reader.Encryption;

    /// <summary>Gets whether the file is an offline file (OST) of an Exchange or IMAP account, rather than a personal file (PST).</summary>
    public bool IsOfflineFile => _reader.Header.MagicClient == NdbHeader.MagicClientOst;

    /// <summary>Gets the message store: the display name of the file and its root folder.</summary>
    /// <exception cref="InvalidPstException">The file is malformed.</exception>
    public PstStore Store => _store ??= new PstStore(this);

    /// <summary>Gets the root folder of the file, which contains the top of the folder hierarchy and the search folders.</summary>
    /// <exception cref="InvalidPstException">The file is malformed.</exception>
    public PstFolder RootFolder => Store.RootFolder;

    /// <summary>Opens a file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options of the reader, or <see langword="null"/> for the default options.</param>
    /// <returns>The opened file.</returns>
    /// <exception cref="InvalidPstException">The file is not an Outlook data file or is malformed.</exception>
    /// <exception cref="UnsupportedPstFeatureException">The file uses a format version or an encryption method the library does not support.</exception>
    public static PstFile Open(string path, PstReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.RandomAccess);
        return Open(stream, options, leaveOpen: false);
    }

    /// <summary>Opens a file from a stream.</summary>
    /// <param name="stream">A readable and seekable stream.</param>
    /// <param name="options">The options of the reader, or <see langword="null"/> for the default options.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave the stream open when the file is disposed.</param>
    /// <returns>The opened file.</returns>
    /// <exception cref="ArgumentException">The stream is not readable or not seekable.</exception>
    /// <exception cref="InvalidPstException">The stream does not contain an Outlook data file or the file is malformed.</exception>
    /// <exception cref="UnsupportedPstFeatureException">The file uses a format version or an encryption method the library does not support.</exception>
    public static PstFile Open(Stream stream, PstReadOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new PstFile(NdbReader.Open(stream, leaveOpen, options ?? PstReadOptions.Default));
    }

    /// <summary>Opens a file from its content.</summary>
    /// <param name="data">The content of the file.</param>
    /// <param name="options">The options of the reader, or <see langword="null"/> for the default options.</param>
    /// <returns>The opened file.</returns>
    /// <exception cref="InvalidPstException">The data is not an Outlook data file or the file is malformed.</exception>
    /// <exception cref="UnsupportedPstFeatureException">The file uses a format version or an encryption method the library does not support.</exception>
    public static PstFile Open(byte[] data, PstReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Open(new MemoryStream(data, writable: false), options, leaveOpen: false);
    }

    /// <summary>Returns a folder from its path, the display names of the folders from the top of the folder hierarchy (or from the root folder when the first name is not found there), separated by <c>/</c>.</summary>
    /// <param name="path">The path of the folder, such as <c>Inbox/Projects</c>.</param>
    /// <returns>The folder, or <see langword="null"/> when no folder has this path.</returns>
    public PstFolder? GetFolder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var names = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Store.IpmSubtree?.GetFolder(names) ?? RootFolder.GetFolder(names);
    }

    /// <summary>Checks the structures of the whole file: the header, the B-trees, every block and every node, without stopping at the first error.</summary>
    /// <returns>The problems found; an empty list when the file is valid.</returns>
    public IReadOnlyList<PstValidationIssue> Validate() => PstValidator.Validate(this);

    /// <summary>Closes the file and the stream it reads, unless it was opened with <c>leaveOpen</c>.</summary>
    public void Dispose() => _reader.Dispose();
}
