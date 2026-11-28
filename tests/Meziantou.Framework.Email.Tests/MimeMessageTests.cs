using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Tests;

public sealed class MimeMessageTests
{
    public static TheoryData<string> Files => [.. Directory.EnumerateFiles(GetPath(""), "*.eml").Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    internal static string GetPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "Mime", fileName);

    // The messages of mime4j that the reader reads differently. Remove a message from the list when the reader reads it as mime4j does.
    private static readonly Dictionary<string, string> Mime4jDifferences = new(StringComparer.Ordinal)
    {
        // mime4j reads the same messages in two modes, which these files expect: the reader keeps the lines that are not headers in the body
        ["malformedHeader-nocrlfcrlf"] = "mime4j drops the lines that are not headers, in its strict mode",
        ["malformedHeader-noheader"] = "mime4j drops the lines that are not headers, in its strict mode",
        ["malformedHeaderStartsBody-nocrlfcrlf"] = "mime4j drops the header that follows the first line that is not a header",
        ["malformedHeaderStartsBody-noheader"] = "mime4j keeps only the first line in the body",
        ["basic-plain-with-bad-header-separator"] = "the reader ends the header at a line of white space, which RFC 5322 reads as a folded line",
        ["obsolete"] = "the reader ends the header at an invalid line, and does not read the white space before the colon of the obsolete syntax (RFC 5322 4.5.3)",
        ["ending-boundaries"] = "the reader does not recognize a boundary followed by text on its line (the note to implementors of RFC 2046 5.1.1)",
    };

