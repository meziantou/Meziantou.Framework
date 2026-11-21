namespace Meziantou.Framework.Email.Storage;

/// <summary>
/// Converts a mailbox from one format to another, with its tree of folders: an Outlook data file, a folder of mbox files
/// (Thunderbird) or a Maildir folder. The messages of mbox and Maildir folders are copied as they are between these formats
/// (an mbox file has LF line breaks), and converted through <see cref="EmailMessage"/> to and from Outlook data files.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../../tests/Meziantou.Framework.Email.Tests/MailboxConverterTests.cs" ref:region="MailboxConverter_Usage" ref:dedent="true">
/// // An Outlook data file to the folders of Thunderbird, then to a Maildir folder
/// var result = MailboxConverter.Convert(pstPath, mboxPath, MailboxFormat.Mbox);
/// Assert.Empty(result.Errors);
/// Assert.True(File.Exists(Path.Combine(mboxPath, "Inbox")));
///
/// var copy = MailboxConverter.Convert(mboxPath, maildirPath, MailboxFormat.Maildir);
/// Assert.Equal(result.MessageCount, copy.MessageCount);
/// Assert.Equal(MailboxFormat.Maildir, MailboxConverter.DetectFormat(maildirPath));
/// </code>
/// </example>
public static class MailboxConverter
{
    private const string InboxName = "Inbox";
    private const string SubfolderSuffix = ".sbd";

    /// <summary>Returns the format of a mailbox: a directory with <c>cur</c>, <c>new</c> and <c>tmp</c> is a Maildir folder, another directory a folder of mbox files, and a file is detected from its content.</summary>
    /// <param name="path">The path of the file or of the directory.</param>
    /// <returns>The format, or <see langword="null"/> when the path is not a mailbox the converter reads.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static MailboxFormat? DetectFormat(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Directory.Exists(path))
        {
            return EmailFormatDetector.Detect(path) == EmailFileFormat.Maildir ? MailboxFormat.Maildir : MailboxFormat.Mbox;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        return EmailFormatDetector.Detect(path) switch
        {
            EmailFileFormat.Pst or EmailFileFormat.Ost => MailboxFormat.Pst,
            EmailFileFormat.Mbox => MailboxFormat.Mbox,
            _ => new FileInfo(path).Length == 0 ? MailboxFormat.Mbox : null,
        };
    }

    /// <summary>Converts a mailbox, whose format is detected (see <see cref="DetectFormat"/>), to another format.</summary>
    /// <param name="sourcePath">The path of the mailbox to read.</param>
    /// <param name="destinationPath">The path of the mailbox to write: the file of an Outlook data file, or the directory of an mbox or a Maildir mailbox. It must not exist, or be an empty directory.</param>
    /// <param name="destinationFormat">The format of the mailbox to write.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default options.</param>
    /// <returns>The number of folders and messages written, and the messages that could not be converted.</returns>
    /// <exception cref="InvalidOperationException">The source is not a mailbox the converter reads.</exception>
    /// <exception cref="IOException">The destination exists, or a file cannot be read or written.</exception>
    /// <exception cref="EmailException">The source is malformed, beyond the messages the converter skips.</exception>
    public static MailboxConversionResult Convert(string sourcePath, string destinationPath, MailboxFormat destinationFormat, MailboxConversionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(destinationPath);
        options ??= new MailboxConversionOptions();
        var sourceFormat = DetectFormat(sourcePath) ?? throw new InvalidOperationException($"'{sourcePath}' is not an Outlook data file, an mbox file, a folder of mbox files or a Maildir folder.");
        if (File.Exists(destinationPath) || (Directory.Exists(destinationPath) && Directory.EnumerateFileSystemEntries(destinationPath).Any()))
        {
            throw new IOException($"The destination '{destinationPath}' exists.");
        }

        var result = new MailboxConversionResult();
        using var destination = CreateDestination(destinationPath, destinationFormat, options);
        foreach (var folder in ReadSource(sourcePath, sourceFormat, options))
        {
            destination.EnsureFolder(folder.Path);
            result.FolderCount++;
            foreach (var message in folder.Messages())
            {
                if (message.Item is { } item && destinationFormat != MailboxFormat.Pst && !options.IncludeNonMailItems && !IsMail(item))
                {
                    result.SkippedItemCount++;
                    continue;
                }

                try
                {
                    destination.Add(folder.Path, message);
                    result.MessageCount++;
                }
                catch (Exception ex) when (ex is EmailException or InvalidOperationException)
                {
                    result.AddError(folder.Path, ex.Message);
                }
            }
        }

        destination.Complete();
        return result;
    }

    private static bool IsMail(MapiMessage item) => item.GetType() == typeof(MapiMessage) || item is MapiMeetingMessage;

    private static IEnumerable<SourceFolder> ReadSource(string path, MailboxFormat format, MailboxConversionOptions options) => format switch
    {
        MailboxFormat.Pst => ReadPst(path, options),
        MailboxFormat.Maildir => ReadMaildir(MaildirFolder.Open(path), options),
        _ => File.Exists(path) ? [ReadMboxFile(path, [Path.GetFileNameWithoutExtension(path)], options)] : ReadMboxDirectory(path, [], options),
    };

