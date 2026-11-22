using System.Text;

namespace Meziantou.Framework.Email.Tests;

/// <summary>Detects the format of every file of TestData, from its content, and of inputs that look like none of the formats.</summary>
public sealed class EmailFormatDetectorTests
{
    public static TheoryData<string> Files => [.. Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*", SearchOption.AllDirectories)
        .Where(path => GetExpectedFormat(path) is not null)
        .Select(path => Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "TestData"), path).Replace('\\', '/'))
        .Order(StringComparer.Ordinal)];

    [Fact]
    public void Usage_Snippet()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Msg", "poi-quick.msg");

        #region EmailFormatDetector_Usage
        var format = EmailFormatDetector.Detect(path);
        Assert.Equal(EmailFileFormat.Msg, format);

        Assert.Equal(EmailFileFormat.VCard, EmailFormatDetector.Detect("BEGIN:VCARD\r\nVERSION:4.0\r\nFN:Alice\r\nEND:VCARD\r\n"u8));
        Assert.Equal(EmailFileFormat.Eml, EmailFormatDetector.Detect("From: alice@example.com\r\nSubject: Hello\r\n\r\nHi"u8));
        Assert.Equal(EmailFileFormat.Unknown, EmailFormatDetector.Detect("Hello world"u8));
        #endregion
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void TestFile_Detected(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", relativePath);
        var expected = GetExpectedFormat(path);
        Assert.Equal(expected, EmailFormatDetector.Detect(path));
        Assert.Equal(expected, EmailFormatDetector.Detect(File.ReadAllBytes(path)));

        // The position of a stream is restored
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        stream.Position = 0;
        Assert.Equal(expected, EmailFormatDetector.Detect(stream));
        Assert.Equal(0, stream.Position);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Hello world")]
    [InlineData("<html><body>x</body></html>")]
    [InlineData("X-Custom: value\r\n\r\nbody")]
    [InlineData(" folded: line\r\n")]
    [InlineData("Subject without colon\r\n")]
    [InlineData("123\nnot a header")]
    [InlineData("{\\rtf1\\ansi x}")]
    public void Unknown(string content)
    {
        Assert.Equal(EmailFileFormat.Unknown, EmailFormatDetector.Detect(Encoding.UTF8.GetBytes(content)));
    }

    [Theory]
    [InlineData("\uFEFFFrom alice@example.com Mon Jan  1 00:00:00 2024\nSubject: x\n\nbody\n", EmailFileFormat.Mbox)]
    [InlineData("\r\n begin:vcalendar\r\nEND:VCALENDAR", EmailFileFormat.ICalendar)]
    [InlineData("Received: from x\r\n\tby y\r\nX-Other: z\r\n\r\n", EmailFileFormat.Eml)]
    [InlineData("12  \r\nFrom: a@b.c\r\n\r\nx", EmailFileFormat.Emlx)]
    public void Text(string content, EmailFileFormat expected)
    {
        Assert.Equal(expected, EmailFormatDetector.Detect(Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void CompoundFileThatIsNotAnItem()
    {
        // A compound file without the property stream of an Outlook item, such as a Word document
        var builder = new MapiMessageBuilder { Subject = "x" };
        var bytes = MsgFile.ToByteArray(builder);
        Assert.Equal(EmailFileFormat.Msg, EmailFormatDetector.Detect(bytes));
        Assert.Equal(EmailFileFormat.Unknown, EmailFormatDetector.Detect(bytes.AsSpan(0, 512)));
    }

    [Fact]
    public void MaildirFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "maildir-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(path);
            Assert.Equal(EmailFileFormat.Unknown, EmailFormatDetector.Detect(path));
            foreach (var name in new[] { "cur", "new", "tmp" })
            {
                Directory.CreateDirectory(Path.Combine(path, name));
            }

            Assert.Equal(EmailFileFormat.Maildir, EmailFormatDetector.Detect(path));
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static EmailFileFormat? GetExpectedFormat(string path) => Path.GetExtension(path).ToUpperInvariant() switch
    {
        ".PST" => EmailFileFormat.Pst,
        ".OST" => EmailFileFormat.Ost,
        ".MSG" => EmailFileFormat.Msg,
        ".DAT" => EmailFileFormat.Tnef,
        ".OAB" => EmailFileFormat.OfflineAddressBook,
        ".EML" => EmailFileFormat.Eml,
        ".EMLX" => EmailFileFormat.Emlx,
        ".MBOX" => EmailFileFormat.Mbox,
        ".MSF" or ".MAB" => EmailFileFormat.Mork,
        ".VCF" => EmailFileFormat.VCard,
        ".ICS" => EmailFileFormat.ICalendar,
        ".RTF" => EmailFileFormat.Unknown,
        _ => null,
    };
}
