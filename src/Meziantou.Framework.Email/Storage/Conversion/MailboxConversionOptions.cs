namespace Meziantou.Framework.Email.Storage;

/// <summary>The options of <see cref="MailboxConverter"/>.</summary>
public sealed class MailboxConversionOptions
{
    /// <summary>Gets or sets the variant of the mbox files the converter reads and writes.</summary>
    public MboxFormat MboxFormat { get; set; } = MboxFormat.Mboxrd;

    /// <summary>Gets or sets the options of the Outlook data file the converter writes, or <see langword="null"/> for the default options.</summary>
    public PstWriterOptions? PstWriterOptions { get; set; }

    /// <summary>Gets or sets the options of the Outlook data file the converter reads, or <see langword="null"/> for the default options.</summary>
    public PstReadOptions? PstReadOptions { get; set; }

    /// <summary>Gets or sets the options of the messages the converter reads, or <see langword="null"/> for the default options.</summary>
    public MimeReadOptions? MimeReadOptions { get; set; }

    /// <summary>
    /// Gets or sets whether the items of an Outlook data file that are not emails (contacts, appointments, tasks, notes...) are
    /// written as email messages to an mbox or a Maildir destination. When <see langword="false"/>, the default, they are skipped
    /// and counted in <see cref="MailboxConversionResult.SkippedItemCount"/>. An Outlook data file destination keeps them as they are.
    /// </summary>
    public bool IncludeNonMailItems { get; set; }
}
