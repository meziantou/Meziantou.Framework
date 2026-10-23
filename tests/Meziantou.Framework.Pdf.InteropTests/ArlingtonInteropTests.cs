using Meziantou.Framework.Pdf.TestHarness.ExternalTools;

namespace Meziantou.Framework.Pdf.InteropTests;

/// <summary>TestGrammar, the checker of the Arlington PDF Model, finds no error in the files the library writes.</summary>
public sealed class ArlingtonInteropTests
{
    public static TheoryData<string, bool> Written => InteropDocuments.Written;

    public static TheoryData<string> ValidFixtures => InteropDocuments.ValidFixtures;

    [Theory]
    [MemberData(nameof(Written))]
    public async Task WrittenDocumentsHaveNoError(string name, bool objectStreams)
    {
        var tool = await ArlingtonTestGrammarTool.LocateAsync(XunitCancellationToken);
        var path = InteropDocuments.Write(name, objectStreams);

        var errors = await tool.CheckAsync(path, XunitCancellationToken);

        Assert.Empty(errors);
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public async Task FixturesHaveNoError(string fixture)
    {
        var tool = await ArlingtonTestGrammarTool.LocateAsync(XunitCancellationToken);

        var errors = await tool.CheckAsync(InteropDocuments.GetFixturePath(fixture), XunitCancellationToken);

        Assert.Empty(errors);
    }
}
