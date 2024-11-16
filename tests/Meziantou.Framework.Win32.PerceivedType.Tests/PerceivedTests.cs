using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Win32.Tests;

public class PerceivedTests
{
    [RunIfFact(FactOperatingSystem.Windows)]
    public void GetPerceivedType_txt()
    {
        var perceived = Perceived.GetPerceivedType(".txt");
        perceived.PerceivedType.Should().Be(PerceivedType.Text);
    }

    [RunIfTheory(FactOperatingSystem.Windows)]
    [InlineData(".avi")]
    [InlineData(".mpeg")]
    [InlineData(".mp4")]
    public void GetPerceivedType_Video(string extension)
    {
        var perceived = Perceived.GetPerceivedType(extension);
        perceived.PerceivedType.Should().Be(PerceivedType.Video);
    }

    [RunIfTheory(FactOperatingSystem.Windows)]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".tiff")]
    public void GetPerceivedType_Image(string extension)
    {
        var perceived = Perceived.GetPerceivedType(extension);
        perceived.PerceivedType.Should().Be(PerceivedType.Image);
    }

    [RunIfTheory(FactOperatingSystem.Windows)]
    [InlineData(".mp3")]
    public void GetPerceivedType_Audio(string extension)
    {
        var perceived = Perceived.GetPerceivedType(extension);
        perceived.PerceivedType.Should().Be(PerceivedType.Audio);
    }

    [RunIfFact(FactOperatingSystem.Windows)]
    public void GetPerceivedType_Unspecified()
    {
        var perceived = Perceived.GetPerceivedType(".unknown_extension");
        perceived.PerceivedType.Should().Be(PerceivedType.Unspecified);
    }
}
