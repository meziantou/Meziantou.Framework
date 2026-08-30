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

    [Theory]
    [InlineData(".txt", 0, true)]
    [InlineData(".appxmanifest", 4095, true)]
    [InlineData(".txt", 4096, false)]
    [InlineData(".txt", 5000, false)]
    [InlineData(".this_extension_is_far_too_long", 0, false)]
    public void ShouldCache_LimitsLengthAndCount(string extension, int currentCount, bool expected)
    {
        Assert.Equal(expected, Perceived.ShouldCache(extension, currentCount));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetPerceivedType_DoesNotCacheAnAbsurdlyLongExtension()
    {
        var extension = "." + new string('a', 512);

        var perceived = Perceived.GetPerceivedType("file" + extension);

        Assert.Equal(PerceivedType.Unspecified, perceived.PerceivedType);
        Assert.False(Perceived.IsExtensionCached(extension));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetPerceivedType_CachesAnOrdinaryExtension()
    {
        Perceived.GetPerceivedType(".png");

        Assert.True(Perceived.IsExtensionCached(".png"));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetPerceivedType_PreservesTheExtensionAsWritten()
    {
        var perceived = Perceived.GetPerceivedType("report.CasingSample");

        Assert.Equal(".CasingSample", perceived.Extension);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetPerceivedType_IsCaseInsensitiveAndReturnsTheSameInstance()
    {
        var first = Perceived.GetPerceivedType("a.InstanceSample");
        var second = Perceived.GetPerceivedType("b.INSTANCESAMPLE");

        Assert.Same(first, second);
    }

    [Theory, RunIf(TestOperatingSystems.Windows)]
    [InlineData("readme")]
    [InlineData("")]
    public void GetPerceivedType_ReturnsUnspecifiedWhenThereIsNoExtension(string fileName)
    {
        var perceived = Perceived.GetPerceivedType(fileName);

        Assert.Equal(PerceivedType.Unspecified, perceived.PerceivedType);
        Assert.Empty(perceived.Extension);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetPerceivedType_AcceptsAFullPath()
    {
        var perceived = Perceived.GetPerceivedType(@"C:\dir.d\sub\file.txt");

        Assert.Equal(PerceivedType.Text, perceived.PerceivedType);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetPerceivedType_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Perceived.GetPerceivedType(fileName: null!));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void AddDefaultPerceivedTypes_TreatsCompiledJavaClassesAsApplications()
    {
        Perceived.AddDefaultPerceivedTypes();

        Assert.Equal(PerceivedType.Application, Perceived.GetPerceivedType("Program.class").PerceivedType);
    }
}
