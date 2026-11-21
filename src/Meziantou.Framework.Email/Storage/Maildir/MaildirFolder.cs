using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Email.Storage;

/// <summary>A Maildir folder: a directory with the subdirectories <c>tmp</c>, <c>new</c> and <c>cur</c>, and a file per message. The subfolders follow the Maildir++ convention: directories whose names start with a dot.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../../tests/Meziantou.Framework.Email.Tests/MboxTests.cs" ref:region="MaildirFolder_Usage" ref:dedent="true">
/// var folder = MaildirFolder.Create(path);
/// var message = new MimeMessage { Subject = "Hello" };
/// var body = new MimePart();
/// body.SetText("Hello from a Maildir folder.");
/// message.Body = body;
///
/// // A message without flags goes in "new", a message with flags in "cur" with the flags in its name
/// var added = folder.AddMessage(message);
/// Assert.True(added.IsNew);
/// var read = folder.SetFlags(added, MaildirFlags.Seen | MaildirFlags.Replied);
/// Assert.EndsWith("2,RS", read.FullPath, StringComparison.Ordinal);
///
/// var sent = folder.CreateFolder("Sent");
/// sent.AddMessage(message, MaildirFlags.Seen);
/// Assert.Equal(["Sent"], folder.EnumerateFolders().Select(subfolder =&gt; subfolder.Name));
/// Assert.Equal("Hello", folder.EnumerateMessages().Single().Load().Subject);
/// </code>
/// </example>
public sealed class MaildirFolder
{
    private static long s_counter;

    private MaildirFolder(string path)
    {
        FullPath = path;
    }

    /// <summary>Gets the path of the directory of the folder.</summary>
    public string FullPath { get; }

    /// <summary>Gets the character between the unique name of a message and its information when the folder writes a file name: <c>:</c>, or <c>!</c> on Windows, where a file name cannot contain <c>:</c>. The reader accepts <c>:</c>, <c>!</c> and <c>;</c>.</summary>
    public static char InfoSeparator => OperatingSystem.IsWindows() ? '!' : ':';

