using Meziantou.Xunit;

namespace Meziantou.Framework.Win32.Tests;

public sealed class MarkOfTheWebTests
{
    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void Get()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.Equal(UrlZone.LocalMachine, MarkOfTheWeb.GetFileZone(path));
            Assert.False(MarkOfTheWeb.IsUntrusted(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void Set_Get()
    {
        var path = Path.GetTempFileName();
        try
        {
            MarkOfTheWeb.SetFileZone(path, UrlZone.Internet);

            var zoneContent = MarkOfTheWeb.GetFileZoneContent(path);
            Assert.NotNull(zoneContent);
            Assert.Equal("[ZoneTransfer]\nZoneId=3\n", zoneContent.ReplaceLineEndings("\n"));
            Assert.Equal(UrlZone.Internet, MarkOfTheWeb.GetFileZone(path));
            Assert.True(MarkOfTheWeb.IsUntrusted(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void Set_Delete()
    {
        var path = Path.GetTempFileName();
        try
        {
            MarkOfTheWeb.SetFileZone(path, UrlZone.Internet);
            var zoneContent = MarkOfTheWeb.GetFileZoneContent(path);
            Assert.NotNull(zoneContent);
            Assert.NotEmpty(zoneContent);

            MarkOfTheWeb.RemoveFileZone(path);
            Assert.Null(MarkOfTheWeb.GetFileZoneContent(path));
            Assert.Equal(UrlZone.LocalMachine, MarkOfTheWeb.GetFileZone(path));
            Assert.False(MarkOfTheWeb.IsUntrusted(path));
        }
        finally
        {
            File.Delete(path);
        }
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
    public void RemoveFileZone_DoesNothing_WhenTheFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".txt");

        MarkOfTheWeb.RemoveFileZone(path);

        Assert.False(File.Exists(path));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void RemoveFileZone_RemovesTheZoneOfAReadOnlyFile_AndKeepsItReadOnly()
    {
        var path = Path.GetTempFileName();
        try
        {
            MarkOfTheWeb.SetFileZone(path, UrlZone.Internet);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

            MarkOfTheWeb.RemoveFileZone(path);

            Assert.Null(MarkOfTheWeb.GetFileZoneContent(path));
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
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
    public void GetFileZoneIdentifier_ReturnsNull_WhenTheFileHasNoZoneInformation()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.Null(MarkOfTheWeb.GetFileZoneIdentifier(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetFileZoneIdentifier_ReadsWhatSetFileZoneWrote()
    {
        var path = Path.GetTempFileName();
        try
        {
            MarkOfTheWeb.SetFileZone(path, UrlZone.Untrusted, referrerUrl: "https://example.com/page", hostUrl: "https://example.com/file.txt");

            var zoneIdentifier = MarkOfTheWeb.GetFileZoneIdentifier(path);

            Assert.NotNull(zoneIdentifier);
            Assert.Equal(UrlZone.Untrusted, zoneIdentifier.Zone);
            Assert.Equal("https://example.com/page", zoneIdentifier.ReferrerUrl);
            Assert.Equal("https://example.com/file.txt", zoneIdentifier.HostUrl);
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

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void SetFileZone_OmitsEmptyUrls()
    {
        var path = Path.GetTempFileName();
        try
        {
            MarkOfTheWeb.SetFileZone(path, UrlZone.Internet, referrerUrl: "", hostUrl: "");

            Assert.Equal("[ZoneTransfer]\nZoneId=3\n", MarkOfTheWeb.GetFileZoneContent(path)!.ReplaceLineEndings("\n"));
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
    public void SetFileZone_Throws_WhenTheFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".txt");

        Assert.Throws<FileNotFoundException>(() => MarkOfTheWeb.SetFileZone(path, UrlZone.Internet));
        Assert.False(File.Exists(path));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void SetFileZone_Throws_WhenThePathIsADirectory()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        try
        {
            Assert.Throws<FileNotFoundException>(() => MarkOfTheWeb.SetFileZone(directory.FullName, UrlZone.Internet));
            Assert.Null(MarkOfTheWeb.GetFileZoneContent(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void SetFileZone_SetsTheZoneOfAReadOnlyFile_AndKeepsItReadOnly()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

            MarkOfTheWeb.SetFileZone(path, UrlZone.Internet);

            Assert.Equal(UrlZone.Internet, MarkOfTheWeb.GetFileZone(path));
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    // Each name decodes, as a URL, to the name of an unmarked sibling file: "%41" is an escaped 'A',
    // and '#' starts a fragment. Evaluating the decoded path would report the sibling's zone.
    [Theory, RunIf(TestOperatingSystems.Windows)]
    [InlineData("file%41.txt", "fileA.txt")]
    [InlineData("file%23.txt", "file#.txt")]
    [InlineData("file#.txt", "file")]
    public void GetFileZone_EvaluatesFileNamesThatLookLikeUrlEscapes(string markedFileName, string unmarkedFileName)
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        try
        {
            var markedPath = Path.Combine(directory.FullName, markedFileName);
            File.WriteAllBytes(markedPath, []);
            File.WriteAllBytes(Path.Combine(directory.FullName, unmarkedFileName), []);

            MarkOfTheWeb.SetFileZone(markedPath, UrlZone.Internet);

            Assert.Equal(UrlZone.Internet, MarkOfTheWeb.GetFileZone(markedPath));
            Assert.True(MarkOfTheWeb.IsUntrusted(markedPath));
        }
        finally
        {
            directory.Delete(recursive: true);
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

    [Fact]
    public void ZoneIdentifier_Parse_ReadsTheZoneTransferSection()
    {
        var zoneIdentifier = ZoneIdentifier.Parse("[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.com/page\r\nHostUrl=https://example.com/file.txt\r\n");

        Assert.Equal(UrlZone.Internet, zoneIdentifier.Zone);
        Assert.Equal("https://example.com/page", zoneIdentifier.ReferrerUrl);
        Assert.Equal("https://example.com/file.txt", zoneIdentifier.HostUrl);
    }

    [Fact]
    public void ZoneIdentifier_Parse_IgnoresCaseWhitespaceByteOrderMarkAndComments()
    {
        var zoneIdentifier = ZoneIdentifier.Parse("﻿; comment\n  [ zonetransfer ]  \n  zoneid = 4 \n\n hosturl = https://example.com/file.txt \n");

        Assert.Equal(UrlZone.Untrusted, zoneIdentifier.Zone);
        Assert.Null(zoneIdentifier.ReferrerUrl);
        Assert.Equal("https://example.com/file.txt", zoneIdentifier.HostUrl);
    }

    [Fact]
    public void ZoneIdentifier_Parse_IgnoresEntriesOutsideTheZoneTransferSection()
    {
        var zoneIdentifier = ZoneIdentifier.Parse("ZoneId=0\n[Other]\nZoneId=1\nHostUrl=https://example.com/other\n[ZoneTransfer]\nZoneId=3\n[Other]\nReferrerUrl=https://example.com/other\n");

        Assert.Equal(UrlZone.Internet, zoneIdentifier.Zone);
        Assert.Null(zoneIdentifier.ReferrerUrl);
        Assert.Null(zoneIdentifier.HostUrl);
    }

    [Fact]
    public void ZoneIdentifier_Parse_ReportsConflictingEntriesAsAbsent()
    {
        var zoneIdentifier = ZoneIdentifier.Parse("[ZoneTransfer]\nZoneId=3\nHostUrl=https://example.com/a\nZoneId=0\nHostUrl=https://example.com/b\nZoneId=3\n");

        Assert.Equal(UrlZone.Invalid, zoneIdentifier.Zone);
        Assert.Null(zoneIdentifier.HostUrl);
    }

    [Fact]
    public void ZoneIdentifier_Parse_AcceptsRepeatedIdenticalEntries()
    {
        var zoneIdentifier = ZoneIdentifier.Parse("[ZoneTransfer]\nZoneId=3\nZoneId=3\n");

        Assert.Equal(UrlZone.Internet, zoneIdentifier.Zone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[ZoneTransfer]\n")]
    [InlineData("[ZoneTransfer]\nZoneId=\n")]
    [InlineData("[ZoneTransfer]\nZoneId=Internet\n")]
    [InlineData("[ZoneTransfer\nZoneId=3\n")]
    public void ZoneIdentifier_Parse_ReturnsInvalid_WhenThereIsNoUsableZoneId(string content)
    {
        Assert.Equal(UrlZone.Invalid, ZoneIdentifier.Parse(content).Zone);
    }

    [Fact]
    public void ZoneIdentifier_Parse_KeepsCustomZones()
    {
        Assert.Equal((UrlZone)1000, ZoneIdentifier.Parse("[ZoneTransfer]\nZoneId=1000\n").Zone);
    }

    [Fact]
    public void ZoneIdentifier_Parse_ReportsEmptyUrlsAsAbsent()
    {
        var zoneIdentifier = ZoneIdentifier.Parse("[ZoneTransfer]\nZoneId=3\nReferrerUrl=\nHostUrl= \n");

        Assert.Null(zoneIdentifier.ReferrerUrl);
        Assert.Null(zoneIdentifier.HostUrl);
    }
}
