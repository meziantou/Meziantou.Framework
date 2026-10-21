using Meziantou.Framework.JavaScriptEngine.Parsing;
using Meziantou.Framework.JavaScriptEngine.Test262.Runner;
using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine.Test262;

/// <summary>
/// The conformance of the parser: tc39/test262-parser-tests, and every test262 file parsed without running it (the
/// parse must fail exactly for the negative tests of the parse phase). The known failures are in
/// expectations/parser-tests-*.txt and expectations/parse-only-*.txt.
/// </summary>
public sealed class Test262ParserTests
{
    private static readonly string[] Test262ParseOnlyDirectories = ["annexB", "built-ins", "intl402", "language", "staging"];

    public static TheoryData<string> PassTests => GetParserTests("pass");

    public static TheoryData<string> FailTests => GetParserTests("fail");

    public static TheoryData<string> EarlyTests => GetParserTests("early");

    public static TheoryData<string> ParseOnlyLanguageTests => GetParseOnlyTests("language");

    public static TheoryData<string> ParseOnlyOtherTests
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var directory in Test262ParseOnlyDirectories.Where(d => d != "language"))
            {
                foreach (var id in GetParseOnlyTests(directory))
                {
                    data.Add(id);
                }
            }

            return data;
        }
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(PassTests), DisableDiscoveryEnumeration = true)]
    public void Pass(string file)
    {
        var path = Path.Combine(Test262Paths.ParserTests, "pass", file);
        string? error = null;
        try
        {
            var tree = AstPrinter.Print(Parse(File.ReadAllText(path), IsModule(file), strict: false));
            var explicitPath = Path.Combine(Test262Paths.ParserTests, "pass-explicit", file);
            if (File.Exists(explicitPath))
            {
                var explicitTree = AstPrinter.Print(Parse(File.ReadAllText(explicitPath), IsModule(file), strict: false));
                if (tree != explicitTree)
                {
                    error = "The tree differs from the tree of pass-explicit/" + file + "\n" + tree + "\n" + explicitTree;
                }
            }
        }
        catch (JsSyntaxErrorException ex)
        {
            error = $"SyntaxError: {ex.Message} ({ex.Line}:{ex.Column})";
        }

        CheckParserTest("pass/" + file, error);
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(FailTests), DisableDiscoveryEnumeration = true)]
    public void Fail(string file) => CheckRejected("fail", file);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(EarlyTests), DisableDiscoveryEnumeration = true)]
    public void Early(string file) => CheckRejected("early", file);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ParseOnlyLanguageTests), DisableDiscoveryEnumeration = true)]
    public void ParseOnlyLanguage(string test) => RunParseOnly(test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ParseOnlyOtherTests), DisableDiscoveryEnumeration = true)]
    public void ParseOnlyOther(string test) => RunParseOnly(test);

    private static void CheckRejected(string directory, string file)
    {
        var path = Path.Combine(Test262Paths.ParserTests, directory, file);
        string? error = null;
        try
        {
            Parse(File.ReadAllText(path), IsModule(file), strict: false);
            error = "The parser accepted the source, it must report a SyntaxError";
        }
        catch (JsSyntaxErrorException)
        {
        }

        CheckParserTest(directory + "/" + file, error);
    }

    private static void RunParseOnly(string id)
    {
        var test = Test262Discovery.Discover(id.Split('/')[0]).AllById.GetValueOrDefault(id) ?? throw new InvalidOperationException("Unknown test " + id);
        var source = File.ReadAllText(test.FullPath);
        var expectFailure = test.Metadata.NegativePhase == "parse";
        var results = new List<Test262Result>();
        foreach (var mode in test.Modes)
        {
            string? error = null;
            try
            {
                Parse(source, mode == Test262Mode.Module, mode == Test262Mode.Strict);
                if (expectFailure)
                {
                    error = $"The parser accepted the source, it must report a {test.Metadata.NegativeType}";
                }
            }
            catch (JsSyntaxErrorException ex)
            {
                if (!expectFailure)
                {
                    error = $"SyntaxError: {ex.Message} ({ex.Line}:{ex.Column})";
                }
            }

            results.Add(new Test262Result(mode, error is null, error));
        }

        Test262Expectations.Check(new Test262Test("parse-only/" + test.Id, test.FullPath, test.Metadata), results);
    }

    private static void CheckParserTest(string id, string? error)
    {
        var test = new Test262Test("parser-tests/" + id, Path.Combine(Test262Paths.ParserTests, id), Test262Metadata.Parse(""));
        Test262Expectations.Check(test, [new Test262Result(Test262Mode.Sloppy, error is null, error)]);
    }

    private static Ast.Program Parse(string source, bool module, bool strict)
    {
        var text = new SourceText(source);
        return module ? JsParser.ParseModule(text) : JsParser.ParseScript(text, strict ? new ParserOptions { IsStrict = true } : ParserOptions.Script);
    }

    private static bool IsModule(string file) => file.EndsWith(".module.js", StringComparison.Ordinal);

    private static TheoryData<string> GetParserTests(string directory)
    {
        var data = new TheoryData<string>();
        var path = Path.Combine(Test262Paths.ParserTests, directory);
        if (!Directory.Exists(path))
            return data;

        foreach (var file in Directory.EnumerateFiles(path, "*.js").Select(Path.GetFileName).Order(StringComparer.Ordinal))
        {
            if (!Test262Discovery.Skipped.IsSkipped("parser-tests/" + directory + "/" + file))
            {
                data.Add(file!);
            }
        }

        return data;
    }

    private static TheoryData<string> GetParseOnlyTests(string directory)
    {
        var data = new TheoryData<string>();
        if (!Test262Paths.IsAvailable)
            return data;

        var filters = Environment.GetEnvironmentVariable("TEST262_FILTER") is { Length: > 0 } filter ? filter.Split('|', StringSplitOptions.RemoveEmptyEntries) : null;
        foreach (var test in Test262Discovery.Discover(directory).All)
        {
            if (Test262Discovery.IsExcludedByFeature(test) || Test262Discovery.Skipped.IsSkipped("parse-only/" + test.Id))
                continue;

            if (filters is not null && !Array.Exists(filters, value => test.Id.Contains(value, StringComparison.Ordinal)))
                continue;

            data.Add(test.Id);
        }

        return data;
    }
}
