using System;
using System.Text;
using Meziantou.Framework.Toml.Parsing;

namespace Meziantou.Framework.Toml.Tests;

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
        var checksum = 0;

        var allocated = GetMinimumAllocatedBytes(
            () => TomlLexer.Create(toml, lexerOptions, "alloc.toml"),
            lexer =>
            {
                checksum = 0;
                while (lexer.MoveNext())
                {
                    var token = lexer.Current;
                    checksum = unchecked(checksum + (int)token.Kind + token.Start.Offset + token.End.Offset);
                }
            });

        Assert.NotEqual(0, checksum, message: "Sanity check: token loop should execute.");
        Assert.Equal(0, allocated, message: "Lexer iteration should not allocate.");
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

        var parserOptions = new Meziantou.Framework.Toml.Parsing.TomlParserOptions
        {
            DecodeScalars = false,
            Mode = TomlParserMode.Strict,
        };

        var checksum = 0;

        var allocated = GetMinimumAllocatedBytes(
            () => TomlParser.Create(toml, parserOptions),
            parser =>
            {
                checksum = 0;
                while (parser.MoveNext())
                {
                    var evt = parser.Current;
                    var span = evt.Span;
                    checksum = unchecked(checksum + (int)evt.Kind + (span?.Start.Offset ?? 0) + (span?.End.Offset ?? 0));
                }
            });

        Assert.NotEqual(0, checksum, message: "Sanity check: event loop should execute.");
        Assert.Equal(0, allocated, message: "Parser iteration should not allocate.");
    }

    // The parser tracks every key to reject duplicates. Its buffers are rented, and returned at the end of the document.
    [Fact]
    public void TomlParser_MoveNext_ManyKeys_ReusesItsBuffers()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 2000; i++)
        {
            builder.Append('k').Append(i).Append(" = ").Append(i).Append('\n');
        }

        var toml = builder.ToString();
        var parserOptions = new TomlParserOptions
        {
            DecodeScalars = false,
            Mode = TomlParserMode.Strict,
        };

        var count = 0;

        var allocated = GetMinimumAllocatedBytes(
            () => TomlParser.Create(toml, parserOptions),
            parser =>
            {
                count = 0;
                while (parser.MoveNext())
                {
                    count++;
                }
            });

        Assert.True(count > 4000, message: "Sanity check: event loop should execute.");
        Assert.Equal(0, allocated, message: "Parser iteration should not allocate.");
    }

    // The first iteration warms up the code and the buffers the parser rents from the shared ArrayPool. A gen2 GC caused by a
    // test running in parallel can trim that pool before a measured iteration, which then rents new buffers: the smallest of
    // a few measurements is the allocation of the iteration itself.
    private static long GetMinimumAllocatedBytes<T>(Func<T> create, Action<T> iterate)
    {
        iterate(create());

        var minimum = long.MaxValue;
        for (var attempt = 0; attempt < 5 && minimum > 0; attempt++)
        {
            var instance = create();
            var before = GC.GetAllocatedBytesForCurrentThread();
            iterate(instance);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return minimum;
    }
}
