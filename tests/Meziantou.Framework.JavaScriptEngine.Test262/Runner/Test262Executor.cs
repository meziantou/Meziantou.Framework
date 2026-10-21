using System.Collections.Concurrent;
using System.Text;

namespace Meziantou.Framework.JavaScriptEngine.Test262.Runner;

/// <summary>Runs a test262 file with the engine, in each of its modes (https://github.com/tc39/test262/blob/main/INTERPRETING.md).</summary>
internal static class Test262Executor
{
    private static readonly ConcurrentDictionary<string, string> HarnessFiles = new(StringComparer.Ordinal);

    public static IReadOnlyList<Test262Result> Run(Test262Test test)
    {
        var results = new List<Test262Result>();
        var source = File.ReadAllText(test.FullPath);
        foreach (var mode in test.Modes)
        {
            string? error;
            try
            {
                error = RunMode(test, source, mode);
            }
            catch (JsTerminationException ex)
            {
                error = "Terminated: " + ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                error = "Engine failure: " + ex;
            }
            catch (NotSupportedException ex)
            {
                error = "Not supported: " + ex.Message;
            }

            results.Add(new Test262Result(mode, error is null, error));
        }

        return results;
    }

    /// <summary>Runs the test in a mode; returns null when it passes, or the reason of the failure.</summary>
    private static string? RunMode(Test262Test test, string source, Test262Mode mode)
    {
        if (mode == Test262Mode.Module)
            return "Modules are not supported yet";

        var metadata = test.Metadata;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var engine = new JsEngine(new JsEngineOptions
        {
            CancellationToken = cancellation.Token,
            RecursionLimit = 1_000,
            TimeZone = "UTC",
        });

        var output = new StringBuilder();
        var realm = engine.CreateRealm();
        Test262Host.Install(realm, output);
        try
        {
            if (!metadata.IsRaw)
            {
                foreach (var include in GetIncludes(metadata))
                {
                    realm.Execute(GetHarnessFile(include), include);
                }
            }

            var code = mode == Test262Mode.Strict ? "\"use strict\";\n" + source : source;
            realm.Execute(code, test.Id);
            engine.RunJobs();
        }
        catch (JsException ex)
        {
            if (metadata.NegativePhase is { } phase)
            {
                var name = GetErrorName(ex.Value);
                if (name == metadata.NegativeType)
                    return null;

                return $"Expected a {metadata.NegativeType} ({phase}), got {name}: {ex.Message}";
            }

            return "Uncaught exception: " + ex.Message;
        }

        if (metadata.NegativePhase is not null)
            return $"Expected a {metadata.NegativeType} ({metadata.NegativePhase}), but no exception was thrown";

        if (metadata.IsAsync)
        {
            var text = output.ToString();
            if (text.Contains("Test262:AsyncTestFailure:", StringComparison.Ordinal))
                return "Async failure: " + text;

            if (!text.Contains("Test262:AsyncTestComplete", StringComparison.Ordinal))
                return "The async test did not complete. Output: " + text;
        }

        return null;
    }

    private static IEnumerable<string> GetIncludes(Test262Metadata metadata)
    {
        yield return "assert.js";
        yield return "sta.js";
        if (metadata.IsAsync)
            yield return "doneprintHandle.js";

        foreach (var include in metadata.Includes)
        {
            yield return include;
        }
    }

    private static string GetHarnessFile(string name) => HarnessFiles.GetOrAdd(name, static n => File.ReadAllText(Path.Combine(Test262Paths.Harness, n)));

    /// <summary>The name of the constructor of an error, read without running code.</summary>
    private static string GetErrorName(JsValue value)
    {
        if (value.TryGetObject() is { } obj)
        {
            var constructor = obj.Get(new PropertyKey("constructor"));
            if (constructor.TryGetObject() is { } constructorObject && constructorObject.Get(new PropertyKey("name")) is { IsString: true } name)
                return name.AsString();
        }

        return value.ToString();
    }
}
