using Meziantou.Framework.Email;
using Meziantou.Framework.Email.Mapi;
using Meziantou.Framework.Email.Storage;

namespace Meziantou.Framework.FileConversionTool.Outputs;

/// <summary>Converts a mailbox to another mailbox format or to a folder of message files, and a message to another message format.</summary>
internal static class EmailOutputWriter
{
    /// <summary>
    /// Converts a mailbox with <see cref="MailboxConverter"/>, in a temporary path next to the output, so that a failed conversion
    /// does not leave a partial mailbox. An Outlook data file replaces the output file; a folder is never replaced, even with
    /// <c>--overwrite</c>, so that a wrong path does not delete a folder.
    /// </summary>
    public static void WriteMailbox(FullPath inputPath, FullPath outputPath, FileFormat outputFormat, ConversionSettings settings, TextWriter error)
    {
        var options = settings.CreateMailboxOptions(outputFormat);
        var isDirectory = outputFormat.MailboxFormat is not MailboxFormat.Pst;
        if (Directory.Exists(outputPath))
        {
            if (!isDirectory)
                throw new ConversionException($"The output '{outputPath}' is a folder");

            if (Directory.EnumerateFileSystemEntries(outputPath).Any())
                throw new ConversionException($"The output folder '{outputPath}' is not empty: the tool does not replace a folder, even with --overwrite. Remove it, or set another --output");
        }

        var temporaryPath = outputPath.Parent / ("." + Guid.NewGuid().ToString("N")[..16] + ".tmp");
        MailboxConversionResult result;
        var moved = false;
        try
        {
            result = MailboxConverter.Convert(inputPath, temporaryPath, outputFormat.MailboxFormat!.Value, options);
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

        foreach (var message in result.Errors)
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
        var data = File.ReadAllBytes(inputPath);
        MimeMessage? mime = null;
        MapiMessage? item = null;
        var isRead = true;
        DateTimeOffset? dateReceived = null;
        if (inputFormat == FileFormat.Eml)
        {
            mime = MimeMessage.Load(data);
        }
        else if (inputFormat == FileFormat.Emlx)
        {
            var emlx = EmlxFile.Load(data);
            mime = emlx.Message;
            isRead = emlx.Flags.HasFlag(EmlxFlags.Read);
            dateReceived = emlx.DateReceived;
        }
        else if (inputFormat == FileFormat.Tnef)
        {
            item = TnefFile.Load(data);
            isRead = item.IsRead;
        }
        else
        {
            item = MsgFile.Load(data);
            isRead = item.IsRead;
        }

        ArchiveFiles.WriteAtomically(outputPath, stream =>
        {
            if (outputFormat == FileFormat.Eml)
            {
                stream.Write(ToCrLf(GetMime().ToByteArray()));
            }
            else if (outputFormat == FileFormat.Emlx)
            {
                EmlxFile.Save(GetMime(), stream, isRead ? EmlxFlags.Read : EmlxFlags.None, dateReceived);
            }
            else if (outputFormat == FileFormat.Tnef)
            {
                TnefFile.Save(GetBuilder(), stream);
            }
            else
            {
                MsgFile.Save(GetBuilder(), stream, template: outputFormat == FileFormat.Oft);
            }
        });

        MimeMessage GetMime() => mime ?? EmailMessage.FromMapi(item!).ToMime();

        // The writer writes the lines it adds (the end of the headers, the delimiters of the multiparts) with CRLF
        static byte[] ToCrLf(byte[] content)
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

        MapiMessageBuilder GetBuilder()
        {
            if (item is not null)
                return MapiMessageBuilder.FromMessage(item);

            var builder = EmailMessage.FromMime(mime!).ToMapiBuilder();
            builder.Flags = isRead ? MapiMessageFlags.Read : MapiMessageFlags.None;
            return builder;
        }
    }
}
