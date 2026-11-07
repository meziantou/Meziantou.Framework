namespace Meziantou.Framework.Language.Regex.Tests;

/// <summary>Runs the whole corpus through the parser, on every target framework.</summary>
/// <remarks>
/// The corpus exists for the differential test, which only runs where the runtime matches the engine the scanner was
/// ported from. Round-tripping and never throwing are properties of the parser alone, so they are checked everywhere.
/// </remarks>
public sealed class RegexCorpusTests
{
    public static TheoryData<string> CorpusNames => ["NetValid.txt", "NetInvalid.txt"];

    [Theory]
    [MemberData(nameof(CorpusNames))]
    public void EveryPatternRoundTripsExactly(string name)
    {
        var count = 0;
        foreach (var pattern in RegexCorpus.Read(name))
        {
            RegexSyntaxAssert.TextIsFaithful(pattern, RegexDialect.Net);
            count++;
        }

        Assert.NotEqual(0, count);
    }

    [Theory]
    [MemberData(nameof(CorpusNames))]
    public void EveryPrefixOfEveryPatternRoundTripsExactly(string name)
    {
        foreach (var pattern in RegexCorpus.Read(name))
        {
            for (var length = 0; length <= pattern.Length; length++)
            {
                RegexSyntaxAssert.TextIsFaithful(pattern[..length], RegexDialect.Net);
            }
        }
    }

    [Fact]
    public void ValidPatternsProduceNoDiagnostics()
    {
        foreach (var pattern in RegexCorpus.Read("NetValid.txt"))
        {
            var tree = RegexSyntaxTree.ParseText(pattern, RegexDialect.Net);

            Assert.Empty(tree.GetDiagnostics(), $"[{pattern}] should parse cleanly");
        }
    }

    [Fact]
    public void InvalidPatternsProduceDiagnostics()
    {
        foreach (var pattern in RegexCorpus.Read("NetInvalid.txt"))
        {
            var tree = RegexSyntaxTree.ParseText(pattern, RegexDialect.Net);

            Assert.NotEmpty(tree.GetDiagnostics(), $"[{pattern}] should report at least one diagnostic");
        }
    }

    [Theory]
    [MemberData(nameof(CorpusNames))]
    public void EveryPatternIsParsedByEveryDialectWithoutThrowing(string name)
    {
        RegexDialect[] dialects = [RegexDialect.Net, RegexDialect.JavaScript, RegexDialect.PcrePerl, RegexDialect.PosixExtended, RegexDialect.PosixBasic];

        foreach (var pattern in RegexCorpus.Read(name))
        {
            foreach (var dialect in dialects)
            {
                RegexSyntaxAssert.TextIsFaithful(pattern, dialect);
            }
        }
    }
}
