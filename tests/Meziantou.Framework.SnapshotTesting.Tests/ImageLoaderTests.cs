namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class ImageLoaderTests
{
    [Fact]
    public async Task Image_LoadAsync_ThrowsWhenFormatIsNotSupported()
    {
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => Image.LoadAsync(new MemoryStream("not-a-bmp"u8.ToArray())));
        Assert.Contains("Only BMP, PNG, and JPEG are currently supported.", ex.Message, StringComparison.Ordinal);
    }
}
