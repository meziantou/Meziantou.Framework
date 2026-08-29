using Meziantou.Xunit;

namespace Meziantou.Framework.Win32.Tests;

public sealed class MarkOfTheWebTests
{
    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void Get()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, []);
        Assert.Equal(UrlZone.LocalMachine, MarkOfTheWeb.GetFileZone(path));

        Assert.False(MarkOfTheWeb.IsUntrusted(path));
        File.Delete(path);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void Set_Get()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, []);

        MarkOfTheWeb.SetFileZone(path, UrlZone.Internet);

        var zoneContent = MarkOfTheWeb.GetFileZoneContent(path);
        Assert.NotNull(zoneContent);
        Assert.Equal("[ZoneTransfer]\nZoneId=3\n", zoneContent.ReplaceLineEndings("\n"));
        Assert.Equal(UrlZone.Internet, MarkOfTheWeb.GetFileZone(path));
        Assert.True(MarkOfTheWeb.IsUntrusted(path));

        File.Delete(path);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void Set_Delete()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, []);

        MarkOfTheWeb.SetFileZone(path, UrlZone.Internet);
        var zoneContent = MarkOfTheWeb.GetFileZoneContent(path);
        Assert.NotNull(zoneContent);
        Assert.NotEmpty(zoneContent);

        MarkOfTheWeb.RemoveFileZone(path);
        Assert.Null(MarkOfTheWeb.GetFileZoneContent(path));
        Assert.Equal(UrlZone.LocalMachine, MarkOfTheWeb.GetFileZone(path));
        Assert.False(MarkOfTheWeb.IsUntrusted(path));

        File.Delete(path);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void RemoveFileZone_DoesNothing_WhenTheFileHasNoZoneInformation()
    {
        var path = Path.GetTempFileName();
        try
        {
            MarkOfTheWeb.RemoveFileZone(path);

            Assert.Null(MarkOfTheWeb.GetFileZoneContent(path));
            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void RemoveFileZone_DoesNothing_WhenTheDirectoryDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "missing.txt");

        MarkOfTheWeb.RemoveFileZone(path);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetFileZoneContent_ReturnsNull_WhenTheDirectoryDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "missing.txt");

        Assert.Null(MarkOfTheWeb.GetFileZoneContent(path));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetFileZoneContent_ReadsTheAsciiStreamWrittenByWindowsAndBrowsers()
    {
        var path = Path.GetTempFileName();
        try
        {
            const string Content = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.com/file.txt\r\n";
            File.WriteAllBytes(path + ":Zone.Identifier", Encoding.ASCII.GetBytes(Content));

            Assert.Equal(Content, MarkOfTheWeb.GetFileZoneContent(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetFileZoneContent_ReadsTheUtf16StreamWrittenByEarlierVersions()
    {
        var path = Path.GetTempFileName();
        try
        {
            const string Content = "[ZoneTransfer]\r\nZoneId=3\r\n";
            File.WriteAllBytes(path + ":Zone.Identifier", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Content)]);

            Assert.Equal(Content, MarkOfTheWeb.GetFileZoneContent(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void SetFileZone_WritesTheStreamAsAsciiWithoutAByteOrderMark()
    {
        var path = Path.GetTempFileName();
        try
        {
            MarkOfTheWeb.SetFileZone(path, UrlZone.Internet, hostUrl: "https://example.com/file.txt");

            var bytes = File.ReadAllBytes(path + ":Zone.Identifier");
            Assert.All(bytes, b => b is >= 0x09 and <= 0x7F);
            Assert.Equal("[ZoneTransfer]\nZoneId=3\nHostUrl=https://example.com/file.txt\n", Encoding.ASCII.GetString(bytes).ReplaceLineEndings("\n"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void SetFileZone_PreservesNonAsciiCharactersInUrls()
    {
        var path = Path.GetTempFileName();
        try
        {
            MarkOfTheWeb.SetFileZone(path, UrlZone.Internet, hostUrl: "https://example.com/café.txt");

            Assert.Contains("café.txt", MarkOfTheWeb.GetFileZoneContent(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory, RunIf(TestOperatingSystems.Windows)]
    [InlineData("https://example.com/\r\nZoneId=0")]
    [InlineData("https://example.com/\nZoneId=0")]
    [InlineData("https://example.com/\0")]
    public void SetFileZone_ReferrerUrl_RejectsControlCharacters(string referrerUrl)
    {
        var path = Path.GetTempFileName();
        try
        {
            var exception = Assert.Throws<ArgumentException>(() => MarkOfTheWeb.SetFileZone(path, UrlZone.Internet, referrerUrl));
            Assert.Equal("referrerUrl", exception.ParamName);
            Assert.Null(MarkOfTheWeb.GetFileZoneContent(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory, RunIf(TestOperatingSystems.Windows)]
    [InlineData("https://example.com/\r\nZoneId=0")]
    [InlineData("https://example.com/\nZoneId=0")]
    [InlineData("https://example.com/\0")]
    public void SetFileZone_HostUrl_RejectsControlCharacters(string hostUrl)
    {
        var path = Path.GetTempFileName();
        try
        {
            var exception = Assert.Throws<ArgumentException>(() => MarkOfTheWeb.SetFileZone(path, UrlZone.Internet, referrerUrl: null, hostUrl));
            Assert.Equal("hostUrl", exception.ParamName);
            Assert.Null(MarkOfTheWeb.GetFileZoneContent(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory, RunIf(TestOperatingSystems.Windows)]
    [InlineData(UrlZone.Invalid)]
    [InlineData((UrlZone)(-2))]
    [InlineData((UrlZone)5)]
    [InlineData((UrlZone)999)]
    public void SetFileZone_RejectsUndefinedZones(UrlZone zone)
    {
        var path = Path.GetTempFileName();
        try
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => MarkOfTheWeb.SetFileZone(path, zone));
            Assert.Equal("zone", exception.ParamName);
            Assert.Null(MarkOfTheWeb.GetFileZoneContent(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // UrlZone.Trusted is deliberately absent: Windows resolves the Trusted Sites zone from site
    // membership rather than from a saved-file mark, so a file marked ZoneId=2 reads back as
    // UrlZone.LocalMachine. SetFileZone still accepts it, since the value it writes is well-formed.
    [Theory, RunIf(TestOperatingSystems.Windows)]
    [InlineData(UrlZone.LocalMachine)]
    [InlineData(UrlZone.Intranet)]
    [InlineData(UrlZone.Internet)]
    [InlineData(UrlZone.Untrusted)]
    public void SetFileZone_RoundTripsDefinedZones(UrlZone zone)
    {
        var path = Path.GetTempFileName();
        try
        {
            MarkOfTheWeb.SetFileZone(path, zone);

            Assert.Equal(zone, MarkOfTheWeb.GetFileZone(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void SetFileZone_AcceptsTrusted_EvenThoughWindowsResolvesItToLocalMachine()
    {
        var path = Path.GetTempFileName();
        try
        {
            MarkOfTheWeb.SetFileZone(path, UrlZone.Trusted);

            Assert.Equal("[ZoneTransfer]\nZoneId=2\n", MarkOfTheWeb.GetFileZoneContent(path)!.ReplaceLineEndings("\n"));
            Assert.Equal(UrlZone.LocalMachine, MarkOfTheWeb.GetFileZone(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void SetFileZone_KeepsTheRequestedZone_WhenUrlsAreProvided()
    {
        var path = Path.GetTempFileName();
        try
        {
            MarkOfTheWeb.SetFileZone(path, UrlZone.Internet, referrerUrl: "https://example.com/page", hostUrl: "https://example.com/file.txt");

            Assert.Equal(UrlZone.Internet, MarkOfTheWeb.GetFileZone(path));
            Assert.True(MarkOfTheWeb.IsUntrusted(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void IsUntrusted_Throws_WhenTheFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".txt");

        Assert.Throws<FileNotFoundException>(() => MarkOfTheWeb.IsUntrusted(path));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void IsUntrusted_Throws_WhenThePathIsADirectory()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        try
        {
            Assert.Throws<FileNotFoundException>(() => MarkOfTheWeb.IsUntrusted(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetFileZone_ReturnsInvalid_WhenTheFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".txt");

        Assert.Equal(UrlZone.Invalid, MarkOfTheWeb.GetFileZone(path));
    }

}
