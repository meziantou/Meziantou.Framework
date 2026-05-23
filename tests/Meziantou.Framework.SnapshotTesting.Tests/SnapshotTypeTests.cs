namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class SnapshotTypeTests
{
    [Fact]
    public void Create_NullOrEmpty_ReturnsDefault()
    {
        Assert.Equal(SnapshotType.Default, SnapshotType.Create(null));
        Assert.Equal(SnapshotType.Default, SnapshotType.Create(""));
    }

    [Fact]
    public void Create_KnownType_ReturnsWellKnownInstance()
    {
        Assert.Equal(SnapshotType.Default, SnapshotType.Create("txt"));
        Assert.Equal(SnapshotType.Png, SnapshotType.Create("png"));
        Assert.Equal(SnapshotType.Jpeg, SnapshotType.Create("jpeg"));
        Assert.Equal(SnapshotType.Jpeg, SnapshotType.Create("jpg"));
        Assert.Equal(SnapshotType.Gif, SnapshotType.Create("gif"));
        Assert.Equal(SnapshotType.Ico, SnapshotType.Create("ico"));
    }

    [Fact]
    public void Create_KnownType_IsCaseInsensitive()
    {
        Assert.Equal(SnapshotType.Default, SnapshotType.Create("TXT"));
        Assert.Equal(SnapshotType.Png, SnapshotType.Create("PNG"));
        Assert.Equal(SnapshotType.Jpeg, SnapshotType.Create("JPEG"));
        Assert.Equal(SnapshotType.Jpeg, SnapshotType.Create("JPG"));
        Assert.Equal(SnapshotType.Gif, SnapshotType.Create("GIF"));
        Assert.Equal(SnapshotType.Ico, SnapshotType.Create("ICO"));
    }

    [Fact]
    public void Create_WithLeadingDot_StripsIt()
    {
        Assert.Equal(SnapshotType.Default, SnapshotType.Create(".txt"));
        Assert.Equal(SnapshotType.Png, SnapshotType.Create(".png"));
        Assert.Equal(SnapshotType.Jpeg, SnapshotType.Create(".jpeg"));
        Assert.Equal(SnapshotType.Jpeg, SnapshotType.Create(".jpg"));
        Assert.Equal(SnapshotType.Gif, SnapshotType.Create(".gif"));
        Assert.Equal(SnapshotType.Ico, SnapshotType.Create(".ico"));
    }

    [Fact]
    public void Create_UnknownType_ReturnsNewInstance()
    {
        var result = SnapshotType.Create("json");
        Assert.Equal("json", result.Type);
        Assert.Null(result.MimeType);
        Assert.Null(result.DisplayName);
        Assert.Equal(".json", result.FileExtension);
    }

    [Fact]
    public void Create_UnknownTypeWithLeadingDot_StripsItAndReturnsNewInstance()
    {
        var result = SnapshotType.Create(".json");
        Assert.Equal("json", result.Type);
    }

    [Fact]
    public void Create_WithMetadata_SetsAllProperties()
    {
        var result = SnapshotType.Create("json", "application/json", "JSON");
        Assert.Equal("json", result.Type);
        Assert.Equal("application/json", result.MimeType);
        Assert.Equal("JSON", result.DisplayName);
        Assert.Equal(".json", result.FileExtension);
    }
}
