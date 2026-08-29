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
}
