namespace Meziantou.Framework.Email.Storage;

/// <summary>The options of <see cref="MboxReader"/>.</summary>
public sealed class MboxReadOptions
{
    internal static MboxReadOptions Default { get; } = new();

    /// <summary>Gets or sets the variant of the format. The default is <see cref="MboxFormat.Mboxrd"/>.</summary>
    public MboxFormat Format { get; set; } = MboxFormat.Mboxrd;

    /// <summary>Gets or sets the options of the messages. <see cref="MimeReadOptions.MaxSize"/> limits the size of a message.</summary>
    public MimeReadOptions MessageOptions { get; set; } = new();
}