    private static IEnumerable<SourceFolder> ReadPst(string path, MailboxConversionOptions options)
    {
        using var file = PstFile.Open(path, options.PstReadOptions);
        var root = file.Store.IpmSubtree ?? file.RootFolder;
        if (root.ContentCount > 0)
        {
            yield return new SourceFolder([InboxName], () => ReadPstItems(root));
        }

        foreach (var folder in EnumeratePstFolders(root, []))
        {
            yield return folder;
        }
    }

    private static IEnumerable<SourceFolder> EnumeratePstFolders(PstFolder folder, string[] path)
    {
        foreach (var child in folder.Folders)
        {
            if (child.IsSearchFolder)
            {
                continue;
            }

            string[] childPath = [.. path, child.DisplayName ?? "Folder"];
            yield return new SourceFolder(childPath, () => ReadPstItems(child));
            foreach (var descendant in EnumeratePstFolders(child, childPath))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<SourceMessage> ReadPstItems(PstFolder folder)
    {
        foreach (var item in folder.EnumerateItems())
        {
            yield return new SourceMessage(item, Content: null, item.IsRead, Sender: null, Date: null, MimeReadOptions.Default);
        }
    }

    private static IEnumerable<SourceFolder> ReadMaildir(MaildirFolder root, MailboxConversionOptions options)
    {
        yield return new SourceFolder([InboxName], () => ReadMaildirMessages(root, options));
        foreach (var (name, folder) in root.EnumerateFolders())
        {
            // Maildir++: the levels of the name are separated by dots
            yield return new SourceFolder(name.Split('.', StringSplitOptions.RemoveEmptyEntries), () => ReadMaildirMessages(folder, options));
        }
    }

    private static IEnumerable<SourceMessage> ReadMaildirMessages(MaildirFolder folder, MailboxConversionOptions options)
    {
        foreach (var message in folder.EnumerateMessages())
        {
            yield return new SourceMessage(Item: null, () => File.ReadAllBytes(message.FullPath), message.Flags.HasFlag(MaildirFlags.Seen), Sender: null, Date: null, options.MimeReadOptions ?? MimeReadOptions.Default);
        }
    }

    private static IEnumerable<SourceFolder> ReadMboxDirectory(string directory, string[] path, MailboxConversionOptions options)
    {
        foreach (var file in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
        {
            // The summaries (.msf) and the other files of a profile, such as the filters, are not folders
            var name = Path.GetFileName(file);
            if (name is ['.', ..] || !IsMboxFile(file))
            {
                continue;
            }

            string[] folderPath = [.. path, name];
            yield return ReadMboxFile(file, folderPath, options);
            var subfolders = file + SubfolderSuffix;
            if (Directory.Exists(subfolders))
            {
                foreach (var subfolder in ReadMboxDirectory(subfolders, folderPath, options))
                {
                    yield return subfolder;
                }
            }
        }
    }

    private static bool IsMboxFile(string path) => new FileInfo(path).Length == 0 || EmailFormatDetector.Detect(path) == EmailFileFormat.Mbox;

    private static SourceFolder ReadMboxFile(string path, string[] folderPath, MailboxConversionOptions options)
    {
        var readOptions = new MboxReadOptions { Format = options.MboxFormat, MessageOptions = options.MimeReadOptions ?? new MimeReadOptions() };
        return new SourceFolder(folderPath, () => ReadMboxMessages(path, readOptions));
    }

    private static IEnumerable<SourceMessage> ReadMboxMessages(string path, MboxReadOptions options)
    {
        foreach (var message in MboxReader.ReadMessages(path, options))
        {
            yield return new SourceMessage(Item: null, () => message.Content.ToArray(), IsRead: null, message.Sender, message.Date, options.MessageOptions);
        }
    }

    private static Destination CreateDestination(string path, MailboxFormat format, MailboxConversionOptions options) => format switch
    {
        MailboxFormat.Pst => new PstDestination(path, options.PstWriterOptions),
        MailboxFormat.Maildir => new MaildirDestination(MaildirFolder.Create(path)),
        MailboxFormat.Mbox => new MboxDestination(path, options.MboxFormat),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The format is not valid."),
    };

    /// <summary>Returns a name that is valid as a file name and as a level of a Maildir++ name.</summary>
    private static string GetFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) || c is '.' or '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(c) ? '_' : c).ToArray();
        var result = new string(chars);
        return result.Length == 0 ? "_" : result;
    }

    /// <summary>A message of a source: an item of an Outlook data file, or the bytes of a MIME message, with its read state when the source knows it.</summary>
    private sealed record SourceMessage(MapiMessage? Item, Func<byte[]>? Content, bool? IsRead, string? Sender, DateTimeOffset? Date, MimeReadOptions MimeOptions)
    {
        public MimeMessage GetMime() => Item is not null ? EmailMessage.FromMapi(Item).ToMime() : MimeMessage.Load(Content!(), MimeOptions);
    }

    private sealed record SourceFolder(string[] Path, Func<IEnumerable<SourceMessage>> Messages);

    private abstract class Destination : IDisposable
    {
        public abstract void EnsureFolder(string[] path);

        public abstract void Add(string[] path, SourceMessage message);

        public virtual void Complete()
        {
        }

        public abstract void Dispose();
    }

    private sealed class PstDestination(string path, PstWriterOptions? options) : Destination
    {
        private readonly PstWriter _writer = PstWriter.Create(path, options);
        private readonly Dictionary<string, PstWriterFolder> _folders = new(StringComparer.Ordinal);

        public override void EnsureFolder(string[] path) => GetFolder(path);

        public override void Add(string[] path, SourceMessage message)
        {
            MapiMessageBuilder builder;
            if (message.Item is { } item)
            {
                builder = MapiMessageBuilder.FromMessage(item);
            }
            else
            {
                builder = EmailMessage.FromMime(message.GetMime()).ToMapiBuilder();
                builder.Flags = message.IsRead is false ? MapiMessageFlags.None : MapiMessageFlags.Read;
            }

            GetFolder(path).AddItem(builder);
        }

        public override void Complete() => _writer.Complete();

        public override void Dispose() => _writer.Dispose();

        private PstWriterFolder GetFolder(string[] path)
        {
            var folder = _writer.RootFolder;
            var key = "";
            foreach (var name in path)
            {
                key += "/" + name;
                if (!_folders.TryGetValue(key, out var child))
                {
                    child = folder.CreateFolder(name, "IPF.Note");
                    _folders[key] = child;
                }

                folder = child;
            }

            return folder;
        }
    }

    private sealed class MaildirDestination(MaildirFolder root) : Destination
    {
        private readonly Dictionary<string, MaildirFolder> _folders = new(StringComparer.Ordinal);

        public override void EnsureFolder(string[] path) => GetFolder(path);

        public override void Add(string[] path, SourceMessage message)
        {
            // The bytes of a MIME message are copied as they are
            var flags = message.IsRead is true ? MaildirFlags.Seen : MaildirFlags.None;
            if (message.Content is { } content)
            {
                GetFolder(path).AddMessage(content(), flags);
            }
            else
            {
                GetFolder(path).AddMessage(message.GetMime(), flags);
            }
        }

        public override void Dispose()
        {
        }

        private MaildirFolder GetFolder(string[] path)
        {
            // Maildir++: the inbox is the root, and the levels of a subfolder are separated by dots
            if (path is [] || (path is [var single] && single.Equals(InboxName, StringComparison.OrdinalIgnoreCase)))
            {
                return root;
            }

            var name = string.Join('.', path.Select(GetFileName));
            if (!_folders.TryGetValue(name, out var folder))
            {
                folder = root.CreateFolder(name);
                _folders[name] = folder;
            }

            return folder;
        }
    }

    private sealed class MboxDestination(string directory, MboxFormat format) : Destination
    {
        private readonly Dictionary<string, MboxWriter> _writers = new(StringComparer.Ordinal);

        public override void EnsureFolder(string[] path) => GetWriter(path);

        public override void Add(string[] path, SourceMessage message)
        {
            var mime = message.GetMime();
            var addStatus = message.IsRead is true && mime.Headers.Find("Status") is null;
            if (message.Content is not { } content)
            {
                if (addStatus)
                {
                    // The read state of mail clients that read mbox files (mutt, Thunderbird)
                    mime.Headers.Set("Status", MimeHeader.CreateRaw("Status", "RO"));
                }

                GetWriter(path).Write(mime, message.Sender, message.Date);
                return;
            }

            // The bytes of a MIME message are copied as they are, with the read state before its first header
            var bytes = content();
            if (addStatus)
            {
                var newLine = bytes.AsSpan().IndexOf((byte)'\n');
                var lineBreak = newLine > 0 && bytes[newLine - 1] == '\r' ? "\r\n"u8 : "\n"u8;
                bytes = [.. "Status: RO"u8, .. lineBreak, .. bytes];
            }

            var sender = message.Sender ?? (mime.From.Count > 0 ? mime.From[0].Address : null);
            GetWriter(path).Write(bytes, sender, message.Date ?? mime.Date ?? DateTimeOffset.UtcNow);
        }

        public override void Dispose()
        {
            foreach (var writer in _writers.Values)
            {
                writer.Dispose();
            }
        }

        private MboxWriter GetWriter(string[] path)
        {
            // Thunderbird: a file per folder, and the subfolders of a folder in the directory "<name>.sbd"
            if (path is [])
            {
                path = [InboxName];
            }

            var names = path.Select(GetFileName).ToArray();
            var key = string.Join('/', names);
            if (!_writers.TryGetValue(key, out var writer))
            {
                var parent = directory;
                for (var i = 0; i < names.Length - 1; i++)
                {
                    parent = Path.Combine(parent, names[i] + SubfolderSuffix);
                }

                Directory.CreateDirectory(parent);
                writer = MboxWriter.Create(Path.Combine(parent, names[^1]), format);
                _writers[key] = writer;

                // A parent folder is a file too
                if (names.Length > 1)
                {
                    GetWriter(path[..^1]);
                }
            }

            return writer;
        }
    }
}
