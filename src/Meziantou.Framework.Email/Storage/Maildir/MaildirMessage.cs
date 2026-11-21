namespace Meziantou.Framework.Email.Storage;

/// <summary>A message of a Maildir folder: a file of its <c>new</c> or <c>cur</c> directory.</summary>
public sealed class MaildirMessage
{
    internal MaildirMessage(MaildirFolder folder, string path, bool isNew)
    {
        Folder = folder;
        FullPath = path;
        IsNew = isNew;
        var name = System.IO.Path.GetFileName(path);
        var info = MaildirFolder.FindInfo(name);
        if (info >= 0)
        {
            Key = name[..info];
            Info = name[(info + 1)..];
            Flags = Info.StartsWith("2,", StringComparison.Ordinal) ? MaildirFolder.ParseFlags(Info.AsSpan(2)) : MaildirFlags.None;
        }
        else
        {
            Key = name;
        }
    }

    /// <summary>Gets the folder of the message.</summary>
    public MaildirFolder Folder { get; }

    /// <summary>Gets the path of the file of the message.</summary>
    public string FullPath { get; }

    /// <summary>Gets the unique name of the message: the name of its file, without its information (<c>:2,FLAGS</c>).</summary>
    public string Key { get; }

    /// <summary>Gets the information of the name of the file, such as <c>2,RS</c>, or <see langword="null"/> when the name has none.</summary>
    public string? Info { get; }

    /// <summary>Gets the flags of the message.</summary>
    public MaildirFlags Flags { get; }

    /// <summary>Gets whether the message is in the <c>new</c> directory: delivered, and not yet seen by a mail client.</summary>
    public bool IsNew { get; }

    /// <summary>Reads the message.</summary>
    /// <param name="options">The options, or <see langword="null"/> for the default options.</param>
    /// <returns>The message.</returns>
    /// <exception cref="EmailResourceLimitException">The message is larger than <see cref="MimeReadOptions.MaxSize"/>.</exception>
    public MimeMessage Load(MimeReadOptions? options = null) => MimeMessage.Load(FullPath, options);

    /// <summary>Returns the key of the message.</summary>
    /// <returns>The key of the message.</returns>
    public override string ToString() => Key;
}
