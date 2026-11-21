using System.Globalization;
using System.Text;
using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>Reads and writes the files of the messages of Apple Mail (<c>.emlx</c>): the size of the message on the first line, the message, then a property list of its flags and dates.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Mail.Tests/MboxTests.cs" ref:region="EmlxFile_Usage" ref:dedent="true">
/// var emlx = EmlxFile.Load(path);
/// Assert.Equal("First", emlx.Message.Subject);
/// Assert.True(emlx.Flags.HasFlag(EmlxFlags.Read));
/// Assert.Equal(new DateTimeOffset(2025, 7, 1, 8, 52, 37, TimeSpan.Zero), emlx.DateReceived);
/// Assert.Equal("4242", emlx.Properties["remote-id"]);
///
/// using var stream = new MemoryStream();
/// EmlxFile.Save(emlx.Message, stream, EmlxFlags.Read | EmlxFlags.Flagged);
/// </code>
/// </example>
public static class EmlxFile
{
    /// <summary>Reads an .emlx file.</summary>
    /// <param name="data">The content of the file.</param>
    /// <param name="options">The options of the message, or <see langword="null"/> for the default options.</param>
    /// <returns>The message and its properties.</returns>
    /// <exception cref="InvalidMailDataException">The first line is not the size of the message, the size is larger than the file, or the property list is malformed.</exception>
    public static EmlxMessage Load(ReadOnlyMemory<byte> data, MimeReadOptions? options = null) => Load(data, options, isPartial: false);

    /// <summary>Reads an .emlx file from a stream, until its end.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="options">The options of the message, or <see langword="null"/> for the default options. <see cref="MimeReadOptions.MaxSize"/> limits the size of the file.</param>
    /// <returns>The message and its properties.</returns>
    /// <exception cref="InvalidMailDataException">The first line is not the size of the message, the size is larger than the file, or the property list is malformed.</exception>
    /// <exception cref="MailResourceLimitException">The file is larger than <see cref="MimeReadOptions.MaxSize"/>.</exception>
    public static EmlxMessage Load(Stream stream, MimeReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Load(StreamHelper.ReadAll(stream, (options ?? new MimeReadOptions()).MaxSize), options);
    }

    /// <summary>Reads an .emlx or .partial.emlx file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options of the message, or <see langword="null"/> for the default options.</param>
    /// <returns>The message and its properties.</returns>
    /// <exception cref="InvalidMailDataException">The first line is not the size of the message, the size is larger than the file, or the property list is malformed.</exception>
    /// <exception cref="MailResourceLimitException">The file is larger than <see cref="MimeReadOptions.MaxSize"/>.</exception>
    public static EmlxMessage Load(string path, MimeReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        var data = StreamHelper.ReadAll(stream, (options ?? new MimeReadOptions()).MaxSize);
        return Load(data, options, path.EndsWith(".partial.emlx", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Writes a message as an .emlx file. The lines of the message end with LF, as in the files of Apple Mail.</summary>
    /// <param name="message">The message.</param>
    /// <param name="stream">The stream.</param>
    /// <param name="flags">The flags of the message.</param>
    /// <param name="dateReceived">The date the message was received, or <see langword="null"/> for the date of the message.</param>
    public static void Save(MimeMessage message, Stream stream, EmlxFlags flags = EmlxFlags.None, DateTimeOffset? dateReceived = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(stream);
        var content = ToLf(message.ToByteArray());
        stream.Write(Encoding.ASCII.GetBytes(content.Length.ToString(CultureInfo.InvariantCulture).PadRight(10) + "\n"));
        stream.Write(content);
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["flags"] = (long)flags,
        };

        if ((dateReceived ?? message.Date) is { } date)
        {
            properties["date-received"] = date.ToUnixTimeSeconds();
        }

        PropertyList.Write(stream, properties);
    }

    /// <summary>Writes a message as an .emlx file.</summary>
    /// <param name="message">The message.</param>
    /// <param name="path">The path of the file.</param>
    /// <param name="flags">The flags of the message.</param>
    /// <param name="dateReceived">The date the message was received, or <see langword="null"/> for the date of the message.</param>
    public static void Save(MimeMessage message, string path, EmlxFlags flags = EmlxFlags.None, DateTimeOffset? dateReceived = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.Create(path);
        Save(message, stream, flags, dateReceived);
    }

    private static EmlxMessage Load(ReadOnlyMemory<byte> data, MimeReadOptions? options, bool isPartial)
    {
        var span = data.Span;
        var newLine = span.IndexOf((byte)'\n');
        if (newLine < 0 || newLine > 32 || !long.TryParse(Encoding.ASCII.GetString(span[..newLine]).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var size))
        {
            throw new InvalidMailDataException("The content is not an .emlx file: the first line is not the size of the message.");
        }

        var start = newLine + 1;
        if (size > data.Length - start)
        {
            throw new InvalidMailDataException("The size of the message of the .emlx file is larger than the file.");
        }

        var message = MimeMessage.Load(data.Slice(start, (int)size), options);
        var rest = span[(start + (int)size)..];
        var properties = rest.Trim("\r\n\t "u8).IsEmpty ? new Dictionary<string, object?>(StringComparer.Ordinal) : PropertyList.ReadDictionary(rest);
        return new EmlxMessage(message, properties, isPartial);
    }

    private static byte[] ToLf(byte[] content)
    {
        using var result = new MemoryStream(content.Length);
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
            {
                continue;
            }

            result.WriteByte(content[i]);
        }

        return result.ToArray();
    }
}
