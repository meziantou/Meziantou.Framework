namespace Meziantou.Framework.InlineSnapshotTesting.Samples;

public sealed class ConfigureCSharpStringFormat
{
    [Fact]
    public void Quoted()
    {
        var settings = InlineSnapshotSettings.Default with
        {
            AllowedStringFormats = CSharpStringFormats.Quoted,
        };

        InlineSnapshot.WithSettings(settings).Validate("a\nb", "a\r\nb");
    }

    [Fact]
    public void Verbatim()
    {
        var settings = InlineSnapshotSettings.Default with
        {
            AllowedStringFormats = CSharpStringFormats.Verbatim,
        };

        InlineSnapshot.WithSettings(settings).Validate("a\nb", @"a
b");
    }

    [Fact]
    public void Raw()
    {
        var settings = InlineSnapshotSettings.Default with
        {
            AllowedStringFormats = CSharpStringFormats.Raw,
        };

        InlineSnapshot.WithSettings(settings).Validate("a\nb", """
            a
            b
            """);
    }

    [Fact]
    public void LeftAlignedRaw()
    {
        var settings = InlineSnapshotSettings.Default with
        {
            AllowedStringFormats = CSharpStringFormats.LeftAlignedRaw,
        };

        InlineSnapshot.WithSettings(settings).Validate("a\nb", """
a
b
""");
    }
}