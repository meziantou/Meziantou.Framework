using System;
using Tomlyn.Parsing;

namespace Tomlyn.Tests;

public sealed class AllocationFreeParsingTests
{
    [Fact]
    public void TomlLexer_MoveNext_NoAllocations()
    {
        var toml = "# comment\n" +
                   "a = 1\n" +
                   "b = true\n" +
                   "arr = [1, 2, 3]\n" +
                   "inline = {\n" +
                   "  c = 1,\n" +
                   "  d = 2,\n" +
                   "}\n";

        var lexerOptions = new TomlLexerOptions { DecodeScalars = false };

        WarmUpLexer(toml, lexerOptions);

        var lexer = TomlLexer.Create(toml, lexerOptions, "alloc.toml");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0;
        while (lexer.MoveNext())
        {
            var token = lexer.Current;
            checksum = unchecked(checksum + (int)token.Kind + token.Start.Offset + token.End.Offset);
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.NotEqual(0, checksum, message: "Sanity check: token loop should execute.");
        Assert.Equal(0, after - before, message: "Lexer iteration should not allocate.");
    }

    [Fact]
    public void TomlParser_MoveNext_NoAllocations()
    {
        var toml = "a = 1\n" +
                   "b = \"hello\"\n" +
                   "arr = [1, 2, 3]\n" +
                   "inline = {\n" +
                   "  c = 1,\n" +
                   "  d = 2,\n" +
                   "}\n";

        var parserOptions = new Tomlyn.Parsing.TomlParserOptions
        {
            DecodeScalars = false,
            Mode = TomlParserMode.Strict,
        };

        WarmUpParser(toml, parserOptions);

        var parser = TomlParser.Create(toml, parserOptions);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0;
        while (parser.MoveNext())
        {
            var evt = parser.Current;
            var span = evt.Span;
            checksum = unchecked(checksum + (int)evt.Kind + (span?.Start.Offset ?? 0) + (span?.End.Offset ?? 0));
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.NotEqual(0, checksum, message: "Sanity check: event loop should execute.");
        Assert.Equal(0, after - before, message: "Parser iteration should not allocate.");
    }

    private static void WarmUpLexer(string toml, TomlLexerOptions lexerOptions)
    {
        var lexer = TomlLexer.Create(toml, lexerOptions, "warmup.toml");
        while (lexer.MoveNext())
        {
        }
    }

    private static void WarmUpParser(string toml, Tomlyn.Parsing.TomlParserOptions parserOptions)
    {
        var parser = TomlParser.Create(toml, parserOptions);
        while (parser.MoveNext())
        {
        }
    }
}
