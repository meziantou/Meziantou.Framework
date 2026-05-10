using Meziantou.Xunit;

namespace Meziantou.Framework.Win32.Tests;

public class PerceivedTests
{
    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetPerceivedType_txt()
    {
        var perceived = Perceived.GetPerceivedType(".txt");
        Assert.Equal(PerceivedType.Text, perceived.PerceivedType);
    }

    [Theory, RunIf(TestOperatingSystems.Windows)]
    [InlineData(".avi")]
    [InlineData(".mpeg")]
    [InlineData(".mp4")]
    public void GetPerceivedType_Video(string extension)
    {
        var perceived = Perceived.GetPerceivedType(extension);
        Assert.Equal(PerceivedType.Video, perceived.PerceivedType);
    }

    [Theory, RunIf(TestOperatingSystems.Windows)]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".tiff")]
    public void GetPerceivedType_Image(string extension)
    {
        var perceived = Perceived.GetPerceivedType(extension);
        Assert.Equal(PerceivedType.Image, perceived.PerceivedType);
    }

    [Theory, RunIf(TestOperatingSystems.Windows)]
    [InlineData(".mp3")]
    public void GetPerceivedType_Audio(string extension)
    {
        var perceived = Perceived.GetPerceivedType(extension);
        Assert.Equal(PerceivedType.Audio, perceived.PerceivedType);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetPerceivedType_Unspecified()
    {
        var perceived = Perceived.GetPerceivedType(".unknown_extension");
        Assert.Equal(PerceivedType.Unspecified, perceived.PerceivedType);
    }
}
