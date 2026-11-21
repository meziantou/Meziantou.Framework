namespace Meziantou.Framework.Email.Storage;

/// <summary>The options of <see cref="PstWriter"/>.</summary>
public sealed class PstWriterOptions
{
    internal static PstWriterOptions Default { get; } = new();

    /// <summary>Gets or sets the format of the file. The default is <see cref="PstFormat.Unicode"/>, the format of Outlook 2003 and later. A <see cref="PstFormat.Unicode4K"/> file is an offline file (OST), which Outlook does not open as a data file.</summary>
    public PstFormat Format { get; set; } = PstFormat.Unicode;

    /// <summary>Gets or sets how the data blocks are obfuscated. The default is <see cref="PstEncryption.Permute"/>, the default of Outlook.</summary>
    public PstEncryption Encryption { get; set; } = PstEncryption.Permute;

    /// <summary>Gets or sets the display name of the store, the name Outlook shows for the file. The default is "Personal Folders".</summary>
    public string DisplayName { get; set; } = "Personal Folders";

    /// <summary>Gets or sets the display name of the top of the folder hierarchy. The default is "Top of Personal Folders".</summary>
    public string RootFolderName { get; set; } = "Top of Personal Folders";

    /// <summary>Gets or sets the code page of the strings of an ANSI file. The default is 1252 (Western European).</summary>
    public int CodePage { get; set; } = 1252;

    /// <summary>Gets or sets the identifier of the store, the record key that the entry identifiers of its objects contain. The writer generates one when it is <see langword="null"/>; set it to write the same file from the same content.</summary>
    public Guid? StoreId { get; set; }

    /// <summary>Gets or sets the date and time the writer uses for the creation and modification dates of the items that do not set them. The writer uses the current date when it is <see langword="null"/>.</summary>
    public DateTime? Now { get; set; }
}
