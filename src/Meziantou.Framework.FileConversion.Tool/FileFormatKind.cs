namespace Meziantou.Framework.FileConversionTool;

internal enum FileFormatKind
{
    Image,
    Pdf,
    Epub,

    /// <summary>A comic book archive (CBZ, CBR, CB7, CBT): an archive whose images, sorted by name, are the pages.</summary>
    Comic,

    /// <summary>An archive (zip, 7z, RAR, tar...): converting it to another archive copies its entries.</summary>
    Archive,

    /// <summary>A Markdown document, converted to HTML, or laid out on pages.</summary>
    Markdown,

    /// <summary>An HTML document, converted to Markdown, or laid out on pages.</summary>
    Html,

    /// <summary>A mailbox with its folders (PST, OST, mbox, Maildir), converted to another mailbox or to a folder of message files.</summary>
    Mailbox,

    /// <summary>An email message (EML, EMLX) or an Outlook item (MSG, OFT, TNEF), converted to another message format.</summary>
    EmailMessage,
}
