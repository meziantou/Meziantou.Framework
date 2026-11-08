using System.Globalization;
using System.Text.RegularExpressions;
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

        Assert.DoesNotContain(errors, error => !IsRadioButtonTypedAsPushButton(error));
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public async Task FixturesHaveNoError(string fixture)
    {
        var tool = await ArlingtonTestGrammarTool.LocateAsync(XunitCancellationToken);

        var errors = await tool.CheckAsync(InteropDocuments.GetFixturePath(fixture), XunitCancellationToken);

        Assert.Empty(errors);
    }

    /// <summary>
    /// TestGrammar types a field of buttons that is not a widget as the first candidate of its links, a push button
    /// (<c>FieldBtnPush</c>), and reports the flags of a group of radio buttons, even a minimal one written by hand. The
    /// unit tests check these fields with the Arlington model (<c>PdfArlingtonTests</c>), which chooses the type from the flags.
    /// </summary>
    private static bool IsRadioButtonTypedAsPushButton(string error)
    {
        var match = Regex.Match(error, @"^Error: special case not correct: Ff \(FieldBtnPush\) .* and is integer==(?<value>[0-9]+)$", RegexOptions.None, TimeSpan.FromSeconds(1));
        const int RadioFlag = 1 << 15;
        return match.Success && (int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture) & RadioFlag) != 0;
    }
}