    /// <summary>Opens a folder.</summary>
    /// <param name="path">The path of the directory.</param>
    /// <returns>The folder.</returns>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist, or has no <c>cur</c> or <c>new</c> subdirectory.</exception>
    public static MaildirFolder Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!Directory.Exists(Path.Combine(path, "cur")) || !Directory.Exists(Path.Combine(path, "new")))
        {
            throw new DirectoryNotFoundException($"The directory '{path}' is not a Maildir folder: it has no 'cur' and 'new' subdirectories.");
        }

        return new MaildirFolder(Path.GetFullPath(path));
    }

    /// <summary>Creates a folder, or opens it when it exists.</summary>
    /// <param name="path">The path of the directory.</param>
    /// <returns>The folder.</returns>
    public static MaildirFolder Create(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        foreach (var name in new[] { "tmp", "new", "cur" })
        {
            Directory.CreateDirectory(Path.Combine(path, name));
        }

        return new MaildirFolder(Path.GetFullPath(path));
    }

    /// <summary>Returns the subfolders (Maildir++): the subdirectories whose names start with a dot, such as <c>.Sent</c> or <c>.Archive.2026</c>.</summary>
    /// <returns>The names of the subfolders, without their first dot, and their folders.</returns>
    public IEnumerable<(string Name, MaildirFolder Folder)> EnumerateFolders()
    {
        foreach (var directory in Directory.EnumerateDirectories(FullPath, ".*").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(directory);
            if (name.Length > 1 && Directory.Exists(Path.Combine(directory, "cur")))
            {
                yield return (name[1..], new MaildirFolder(directory));
            }
        }
    }

    /// <summary>Creates a subfolder (Maildir++), or opens it when it exists.</summary>
    /// <param name="name">The name of the subfolder, such as <c>Sent</c>; the levels are separated by dots (<c>Archive.2026</c>).</param>
    /// <returns>The subfolder.</returns>
    public MaildirFolder CreateFolder(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name.IndexOfAny(['/', '\\']) >= 0 || name[0] == '.')
        {
            throw new ArgumentException("The name of a subfolder cannot contain '/' or '\\', or start with a dot.", nameof(name));
        }

        return Create(Path.Combine(FullPath, "." + name));
    }

    /// <summary>Returns the messages of the <c>new</c> and <c>cur</c> directories. The files whose names start with a dot are skipped.</summary>
    /// <returns>The messages.</returns>
    public IEnumerable<MaildirMessage> EnumerateMessages()
    {
        foreach (var (directory, isNew) in new[] { ("new", true), ("cur", false) })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(FullPath, directory)).Order(StringComparer.Ordinal))
            {
                if (Path.GetFileName(file) is not ['.', ..])
                {
                    yield return new MaildirMessage(this, file, isNew);
                }
            }
        }
    }

    /// <summary>Adds a message: writes it in <c>tmp</c>, then moves it to <c>new</c>, or to <c>cur</c> when it has flags.</summary>
    /// <param name="message">The message.</param>
    /// <param name="flags">The flags of the message.</param>
    /// <returns>The message added.</returns>
    public MaildirMessage AddMessage(MimeMessage message, MaildirFlags flags = MaildirFlags.None)
    {
        ArgumentNullException.ThrowIfNull(message);
        return AddMessage(message.ToByteArray(), flags);
    }

    /// <summary>Adds the bytes of a message: writes them in <c>tmp</c>, then moves the file to <c>new</c>, or to <c>cur</c> when the message has flags.</summary>
    /// <param name="content">The bytes of the message.</param>
    /// <param name="flags">The flags of the message.</param>
    /// <returns>The message added.</returns>
    public MaildirMessage AddMessage(ReadOnlySpan<byte> content, MaildirFlags flags = MaildirFlags.None)
    {
        var key = CreateKey();
        var temporary = Path.Combine(FullPath, "tmp", key);
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
        {
            stream.Write(content);
        }

        var isNew = flags == MaildirFlags.None;
        var destination = isNew ? Path.Combine(FullPath, "new", key) : Path.Combine(FullPath, "cur", key + InfoSeparator + "2," + FormatFlags(flags));
        File.Move(temporary, destination);
        return new MaildirMessage(this, destination, isNew);
    }

    /// <summary>Sets the flags of a message: moves its file to <c>cur</c>, with the flags in its name.</summary>
    /// <param name="message">The message.</param>
    /// <param name="flags">The flags.</param>
    /// <returns>The message, with its new path.</returns>
    public MaildirMessage SetFlags(MaildirMessage message, MaildirFlags flags)
    {
        CheckFolder(message);
        var destination = Path.Combine(FullPath, "cur", message.Key + InfoSeparator + "2," + FormatFlags(flags));
        if (!string.Equals(destination, message.FullPath, StringComparison.Ordinal))
        {
            File.Move(message.FullPath, destination);
        }

        return new MaildirMessage(this, destination, isNew: false);
    }

    /// <summary>Deletes the file of a message.</summary>
    /// <param name="message">The message.</param>
    public void Delete(MaildirMessage message)
    {
        CheckFolder(message);
        File.Delete(message.FullPath);
    }

    private void CheckFolder(MaildirMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!string.Equals(message.Folder.FullPath, FullPath, StringComparison.Ordinal))
        {
            throw new ArgumentException("The message is not a message of this folder.", nameof(message));
        }
    }

    /// <summary>Returns the position of the separator of the information in the name of a file, or -1.</summary>
    internal static int FindInfo(string name)
    {
        var index = name.LastIndexOfAny([':', '!', ';']);
        if (index < 0)
        {
            return -1;
        }

        var info = name.AsSpan(index + 1);
        return info.StartsWith("2,", StringComparison.Ordinal) || info.StartsWith("1,", StringComparison.Ordinal) ? index : -1;
    }

    internal static MaildirFlags ParseFlags(ReadOnlySpan<char> value)
    {
        var flags = MaildirFlags.None;
        foreach (var c in value)
        {
            flags |= c switch
            {
                'D' => MaildirFlags.Draft,
                'F' => MaildirFlags.Flagged,
                'P' => MaildirFlags.Passed,
                'R' => MaildirFlags.Replied,
                'S' => MaildirFlags.Seen,
                'T' => MaildirFlags.Trashed,
                _ => MaildirFlags.None,
            };
        }

        return flags;
    }

    /// <summary>Returns the letters of the flags, in ASCII order as the specification requires.</summary>
    internal static string FormatFlags(MaildirFlags flags)
    {
        var builder = new StringBuilder(6);
        if (flags.HasFlag(MaildirFlags.Draft))
        {
            builder.Append('D');
        }

        if (flags.HasFlag(MaildirFlags.Flagged))
        {
            builder.Append('F');
        }

        if (flags.HasFlag(MaildirFlags.Passed))
        {
            builder.Append('P');
        }

        if (flags.HasFlag(MaildirFlags.Replied))
        {
            builder.Append('R');
        }

        if (flags.HasFlag(MaildirFlags.Seen))
        {
            builder.Append('S');
        }

        if (flags.HasFlag(MaildirFlags.Trashed))
        {
            builder.Append('T');
        }

        return builder.ToString();
    }

    /// <summary>Returns a unique name: the time, the microseconds, the process and a counter, then the host (<c>1767254400.M123456P4242Q1.host</c>).</summary>
    private static string CreateKey()
    {
        var now = DateTimeOffset.UtcNow;
        var host = new StringBuilder();
        foreach (var c in Environment.MachineName)
        {
            // '/' and ':' cannot appear in the host part ("\057" and "\072" in the specification)
            host.Append(c switch { '/' => "\\057", ':' => "\\072", '!' or ';' or '\\' => "_", _ => c.ToString() });
        }

        var micro = (now.Ticks / 10) % 1_000_000;
        return string.Create(CultureInfo.InvariantCulture, $"{now.ToUnixTimeSeconds()}.M{micro}P{Environment.ProcessId}Q{Interlocked.Increment(ref s_counter)}.{host}");
    }
}