    public static TheoryData<string> Mime4jFiles => [.. Directory.EnumerateFiles(GetPath("Mime4j"), "*.eml").Select(path => Path.GetFileNameWithoutExtension(path)).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Mime4jFiles))]
    public void Mime4j_MatchesExpectedStructure(string name)
    {
        // The messages of mime4j, with the structure and the decoded content of each part that mime4j expects.
        // The headers are compared as sets: the reader keeps the content headers of a message in its body.
        var directory = GetPath("Mime4j");
        var expected = new List<string>();
        DescribeExpected(XDocument.Load(Path.Combine(directory, name + "_decoded.xml")).Root!, directory, expected, "");
        var actual = new List<string>();
        DescribeMessage(MimeMessage.Load(File.ReadAllBytes(Path.Combine(directory, name + ".eml"))), actual, "");
        if (Mime4jDifferences.TryGetValue(name, out var difference))
        {
            Assert.NotEqual(expected, actual, "The message is read as mime4j expects: remove it from the differences (" + difference + ")");
        }
        else
        {
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Load_Snippet()
    {
        var path = GetPath("attachments.eml");

        #region MimeMessage_Load
        var message = MimeMessage.Load(path);
        Assert.Equal("Attachments", message.Subject);
        Assert.Equal("alice@example.com", message.From[0].Address);
        Assert.Equal("See the attachments.", message.TextBody?.TrimEnd());

        foreach (var attachment in message.Attachments.OfType<MimePart>())
        {
            // The file name is decoded (RFC 2047 and RFC 2231), and the content is decoded from base64 or quoted-printable
            var fileName = attachment.FileName;
            var content = attachment.GetContent();
            Assert.NotNull(content);
            _ = fileName;
        }
        #endregion

        Assert.Equal(["résumé très long nom.txt", "datá.csv", "report.pdf"], message.Attachments.OfType<MimePart>().Select(part => part.FileName));
    }

    [Fact]
    public void Create_Snippet()
    {
        #region MimeMessage_Create
        var message = new MimeMessage
        {
            From = [new MailboxAddress("Alice", "alice@example.com")],
            To = [new MailboxAddress("Bob", "bob@example.com")],
            Subject = "Réunion à 10 h",
            Date = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.FromHours(1)),
            MessageId = "meeting-1@example.com",
        };

        var body = new MimeMultipart("mixed");
        var text = new MimePart();
        text.SetText("Bonjour Bob,\nÀ demain.");
        body.Parts.Add(text);

        var attachment = new MimePart
        {
            ContentType = new MimeContentType("application", "pdf"),
            ContentDisposition = new MimeContentDisposition(MimeContentDisposition.Attachment) { FileName = "ordre du jour.pdf" },
        };
        attachment.SetContent("%PDF-1.4"u8.ToArray());
        body.Parts.Add(attachment);
        message.Body = body;

        var bytes = message.ToByteArray();
        #endregion

        var read = MimeMessage.Load(bytes);
        Assert.Equal("Réunion à 10 h", read.Subject);
        Assert.Equal("Bonjour Bob,\r\nÀ demain.", read.TextBody);
        Assert.Equal("ordre du jour.pdf", Assert.IsType<MimePart>(read.Attachments.Single()).FileName);
        Assert.Equal("%PDF-1.4"u8.ToArray(), ((MimePart)read.Attachments.Single()).GetContent());
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.FromHours(1)), read.Date);

        // Every line is short enough, and the headers are ASCII
        var written = Encoding.Latin1.GetString(bytes);
        Assert.DoesNotContain(written.Split("\r\n"), line => line.Length > 998);
        Assert.True(MimeEncoding.IsPrintableAscii(written[..written.IndexOf("\r\n\r\n", StringComparison.Ordinal)].Replace("\r\n", "", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void WriteTo_UnchangedMessage_IsIdentical(string fileName)
    {
        var bytes = File.ReadAllBytes(GetPath(fileName));
        var written = MimeMessage.Load(bytes).ToByteArray();

        // The multiparts write CRLF before their delimiters: compare the messages with the same line endings
        Assert.Equal(Normalize(bytes), Normalize(written));

        static string Normalize(byte[] value) => Encoding.Latin1.GetString(value).ReplaceLineEndings("\n").TrimEnd('\n');
    }

    [Theory]
    [InlineData("=?UTF-8?B?w4lsb2RpZQ==?=", "Élodie")]
    [InlineData("=?iso-8859-1?q?Andr=E9?= =?utf-8?q?_J=C3=BCrgen?=", "André Jürgen")]
    [InlineData("a =?utf-8?q?b?= c", "a b c")]
    [InlineData("=?utf-8*fr?q?caf=C3=A9?=", "café")]
    [InlineData("=?unknown?q?x?=", "x")]
    [InlineData("=?utf-8?x?invalid?=", "=?utf-8?x?invalid?=")]
    [InlineData("no encoded word", "no encoded word")]
    public void DecodeHeaderValue(string value, string expected)
    {
        Assert.Equal(expected, MimeEncoding.DecodeHeaderValue(value));
    }

    [Theory]
    [InlineData("ASCII")]
    [InlineData("Réunion à Zürich 😀")]
    [InlineData("A very long subject with non-ASCII characters éèàùç that must be split into several encoded words because each one is limited")]
    public void EncodeHeaderText_RoundTrip(string value)
    {
        var encoded = MimeEncoding.EncodeHeaderText(value);
        Assert.True(MimeEncoding.IsPrintableAscii(encoded));
        Assert.All(encoded.Split(' ').Where(word => word.StartsWith("=?", StringComparison.Ordinal)), word => Assert.InRange(word.Length, 1, 75));
        Assert.Equal(value, MimeEncoding.DecodeHeaderValue(encoded));
    }

    [Fact]
    public void ContentId_EncodedWordIsNotDecoded()
    {
        // =?utf-8?B?YQpi?= is "a\nb": a msg-id has no encoded words, its text is the identifier
        var message = MimeMessage.Load(Encoding.ASCII.GetBytes("Content-ID: <=?utf-8?B?YQpi?=@example.com>\r\n\r\nbody"));

        Assert.Equal("=?utf-8?B?YQpi?=@example.com", message.Body.ContentId);
    }

    [Fact]
    public void QuotedPrintable_RoundTrip()
    {
        var text = "Ligne avec é et = et une fin d'espace \r\n" + new string('x', 200) + "\r\nfin\t";
        var encoded = MimeEncoding.EncodeQuotedPrintable(Encoding.UTF8.GetBytes(text));
        Assert.All(Encoding.ASCII.GetString(encoded).Split("\r\n"), line => Assert.InRange(line.Length, 0, 76));
        Assert.Equal(text, Encoding.UTF8.GetString(MimeEncoding.DecodeQuotedPrintable(encoded)));
    }

    [Theory]
    [InlineData("text/plain; charset=\"utf-8\"; format=flowed", "text/plain", "charset", "utf-8")]
    [InlineData("application/octet-stream; name*0*=utf-8''r%C3%A9; name*1=sum%C3%A9.txt", "application/octet-stream", "name", "résum%C3%A9.txt")]
    [InlineData("application/octet-stream; name*=iso-8859-1'fr'caf%E9.txt", "application/octet-stream", "name", "café.txt")]
    [InlineData("TEXT/HTML ; CHARSET = us-ascii (comment)", "TEXT/HTML", "charset", "us-ascii")]
    [InlineData("garbage", "text/plain", "charset", null)]
    public void ContentType_Parse(string value, string mimeType, string parameter, string? expected)
    {
        var contentType = MimeContentType.Parse(value);
        Assert.Equal(mimeType, contentType.MimeType);
        Assert.Equal(expected, contentType.Parameters.TryGetValue(parameter, out var actual) ? actual : null);
    }

    [Fact]
    public void ContentType_FormatNonAscii()
    {
        var contentType = new MimeContentType("text", "plain") { Name = "résumé.txt" };
        Assert.Equal("text/plain; name*=utf-8''r%C3%A9sum%C3%A9.txt", contentType.ToString());
        Assert.Equal("résumé.txt", MimeContentType.Parse(contentType.ToString()).Name);
    }

    [Theory]
    [InlineData("Tue, 1 Jul 2003 10:52:37 +0200", "2003-07-01T10:52:37+02:00")]
    [InlineData("1 Jul 2003 10:52 GMT", "2003-07-01T10:52:00+00:00")]
    [InlineData("Thu, 10 Jul 25 10:00:00 EST", "2025-07-10T10:00:00-05:00")]
    [InlineData("Mon, 14 Jul 2025 10:00:00 +0000 (UTC)", "2025-07-14T10:00:00+00:00")]
    [InlineData("31 Feb 2025 10:00:00 +0000", null)]
    [InlineData("not a date", null)]
    public void Date_Parse(string value, string? expected)
    {
        Assert.Equal(expected is null ? null : DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), MimeDate.Parse(value));
    }

    [Theory]
    [InlineData("\"Smith, John\" <john@example.com>", "Smith, John|john@example.com")]
    [InlineData("john@example.com (John)", "John|john@example.com")]
    [InlineData("Team: a@example.com, b@example.com;", "|a@example.com;|b@example.com")]
    [InlineData("<@route.example:john@example.com>", "|john@example.com")]
    [InlineData("=?utf-8?q?J=C3=BCrgen?= <j@example.com>, bob@example.com", "Jürgen|j@example.com;|bob@example.com")]
    [InlineData("", "")]
    public void Address_ParseList(string value, string expected)
    {
        Assert.Equal(expected, string.Join(';', MailboxAddress.ParseList(value).Select(address => address.Name + "|" + address.Address)));
    }

    [Fact]
    public void Address_FormatNonAscii()
    {
        var formatted = MailboxAddress.FormatList([new MailboxAddress("Jürgen Müller", "j@example.com"), new MailboxAddress("Smith, John", "john@example.com")]);
        Assert.True(MimeEncoding.IsPrintableAscii(formatted));
        Assert.Equal(["Jürgen Müller|j@example.com", "Smith, John|john@example.com"], MailboxAddress.ParseList(formatted).Select(address => address.Name + "|" + address.Address));
    }

    [Theory]
    [InlineData("a\u00A0b@example.com")]
    [InlineData("a\u2003b@example.com")]
    [InlineData("a\u000Bb@example.com")]
    [InlineData("a b@example.com")]
    public void Address_FormatWhitespace(string address)
    {
        // The reader removes the whitespace of a bare address: the writer puts the address in angle brackets
        var formatted = new MailboxAddress(name: null, address).ToHeaderValue();
        Assert.Equal("<" + address + ">", formatted);
        Assert.Equal(address, MailboxAddress.Parse(formatted)?.Address);
    }

    [Theory]
    [InlineData("@route:alice@example.com", "alice@example.com")]
    [InlineData("@a>;b:", "")]
    public void Address_FormatObsoleteRoute(string address, string expected)
    {
        // In angle brackets, @...: is an obsolete route that the reader removes: only the address after it is written
        Assert.Equal(expected, MailboxAddress.FormatList([new MailboxAddress(name: null, address)]));
    }

    [Fact]
    public void Load_DeepNesting_IsLimited()
    {
        var builder = new StringBuilder("Content-Type: multipart/mixed; boundary=b0\r\n\r\n");
        for (var i = 1; i < 200; i++)
        {
            builder.Append("--b").Append(i - 1).Append("\r\nContent-Type: multipart/mixed; boundary=b").Append(i).Append("\r\n\r\n");
        }

        var message = MimeMessage.Load(Encoding.ASCII.GetBytes(builder.ToString()), new MimeReadOptions { MaxDepth = 10 });
        var depth = 0;
        for (var entity = message.Body; entity is MimeMultipart { Parts: [var first, ..] }; entity = first)
        {
            depth++;
        }

        Assert.True(depth <= 10);
    }

    private static void DescribeExpected(XElement element, string directory, List<string> result, string indent)
    {
        switch (element.Name.LocalName)
        {
            case "message":
            case "body-part":
                result.Add(indent + element.Name.LocalName + ": " + string.Join(", ", element.Element("header")!.Elements("field").Select(field => GetFieldName(field.Value)).Order(StringComparer.OrdinalIgnoreCase)));
                foreach (var child in element.Elements().Where(child => child.Name.LocalName is not "header"))
                {
                    DescribeExpected(child, directory, result, indent + "  ");
                }

                break;
            case "multipart":
                result.Add(indent + "multipart");
                foreach (var part in element.Elements("body-part"))
                {
                    DescribeExpected(part, directory, result, indent + "  ");
                }

                break;
            case "text-body":
            case "binary-body":
                result.Add(indent + "content: " + HashContent(File.ReadAllBytes(Path.Combine(directory, element.Attribute("name")!.Value))));
                break;
        }

        static string GetFieldName(string field) => field.TrimStart('\r', '\n')[..field.TrimStart('\r', '\n').IndexOf(':', StringComparison.Ordinal)].Trim();
    }

    private static void DescribeMessage(MimeMessage message, List<string> result, string indent)
    {
        result.Add(indent + "message: " + string.Join(", ", message.Headers.Concat(message.Body.Headers).Select(header => header.Name).Order(StringComparer.OrdinalIgnoreCase)));
        DescribeBody(message.Body, result, indent + "  ");
    }

    private static void DescribeBody(MimeEntity entity, List<string> result, string indent)
    {
        switch (entity)
        {
            case MimeMultipart multipart:
                result.Add(indent + "multipart");
                foreach (var part in multipart.Parts)
                {
                    result.Add(indent + "  body-part: " + string.Join(", ", part.Headers.Select(header => header.Name).Order(StringComparer.OrdinalIgnoreCase)));
                    DescribeBody(part, result, indent + "    ");
                }

                break;
            case MimeMessagePart embedded:
                DescribeMessage(embedded.Message, result, indent);
                break;
            case MimePart part:
                result.Add(indent + "content: " + HashContent(part.GetContent()));
                break;
        }
    }

    private static string HashContent(byte[] content) => content.Length + " " + Convert.ToHexStringLower(SHA256.HashData(content))[..16];
}
