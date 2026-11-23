using Meziantou.Framework.Email;
using Meziantou.Framework.Email.Mapi;
using Meziantou.Framework.Email.Storage;

namespace Meziantou.Framework.FileConversionTool.Outputs;

/// <summary>Converts a mailbox to another mailbox format or to a folder of message files, a message to another message format, and messages to a mailbox.</summary>
internal static class EmailOutputWriter
{
    private const string InboxName = "Inbox";

    /// <summary>
    /// Converts a mailbox with <see cref="MailboxConverter"/>, in a temporary path next to the output, so that a failed conversion
    /// does not leave a partial mailbox. An Outlook data file replaces the output file; a folder is never replaced, even with
    /// <c>--overwrite</c>, so that a wrong path does not delete a folder.
    /// </summary>
    public static void WriteMailbox(FullPath inputPath, FullPath outputPath, FileFormat outputFormat, ConversionSettings settings, TextWriter error)
    {
        var options = settings.CreateMailboxOptions(outputFormat);
        MailboxConversionResult? result = null;
        WriteThroughTemporaryPath(outputPath, isDirectory: outputFormat.MailboxFormat is not MailboxFormat.Pst, temporaryPath => result = MailboxConverter.Convert(inputPath, temporaryPath, outputFormat.MailboxFormat!.Value, options));

        foreach (var message in result!.Errors)
        {
            error.WriteLine($"{inputPath}: {message}");
        }

        if (result.SkippedItemCount > 0)
        {
            error.WriteLine($"{inputPath}: skipped the items that are not emails (contacts, appointments, tasks...): {result.SkippedItemCount.ToString(CultureInfo.InvariantCulture)}. Use --email-include-non-mail-items to write them as messages");
        }

        if (result.FolderCount is 0)
        {
            error.WriteLine($"{inputPath}: the mailbox has no folder");
        }
    }

    /// <summary>
    /// Converts a message: an email message (EML, EMLX) or an Outlook item (MSG, OFT, TNEF). The bytes of an email message are kept
    /// in an EML output, with CRLF line breaks ([RFC 5322] 2.1): an EMLX file has LF line breaks. The other conversions go through
    /// <see cref="EmailMessage"/>. A message whose source does not tell whether it is read (EML) is read.
    /// </summary>
    public static void WriteMessage(FullPath inputPath, FileFormat inputFormat, FullPath outputPath, FileFormat outputFormat)
    {
        var message = LoadMessage(inputPath, inputFormat);
        var isRead = message.IsRead ?? true;
        ArchiveFiles.WriteAtomically(outputPath, stream =>
        {
            if (outputFormat == FileFormat.Eml)
            {
                // The bytes of an EML input: writing the parsed message changes the headers the reader does not keep as they are
                stream.Write(ToCrLf(message.Content ?? message.GetMime().ToByteArray()));
            }
            else if (outputFormat == FileFormat.Emlx)
            {
                EmlxFile.Save(message.GetMime(), stream, isRead ? EmlxFlags.Read : EmlxFlags.None, message.DateReceived);
            }
            else if (outputFormat == FileFormat.Tnef)
            {
                TnefFile.Save(message.GetBuilder(), stream);
            }
            else
            {
                MsgFile.Save(message.GetBuilder(), stream, template: outputFormat == FileFormat.Oft);
            }
        });
    }

    /// <summary>
    /// Writes messages, in order, to one mailbox: an mbox file, or the inbox of an Outlook data file or of a Maildir folder. A
    /// message that cannot be read or converted is reported and skipped.
    /// </summary>
    /// <exception cref="ConversionException">No message can be converted.</exception>
    public static void WriteMessages(IReadOnlyList<(FullPath Path, FileFormat Format)> inputs, FullPath outputPath, FileFormat outputFormat, ConversionSettings settings, TextWriter error)
    {
        var options = settings.CreateMailboxOptions(outputFormat);
        if (outputFormat == FileFormat.Mbox)
        {
            ArchiveFiles.WriteAtomically(outputPath, stream =>
            {
                using var writer = new MboxWriter(stream, settings.MboxFormat, leaveOpen: true);
                AddMessages(inputs, error, (input, message) => WriteMbox(writer, input, message));
            });
        }
        else if (outputFormat == FileFormat.Maildir)
        {
            WriteThroughTemporaryPath(outputPath, isDirectory: true, temporaryPath =>
            {
                var root = MaildirFolder.Create(temporaryPath);
                AddMessages(inputs, error, (_, message) => root.AddMessage(message.Content ?? message.GetMime().ToByteArray(), message.IsRead is true ? MaildirFlags.Seen : MaildirFlags.None));
            });
        }
        else
        {
            // PST, OST
            WriteThroughTemporaryPath(outputPath, isDirectory: false, temporaryPath =>
            {
                using var writer = PstWriter.Create(temporaryPath, options.PstWriterOptions);
                var inbox = writer.RootFolder.CreateFolder(InboxName, "IPF.Note");
                AddMessages(inputs, error, (_, message) => inbox.AddItem(message.GetBuilder()));
                writer.Complete();
            });
        }
    }

