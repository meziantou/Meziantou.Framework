namespace Meziantou.Framework.Email.Storage;

/// <summary>What <see cref="MailboxConverter"/> converted.</summary>
public sealed class MailboxConversionResult
{
    private readonly List<string> _errors = [];

    internal MailboxConversionResult()
    {
    }

    /// <summary>Gets the number of folders written, the inbox included.</summary>
    public int FolderCount { get; internal set; }

    /// <summary>Gets the number of messages written.</summary>
    public int MessageCount { get; internal set; }

    /// <summary>Gets the number of items of an Outlook data file that are not emails and were not written (see <see cref="MailboxConversionOptions.IncludeNonMailItems"/>).</summary>
    public int SkippedItemCount { get; internal set; }

    /// <summary>Gets the messages that could not be read or written, with the folder they are in and the reason: the converter skips them and converts the others.</summary>
    public IReadOnlyList<string> Errors => _errors;

    internal void AddError(IReadOnlyList<string> folder, string message) => _errors.Add((folder.Count == 0 ? "" : string.Join('/', folder) + ": ") + message);
}
