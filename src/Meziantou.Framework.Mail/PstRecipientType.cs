namespace Meziantou.Framework.Mail;

/// <summary>The type of a recipient (PidTagRecipientType).</summary>
public enum PstRecipientType
{
    /// <summary>The sender of the message.</summary>
    Originator = 0,

    /// <summary>A primary recipient.</summary>
    To = 1,

    /// <summary>A carbon copy recipient.</summary>
    Cc = 2,

    /// <summary>A blind carbon copy recipient.</summary>
    Bcc = 3,
}
