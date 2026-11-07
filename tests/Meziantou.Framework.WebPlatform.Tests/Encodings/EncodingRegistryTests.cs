using System.Text.Json;
using Meziantou.Framework.WebPlatform.Encodings;

namespace Meziantou.Framework.WebPlatform.Tests.Encodings;

public sealed class EncodingRegistryTests
{
    public static TheoryData<string, string> Labels()
    {
        var data = new TheoryData<string, string>();
        using var document = JsonDocument.Parse(File.ReadAllText(TestPaths.SpecData("encoding", "encodings.json")));
        foreach (var heading in document.RootElement.EnumerateArray())
        {
            foreach (var encoding in heading.GetProperty("encodings").EnumerateArray())
            {
                foreach (var label in encoding.GetProperty("labels").EnumerateArray())
                {
                    data.Add(label.GetString()!, encoding.GetProperty("name").GetString()!);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Labels))]
    public void GetEncoding_Label(string label, string expectedName)
    {
        Assert.True(WebEncodingRegistry.TryGetEncoding(label, out var encoding));
        Assert.Equal(expectedName, WebEncodingRegistry.GetName(encoding));

        // Labels are matched ASCII case-insensitively after stripping ASCII whitespace
        Assert.True(WebEncodingRegistry.TryGetEncoding(" \t\n\f\r" + label.ToUpperInvariant() + " \t\n\f\r", out encoding));
        Assert.Equal(expectedName, WebEncodingRegistry.GetName(encoding));
    }

    [Theory]
    [InlineData("")]
    [InlineData("utf8\u000B")] // vertical tab is not ASCII whitespace
    [InlineData("\U000000A0utf-8")] // NBSP is not ASCII whitespace
    [InlineData("utf-7")]
    [InlineData("unicode-1-1-utf-7")]
    [InlineData("\U0000212Aoi8-r")] // KELVIN SIGN must not match 'k'
    [InlineData("iso-8859-12")]
    [InlineData("utf-32")]
    public void GetEncoding_InvalidLabel(string label)
    {
        Assert.False(WebEncodingRegistry.TryGetEncoding(label, out _));
    }

    [Fact]
    public void SniffBom()
    {
        Assert.Equal(3, WebEncodingRegistry.SniffBom([0xEF, 0xBB, 0xBF, 0x41], out var encoding));
        Assert.Equal(WebEncodingId.Utf8, encoding);
        Assert.Equal(2, WebEncodingRegistry.SniffBom([0xFE, 0xFF], out encoding));
        Assert.Equal(WebEncodingId.Utf16BE, encoding);
        Assert.Equal(2, WebEncodingRegistry.SniffBom([0xFF, 0xFE, 0x00], out encoding));
        Assert.Equal(WebEncodingId.Utf16LE, encoding);
        Assert.Equal(0, WebEncodingRegistry.SniffBom([0xEF, 0xBB], out _));
    }
}
