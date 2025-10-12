using Meziantou.Framework.Win32;
using TestUtilities;
using Xunit;

public sealed class MarkOfTheWebTests
{
    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void Set_Get()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, []);
        //Assert.Equal(UrlZone.LocalMachine, MarkOfTheWeb.GetFileZone(path));

        MarkOfTheWeb.SetFileZone(path, UrlZone.Internet);
        var actualContent = MarkOfTheWeb.GetFileZoneContent(path);
        Assert.Equal("[ZoneTransfer]\nZoneId=3\n", actualContent.ReplaceLineEndings("\n"));

        Assert.Equal(UrlZone.Internet, MarkOfTheWeb.GetFileZone(path));
    }
}