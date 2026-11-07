using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.JavaScriptEngine.WebToolingBenchmark;

/// <summary>
/// Runs the workloads of the Web Tooling Benchmark (harness.js) and compares their results with the results of Node.js
/// (expected.json, written by generate.js). See readme.md.
/// </summary>
public sealed class WebToolingBenchmarkTests
{
    private static readonly string Root = FullPath.FromPath(AppContext.BaseDirectory).FindRequiredGitRepositoryRoot();

    private static readonly string ProjectDirectory = Path.Combine(Root, "tests", "Meziantou.Framework.JavaScriptEngine.WebToolingBenchmark");

    /// <summary>The benchmark and its npm packages. Run 'dotnet run eng/javascriptengine/fetch-tests.cs' to download them.</summary>
    private static readonly string BenchmarkDirectory = Path.Combine(Root, "tests", "Meziantou.Framework.JavaScriptEngine.External", "web-tooling-benchmark");

    public static TheoryData<string> Workloads
    {
        get
        {
            var data = new TheoryData<string>();
            if (!Directory.Exists(Path.Combine(BenchmarkDirectory, "node_modules")))
                return data;

            foreach (var workload in ReadExpectedResults().Keys.Order(StringComparer.Ordinal))
            {
                data.Add(workload);
            }

            return data;
        }
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Workloads), DisableDiscoveryEnumeration = true)]
    public void Run(string workload)
    {
        var expected = ReadExpectedResults()[workload];
        var result = RunWorkload(workload);

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.Unicode.GetBytes(result)));
        if (result.Length != expected.Length || sha256 != expected.Sha256)
        {
            var resultPath = Path.Combine(Path.GetTempPath(), "web-tooling-benchmark", workload + ".txt");
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
            File.WriteAllText(resultPath, result);
            Assert.Fail($"The result of '{workload}' differs from the result of Node.js ({result.Length} characters instead of {expected.Length}). " +
                $"The result is in '{resultPath}'; 'node generate.js --dump <directory> {workload}' writes the result of Node.js.");
        }
    }

    private static string RunWorkload(string workload)
    {
        using var engine = new JsEngine(new JsEngineOptions
        {
            Timeout = TimeSpan.FromMinutes(5),
        });
        var realm = engine.CreateRealm();
        var harness = realm.Execute(File.ReadAllText(Path.Combine(ProjectDirectory, "harness.js")), "harness.js");

        JsValue readFile;
        using (engine.Enter())
        {
            readFile = realm.CreateFunction("readFile", 1, (thisValue, arguments) =>
            {
                var path = Path.Combine(BenchmarkDirectory, arguments[0].AsString().TrimStart('/'));
                return File.Exists(path) ? File.ReadAllText(path) : JsValue.Undefined;
            });
        }

        var workloads = engine.Call(harness, JsValue.Undefined, readFile);
        JsValue run;
        using (engine.Enter())
        {
            run = workloads.AsObject().GetProperty("run");
        }

        var result = engine.Call(run, workloads, workload);
        using (engine.Enter())
        {
            return result.AsString();
        }
    }

    private static Dictionary<string, ExpectedResult> ReadExpectedResults()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectDirectory, "expected.json")));
        var result = new Dictionary<string, ExpectedResult>(StringComparer.Ordinal);
        foreach (var workload in document.RootElement.GetProperty("workloads").EnumerateObject())
        {
            result.Add(workload.Name, new ExpectedResult(workload.Value.GetProperty("length").GetInt32(), workload.Value.GetProperty("sha256").GetString()!));
        }

        return result;
    }

    private sealed record ExpectedResult(int Length, string Sha256);
}
