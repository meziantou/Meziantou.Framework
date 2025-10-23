using TestUtilities;

namespace Meziantou.Framework.Win32.Tests;

public class PerceivedTests
{
    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void GetPerceivedType_txt()
    {
        var perceived = Perceived.GetPerceivedType(".txt");
        Assert.Equal(PerceivedType.Text, perceived.PerceivedType);
    }

    [Theory, RunIf(FactOperatingSystem.Windows)]
    [InlineData(".avi")]
    [InlineData(".mpeg")]
    [InlineData(".mp4")]
    public void GetPerceivedType_Video(string extension)
    {
        var perceived = Perceived.GetPerceivedType(extension);
        Assert.Equal(PerceivedType.Video, perceived.PerceivedType);
    }

    [Theory, RunIf(FactOperatingSystem.Windows)]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".tiff")]
    public void GetPerceivedType_Image(string extension)
    {
        var perceived = Perceived.GetPerceivedType(extension);
        Assert.Equal(PerceivedType.Image, perceived.PerceivedType);
    }

    [Theory, RunIf(FactOperatingSystem.Windows)]
    [InlineData(".mp3")]
    public void GetPerceivedType_Audio(string extension)
    {
        var perceived = Perceived.GetPerceivedType(extension);
        Assert.Equal(PerceivedType.Audio, perceived.PerceivedType);
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void GetPerceivedType_Unspecified()
    {
        var perceived = Perceived.GetPerceivedType(".unknown_extension");
        Assert.Equal(PerceivedType.Unspecified, perceived.PerceivedType);
    }
}
