using System.Globalization;
using System.Security.Cryptography;

namespace Meziantou.Framework.Email.Tests;

/// <summary>Compares what the library reads from the messages of TestData/Mime with what MimeKit, an independent parser, reads.</summary>
public sealed class MimeOracleTests
{
    static MimeOracleTests()
    {
        // MimeKit uses Encoding.GetEncoding for the charsets: without the provider, it reads windows-1252 and ISO-8859-15 as Latin-1
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public static TheoryData<string> Files => [.. Directory.EnumerateFiles(GetPath(""), "*.eml").Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    internal static string GetPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "Mime", fileName);

    [Theory]
    [MemberData(nameof(Files))]
    public void Message_MatchesMimeKit(string fileName)
    {
        var bytes = File.ReadAllBytes(GetPath(fileName));
        var expected = DescribeWithMimeKit(MimeKit.MimeMessage.Load(new MemoryStream(bytes)));
        var actual = Describe(MimeMessage.Load(bytes));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Written_MatchesMimeKit(string fileName)
    {
        // The messages the library writes, from a message it read, read the same in MimeKit
        var message = EmailMessage.FromMime(MimeMessage.Load(File.ReadAllBytes(GetPath(fileName)))).ToMime();
        var bytes = message.ToByteArray();
        Assert.Equal(DescribeWithMimeKit(MimeKit.MimeMessage.Load(new MemoryStream(bytes))), Describe(MimeMessage.Load(bytes)));
    }

    internal static List<string> Describe(MimeMessage message)
    {
        var result = new List<string>
        {
            "subject: " + message.Subject,
            "from: " + string.Join(", ", message.From.Select(Format)),
            "to: " + string.Join(", ", message.To.Select(Format)),
            "cc: " + string.Join(", ", message.Cc.Select(Format)),
            "date: " + message.Date?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            "message-id: " + message.MessageId,
            "text: " + Normalize(message.TextBody),
            "html: " + Normalize(message.HtmlBody),
        };

        foreach (var entity in message.Attachments)
        {
            result.Add(entity switch
            {
                MimePart part => $"part: {part.ContentType.MimeType.ToLowerInvariant()} {part.FileName} {Hash(part.GetContent())}",
                MimeMessagePart embedded => "message: " + string.Join(" | ", Describe(embedded.Message)),
                _ => "other",
            });
        }

        return result;

        static string Format(MailboxAddress address) => address.Name + " <" + address.Address + ">";
    }

    internal static List<string> DescribeWithMimeKit(MimeKit.MimeMessage message)
    {
        var leaves = message.BodyParts.ToList();
        var text = leaves.OfType<MimeKit.TextPart>().FirstOrDefault(part => part.ContentType.IsMimeType("text", "plain") && !part.IsAttachment);
        var html = leaves.OfType<MimeKit.TextPart>().FirstOrDefault(part => part.ContentType.IsMimeType("text", "html") && !part.IsAttachment);
        var result = new List<string>
        {
            "subject: " + message.Subject,
            "from: " + string.Join(", ", message.From.Mailboxes.Select(Format)),
            "to: " + string.Join(", ", message.To.Mailboxes.Select(Format)),
            "cc: " + string.Join(", ", message.Cc.Mailboxes.Select(Format)),
            "date: " + (message.Headers.Contains(MimeKit.HeaderId.Date) && message.Date != DateTimeOffset.MinValue ? message.Date.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) : null),
            "message-id: " + message.MessageId,
            "text: " + Normalize(text?.Text),
            "html: " + Normalize(html?.Text),
        };

        foreach (var entity in leaves)
        {
            if (entity == text || entity == html)
            {
                continue;
            }

            switch (entity)
            {
                case MimeKit.MimePart part:
                    using (var content = new MemoryStream())
                    {
                        part.Content?.DecodeTo(content);
                        result.Add($"part: {part.ContentType.MimeType.ToLowerInvariant()} {part.FileName} {Hash(content.ToArray())}");
                    }

                    break;
                case MimeKit.MessagePart { Message: not null } embedded:
                    result.Add("message: " + string.Join(" | ", DescribeWithMimeKit(embedded.Message)));
                    break;
                default:
                    result.Add("other");
                    break;
            }
        }

        return result;

        static string Format(MimeKit.MailboxAddress address) => address.Name + " <" + address.Address + ">";
    }

    private static string? Normalize(string? text) => text?.ReplaceLineEndings("\n").TrimEnd();

    private static string Hash(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content))[..16];
}
