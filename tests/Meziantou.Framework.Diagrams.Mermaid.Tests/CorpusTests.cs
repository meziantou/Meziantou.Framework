namespace Meziantou.Framework.Diagrams.Mermaid.Tests;

public sealed class CorpusTests
{
    public static TheoryData<string> Examples()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Corpus"), "*.mmd", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "Corpus"), file).Replace('\\', '/'));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void ExamplesOfTheDocumentation(string name)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Corpus", name));
        var document = MermaidDocument.Parse(text);

        // Written back, the example reads to the same diagram
        var written = MermaidWriter.Write(document.Diagram);
        Assert.Equal(written, MermaidWriter.Write(MermaidDocument.Parse(written).Diagram));

        var svg = document.Diagram.ToSvg();
        Assert.StartsWith("<svg", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
    }
}
