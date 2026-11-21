namespace Meziantou.Framework.Email;

/// <summary>The formats <see cref="EmailFormatDetector"/> recognizes.</summary>
public enum EmailFileFormat
{
    /// <summary>The content is not in a format the library reads.</summary>
    Unknown,

    /// <summary>An Outlook data file (<c>.pst</c>), read with <see cref="Storage.PstFile"/>.</summary>
    Pst,

    /// <summary>An offline Outlook data file (<c>.ost</c>), read with <see cref="Storage.PstFile"/>.</summary>
    Ost,

    /// <summary>An Outlook item (<c>.msg</c>), read with <see cref="Mapi.MsgFile"/>.</summary>
    Msg,

    /// <summary>A TNEF stream (<c>winmail.dat</c>), read with <see cref="Mapi.TnefFile"/>.</summary>
    Tnef,

    /// <summary>An offline address book (<c>udetails.oab</c>), read with <see cref="Storage.OabFile"/>.</summary>
    OfflineAddressBook,

    /// <summary>An email message (<c>.eml</c>), read with <see cref="MimeMessage"/>.</summary>
    Eml,

    /// <summary>An Apple Mail message (<c>.emlx</c>), read with <see cref="Storage.EmlxFile"/>.</summary>
    Emlx,

    /// <summary>An mbox file, read with <see cref="Storage.MboxReader"/>.</summary>
    Mbox,

    /// <summary>A Maildir folder, read with <see cref="Storage.MaildirFolder"/>.</summary>
    Maildir,

    /// <summary>A Mork database, such as a Thunderbird summary (<c>.msf</c>), read with <see cref="Storage.MorkDocument"/>.</summary>
    Mork,

    /// <summary>A vCard file (<c>.vcf</c>), read with <see cref="Contacts.VCard"/>.</summary>
    VCard,

    /// <summary>An iCalendar file (<c>.ics</c>), read with <see cref="Calendar.VCalendar"/>.</summary>
    ICalendar,
}
