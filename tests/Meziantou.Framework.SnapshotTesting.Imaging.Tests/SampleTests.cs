using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.SnapshotTesting.Imaging.Tests;

// The example of the readme: validates with SnapshotSettings.Default, set up by SnapshotConfiguration
#region SnapshotTestingImaging_Validate
public sealed class SampleTests
{
    [Fact]
    public void ValidateImage()
    {
        using var image = Image.Load("sample.png");
        Snapshot.Validate(image, SnapshotType.Png);
    }

    [Fact]
    public void ValidateImageBytes()
    {
        var bytes = File.ReadAllBytes("sample.jpg");
        Snapshot.Validate(bytes, SnapshotType.Jpeg);
    }
}
#endregion
