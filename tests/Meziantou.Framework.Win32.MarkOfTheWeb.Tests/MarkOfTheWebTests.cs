using TestUtilities;

namespace Meziantou.Framework.Win32.Tests;

public sealed class MarkOfTheWebTests
{
    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void Get()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, []);
        Assert.Equal(UrlZone.LocalMachine, MarkOfTheWeb.GetFileZone(path));

        Assert.False(MarkOfTheWeb.IsUntrusted(path));
        File.Delete(path);
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
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

    [Fact, RunIf(FactOperatingSystem.Windows)]
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
}
