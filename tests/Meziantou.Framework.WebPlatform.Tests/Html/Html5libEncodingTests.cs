using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Encodings;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>html5lib-tests/encoding: character encoding sniffing.</summary>
public sealed class Html5libEncodingTests
{
    public static TheoryData<string, int> Tests()
    {
        var data = new TheoryData<string, int>();
        foreach (var file in Directory.GetFiles(TestPaths.External("html5lib-tests", "encoding"), "*.dat").Order(StringComparer.Ordinal))
        {
            var count = Load(file).Count;
            for (var i = 0; i < count; i++)
            {
                data.Add(Path.GetFileName(file), i);
            }
        }

        return data;
    }

    private static List<(byte[] Data, string Encoding)> Load(string path)
    {
        // Latin-1 preserves the bytes of the file
        var content = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        var result = new List<(byte[], string)>();
        foreach (var block in content.Split("#data\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var encodingIndex = block.IndexOf("#encoding\n", StringComparison.Ordinal);
            var data = block[..encodingIndex];
            if (data.EndsWith('\n', StringComparison.Ordinal))
            {
                data = data[..^1];
            }

            var encoding = block[(encodingIndex + "#encoding\n".Length)..].Split('\n')[0].Trim();
            result.Add((Encoding.Latin1.GetBytes(data), encoding));
        }

        return result;
    }

    [Theory]
    [MemberData(nameof(Tests))]
    public void Sniff(string file, int index)
    {
        var (data, expectedLabel) = Load(TestPaths.External("html5lib-tests", "encoding", file))[index];
        Assert.True(WebEncodingRegistry.TryGetEncoding(expectedLabel, out var expected), expectedLabel);

        // The prescan tokenizes the input, as Blink and WebKit do and as proposed by https://github.com/whatwg/html/issues/6962
        // (tested by WPT html/syntax/charset): the meta elements in raw text elements and the ones inserted by document.write()
        // do not declare the encoding
        var text = Encoding.Latin1.GetString(data);
        Assert.SkipWhen(Regex.IsMatch(text, @"<(script|style)[^>]*>(document\.write\(')?<meta charset"),
            "The HTML Standard prescans the bytes, the browsers tokenize them (whatwg/html#6962)");

        var document = new HtmlDocumentParser(new HtmlParserOptions { DefaultEncoding = "windows-1252" }).Parse(data);
        Assert.Equal(WebEncodingRegistry.GetName(expected), document.CharacterSet);
    }
}
