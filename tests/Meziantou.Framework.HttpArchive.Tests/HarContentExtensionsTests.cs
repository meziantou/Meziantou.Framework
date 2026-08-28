namespace Meziantou.Framework.HttpArchive.Tests;

public sealed class HarContentExtensionsTests
{
    [Fact]
    public void TryGetRawData_PlainText()
    {
        var content = new HarContent { MimeType = "text/plain", Text = "hello" };

        Assert.True(content.TryGetRawData(out var rawData));
        Assert.Equal("hello"u8.ToArray(), rawData);
    }

    [Fact]
    public void TryGetRawData_Base64()
    {
        var binaryData = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var content = new HarContent { MimeType = "image/png", Text = Convert.ToBase64String(binaryData), Encoding = "base64" };

        Assert.True(content.TryGetRawData(out var rawData));
        Assert.Equal(binaryData, rawData);
    }

    [Fact]
    public void TryGetRawData_InvalidBase64()
    {
        var content = new HarContent { MimeType = "image/png", Text = "not!valid!base64", Encoding = "base64" };

        Assert.False(content.TryGetRawData(out var rawData));
        Assert.Null(rawData);
    }

    [Fact]
    public void TryGetRawData_UnknownEncoding()
    {
        var content = new HarContent { MimeType = "text/plain", Text = "48656c6c6f", Encoding = "hex" };

        Assert.False(content.TryGetRawData(out var rawData));
        Assert.Null(rawData);
    }

    [Fact]
    public void TryGetRawData_NoText()
    {
        var content = new HarContent { MimeType = "" };

        Assert.False(content.TryGetRawData(out var rawData));
        Assert.Null(rawData);
    }
}