    private static void AddMessages(IReadOnlyList<(FullPath Path, FileFormat Format)> inputs, TextWriter error, Action<FullPath, LoadedMessage> add)
    {
        var count = 0;
        foreach (var (path, format) in inputs)
        {
            try
            {
                add(path, LoadMessage(path, format));
                count++;
            }
            catch (EmailException ex)
            {
                // A message that is not valid, as a message of a mailbox the converter skips
                error.WriteLine($"{path}: {ex.Message}");
            }
        }

        if (count is 0)
            throw new ConversionException("No message can be converted");
    }

    /// <summary>Writes a message to an mbox file: the bytes of an email message as they are, and the read state of the other messages in a <c>Status</c> header, as mutt and Thunderbird read it.</summary>
    private static void WriteMbox(MboxWriter writer, FullPath path, LoadedMessage message)
    {
        var mime = message.GetMime();
        var date = mime.Date ?? message.DateReceived ?? new DateTimeOffset(File.GetLastWriteTimeUtc(path));
        if (message.Content is { } content)
        {
            writer.Write(content, mime.From.Count > 0 ? mime.From[0].Address : null, date);
            return;
        }

        if (message.IsRead is true && mime.Headers.Find("Status") is null)
        {
            mime.Headers.Set("Status", MimeHeader.CreateRaw("Status", "RO"));
        }

        writer.Write(mime, sender: null, date);
    }

    private static LoadedMessage LoadMessage(FullPath path, FileFormat format)
    {
        var data = File.ReadAllBytes(path);
        if (format == FileFormat.Eml)
            return new LoadedMessage(MimeMessage.Load(data), Item: null, data, IsRead: null, DateReceived: null);

        if (format == FileFormat.Emlx)
        {
            var emlx = EmlxFile.Load(data);
            return new LoadedMessage(emlx.Message, Item: null, Content: null, emlx.Flags.HasFlag(EmlxFlags.Read), emlx.DateReceived);
        }

        var item = format == FileFormat.Tnef ? TnefFile.Load(data) : MsgFile.Load(data);
        return new LoadedMessage(Mime: null, item, Content: null, item.IsRead, DateReceived: null);
    }

    /// <summary>
    /// Writes an output through a temporary path next to it, then moves it: a conversion that fails leaves no partial output. A
    /// folder output must not exist or be empty: it is never replaced, even with <c>--overwrite</c>, so that a wrong path does not
    /// delete a folder. A file output replaces the file.
    /// </summary>
    private static void WriteThroughTemporaryPath(FullPath outputPath, bool isDirectory, Action<FullPath> write)
    {
        if (Directory.Exists(outputPath))
        {
            if (!isDirectory)
                throw new ConversionException($"The output '{outputPath}' is a folder");

            if (Directory.EnumerateFileSystemEntries(outputPath).Any())
                throw new ConversionException($"The output folder '{outputPath}' is not empty: the tool does not replace a folder, even with --overwrite. Remove it, or set another --output");
        }

        var temporaryPath = outputPath.Parent / ("." + Guid.NewGuid().ToString("N")[..16] + ".tmp");
        var moved = false;
        try
        {
            write(temporaryPath);
            if (isDirectory)
            {
                // A file --overwrite replaces, or an empty folder (Delete does not delete a folder that is not empty)
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                else if (Directory.Exists(outputPath))
                {
                    Directory.Delete(outputPath);
                }

                Directory.Move(temporaryPath, outputPath);
            }
            else
            {
                File.Move(temporaryPath, outputPath, overwrite: true);
            }

            moved = true;
        }
        finally
        {
            // Only the path this call created
            if (!moved)
            {
                if (Directory.Exists(temporaryPath))
                {
                    Directory.Delete(temporaryPath, recursive: true);
                }
                else if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    // The writer writes the lines it adds (the end of the headers, the delimiters of the multiparts) with CRLF
    private static byte[] ToCrLf(byte[] content)
    {
        if (content.AsSpan().Count((byte)'\n') == content.AsSpan().Count("\r\n"u8))
            return content;

        using var result = new MemoryStream(content.Length + (content.Length / 32));
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n' && (i == 0 || content[i - 1] != '\r'))
            {
                result.WriteByte((byte)'\r');
            }

            result.WriteByte(content[i]);
        }

        return result.ToArray();
    }

    /// <summary>A message read from a file: an email message, with its bytes for an EML file, or an Outlook item, with its read state when the format stores it.</summary>
    private sealed record LoadedMessage(MimeMessage? Mime, MapiMessage? Item, byte[]? Content, bool? IsRead, DateTimeOffset? DateReceived)
    {
        private MimeMessage? _converted;

        public MimeMessage GetMime() => Mime ?? (_converted ??= EmailMessage.FromMapi(Item!).ToMime());

        /// <summary>Returns the message as an Outlook item: an email message is read unless its source says it is unread.</summary>
        public MapiMessageBuilder GetBuilder()
        {
            if (Item is not null)
                return MapiMessageBuilder.FromMessage(Item);

            var builder = EmailMessage.FromMime(Mime!).ToMapiBuilder();
            builder.Flags = IsRead is false ? MapiMessageFlags.None : MapiMessageFlags.Read;
            return builder;
        }
    }
}
