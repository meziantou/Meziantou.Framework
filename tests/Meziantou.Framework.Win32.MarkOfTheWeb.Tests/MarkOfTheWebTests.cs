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
        File.Delete(path);
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void Set_Get()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, []);

        MarkOfTheWeb.SetFileZone(path, UrlZone.Internet);

        Assert.Equal("[ZoneTransfer]\nZoneId=3\n", MarkOfTheWeb.GetFileZoneContent(path).ReplaceLineEndings("\n"));
        Assert.Equal(UrlZone.Internet, MarkOfTheWeb.GetFileZone(path));

        File.Delete(path);
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void Set_Delete()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, []);

        MarkOfTheWeb.SetFileZone(path, UrlZone.Internet);
        Assert.NotEmpty(MarkOfTheWeb.GetFileZoneContent(path));

        MarkOfTheWeb.RemoveFileZone(path);
        Assert.Null(MarkOfTheWeb.GetFileZoneContent(path));
        Assert.Equal(UrlZone.LocalMachine, MarkOfTheWeb.GetFileZone(path));

        File.Delete(path);
    }
}