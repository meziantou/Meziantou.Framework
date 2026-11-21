namespace Meziantou.Framework.Email.Storage;

/// <summary>The summary of a message in the summary file of a folder of Thunderbird (<c>.msf</c>): the headers Thunderbird shows in the list of messages, and where the message is in the mbox file of the folder.</summary>
public sealed class ThunderbirdMessageSummary
{
    /// <summary>Gets or sets the key of the message in the folder (the identifier of its row).</summary>
    public long Key { get; set; }

    /// <summary>Gets or sets the subject, without the <c>Re:</c> that <see cref="ThunderbirdMessageFlags.HasRe"/> indicates.</summary>
    public string? Subject { get; set; }

    /// <summary>Gets or sets the sender, as in the <c>From</c> header.</summary>
    public string? Sender { get; set; }

    /// <summary>Gets or sets the recipients, as in the <c>To</c> header.</summary>
    public string? Recipients { get; set; }

    /// <summary>Gets or sets the <c>Cc</c> recipients.</summary>
    public string? CcList { get; set; }

    /// <summary>Gets or sets the identifier of the message, without its angle brackets.</summary>
    public string? MessageId { get; set; }

    /// <summary>Gets or sets the identifiers of the messages the message replies to, as in the <c>References</c> header.</summary>
    public string? References { get; set; }

    /// <summary>Gets or sets the date of the message.</summary>
    public DateTimeOffset? Date { get; set; }

    /// <summary>Gets or sets the date the message was received.</summary>
    public DateTimeOffset? DateReceived { get; set; }

    /// <summary>Gets or sets the flags.</summary>
    public ThunderbirdMessageFlags Flags { get; set; }

    /// <summary>Gets or sets the size of the message in bytes.</summary>
    public long? Size { get; set; }

    /// <summary>Gets or sets the number of lines of the body.</summary>
    public long? LineCount { get; set; }

    /// <summary>Gets or sets the position of the message in the mbox file of the folder (<c>storeToken</c>, or <c>msgOffset</c> in old summaries).</summary>
    public long? Offset { get; set; }

    /// <summary>Returns the subject.</summary>
    /// <returns>The subject.</returns>
    public override string ToString() => Subject ?? "";
}
