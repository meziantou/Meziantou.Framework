using Meziantou.Framework.Mail.Internals;
using Meziantou.Framework.Mail.Internals.Cfb;
using Meziantou.Framework.Mail.Internals.Msg;

namespace Meziantou.Framework.Mail;

/// <summary>Reads and writes Outlook items saved as files (.msg, [MS-OXMSG]). The item of a file is read with the types of the items of Outlook data files: a <see cref="PstMessage"/>, or a <see cref="PstContact"/>, <see cref="PstAppointment"/>, <see cref="PstTask"/> or <see cref="PstDistributionList"/>.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Mail.Tests/MsgFileTests.cs" ref:region="MsgFile_Save" ref:dedent="true">
/// var message = new PstMessageBuilder
/// {
///     Subject = "Quarterly report",
///     Body = "The report is attached.",
///     SenderName = "Alice",
///     SenderEmailAddress = "alice@example.com",
///     SenderAddressType = "SMTP",
/// };
/// message.Recipients.Add(new PstRecipientBuilder("Bob", "bob@example.com"));
/// message.Attachments.Add(PstAttachmentBuilder.FromContent("report.csv", "Quarter,Total\nQ1,42\n"u8.ToArray(), "text/csv"));
/// MsgFile.Save(message, path);
///
/// var item = MsgFile.Load(path);
/// Assert.Equal("Quarterly report", item.Subject);
/// Assert.Equal("bob@example.com", item.Recipients.Single().EmailAddress);
/// Assert.Equal("report.csv", item.Attachments.Single().Name);
/// </code>
/// </example>
public static class MsgFile
{
    /// <summary>Reads the item of an .msg file. The file is read in memory.</summary>
    /// <param name="data">The content of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default options. <see cref="PstReadOptions.MaxPropertySize"/> limits the size of the streams, and <see cref="PstReadOptions.MaxEmbeddedMessageDepth"/> the depth of the embedded messages.</param>
    /// <returns>The item.</returns>
    /// <exception cref="InvalidMailDataException">The file is not an .msg file, or its structures are malformed.</exception>
    /// <exception cref="MailResourceLimitException">A stream is larger than <see cref="PstReadOptions.MaxPropertySize"/>.</exception>
    public static PstMessage Load(ReadOnlyMemory<byte> data, PstReadOptions? options = null)
    {
        options ??= PstReadOptions.Default;
        var file = CompoundFile.Read(data, options.MaxPropertySize);
        var context = new MsgContext(file, options);
        return MsgMessageSource.Load(context, file.Root, embeddingDepth: 0, context.DefaultEncoding);
    }

    /// <summary>Reads the item of an .msg file from a stream, until its end.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default options.</param>
    /// <returns>The item.</returns>
    /// <exception cref="InvalidMailDataException">The file is not an .msg file, or its structures are malformed.</exception>
    /// <exception cref="MailResourceLimitException">A stream is larger than <see cref="PstReadOptions.MaxPropertySize"/>.</exception>
    public static PstMessage Load(Stream stream, PstReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Load(StreamHelper.ReadAll(stream, int.MaxValue), options);
    }

    /// <summary>Reads the item of an .msg file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default options.</param>
    /// <returns>The item.</returns>
    /// <exception cref="InvalidMailDataException">The file is not an .msg file, or its structures are malformed.</exception>
    /// <exception cref="MailResourceLimitException">A stream is larger than <see cref="PstReadOptions.MaxPropertySize"/>.</exception>
    public static PstMessage Load(string path, PstReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Load(File.ReadAllBytes(path), options);
    }

    /// <summary>Writes an item as an .msg file, with Unicode strings.</summary>
    /// <param name="item">The item: a message, a contact, an appointment, a task or a distribution list.</param>
    /// <param name="stream">The stream.</param>
    public static void Save(PstMessageBuilder item, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(stream);
        MsgWriter.Write(item, stream);
    }

    /// <summary>Writes an item as an .msg file, with Unicode strings.</summary>
    /// <param name="item">The item: a message, a contact, an appointment, a task or a distribution list.</param>
    /// <param name="path">The path of the file.</param>
    public static void Save(PstMessageBuilder item, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.Create(path);
        Save(item, stream);
    }

    /// <summary>Returns the content of an .msg file of an item.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The content of the file.</returns>
    public static byte[] ToByteArray(PstMessageBuilder item)
    {
        using var stream = new MemoryStream();
        Save(item, stream);
        return stream.ToArray();
    }
}
