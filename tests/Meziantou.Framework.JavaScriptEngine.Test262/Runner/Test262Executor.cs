using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
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
            // The test runs interpreted, then with every function compiled to .NET code on its first call
            // (JsEngineOptions.CompilationThreshold = 1): both must give the result of the expectations
            var interpreted = RunModeSafe(test, source, mode, isCompiled: false);
            var compiled = RunModeSafe(test, source, mode, isCompiled: true);
            var error = (interpreted, compiled) switch
            {
                (null, null) => null,
                (not null, null) => "Interpreted: " + interpreted,
                (null, not null) => "Compiled: " + compiled,
                _ when interpreted == compiled => interpreted,
                _ => "Interpreted: " + interpreted + " | Compiled: " + compiled,
            };

            results.Add(new Test262Result(mode, error is null, error));
        }

        return results;
    }

    /// <summary>The options of the engines of a test (the main engine and the engines of the agents).</summary>
    internal static JsEngineOptions CreateEngineOptions(CancellationToken cancellationToken, bool isCompiled)
    {
        return new JsEngineOptions
        {
            CancellationToken = cancellationToken,
            RecursionLimit = 1_000,
            EnableCompilation = isCompiled,
            CompilationThreshold = 1,
            TimeZone = "UTC",
        };
    }

    private static string? RunModeSafe(Test262Test test, string source, Test262Mode mode, bool isCompiled)
    {
        try
        {
            return RunMode(test, source, mode, isCompiled);
        }
        catch (JsTerminationException ex)
        {
            return "Terminated: " + ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            return "Engine failure: " + ex;
        }
        catch (NotSupportedException ex)
        {
            return "Not supported: " + ex.Message;
        }
    }

    /// <summary>Runs the test in a mode; returns null when it passes, or the reason of the failure.</summary>
    /// <remarks>
    /// The method is compiled optimized up front, without dynamic PGO: the .NET 11 RC2 JIT crashes the process (in
    /// Compiler::fgCreateGCPoll) when it recompiles it at tier 1 with the profile data.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static string? RunMode(Test262Test test, string source, Test262Mode mode, bool isCompiled)
    {
        var metadata = test.Metadata;

        // The slowest tests take a few seconds alone; the limit leaves room for the slower CI machines and the parallel
        // runs. The compiled run compiles the code that runs once too: the tests that eval tens of thousands of scripts
        // (language/comments/S7.4_A5.js...) take minutes on CI
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(isCompiled ? 600 : 120));
        var loader = new Test262ModuleLoader(Path.GetDirectoryName(test.FullPath)!);
        var options = CreateEngineOptions(cancellation.Token, isCompiled);
        options.ModuleLoader = loader;
        using var engine = new JsEngine(options);

        // [[CanBlock]]: the tests flagged CanBlockIsFalse expect Atomics.wait to throw in the main agent
        engine.CanBlock = !metadata.HasFlag("CanBlockIsFalse");
        using var agents = new Test262Agents(cancellation.Token, isCompiled);
        var output = new StringBuilder();
        var realm = engine.CreateRealm();
        Test262Host.Install(realm, output, agents);
        agents.InstallSetTimeout(realm);
        try
        {
            if (!metadata.IsRaw)
            {
                foreach (var include in GetIncludes(metadata))
                {
                    realm.Execute(GetHarnessFile(include), include);
                }
            }

            if (mode == Test262Mode.Module)
            {
                RunModule(engine, realm, loader, agents, test, source);
            }
            else
            {
                var code = mode == Test262Mode.Strict ? "\"use strict\";\n" + source : source;
                realm.Execute(code, test.FullPath);
                agents.RunJobs(engine);
            }
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

        if (agents.GetError() is { } agentError)
            return agentError;

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

    /// <summary>Loads, links and evaluates the module of a test; an error of a phase is thrown.</summary>
    private static void RunModule(JsEngine engine, JsRealm realm, Test262ModuleLoader loader, Test262Agents agents, Test262Test test, string source)
    {
        var module = realm.ParseModule(source, test.FullPath);
        loader.Register(realm, test.FullPath, module);
        var loading = module.LoadRequestedModules();
        agents.RunJobs(engine);
        ThrowIfRejected(loading);
        module.Link();
        var evaluation = module.Evaluate();
        agents.RunJobs(engine);
        ThrowIfRejected(evaluation);
    }

    private static void ThrowIfRejected(JsValue promise)
    {
        var jsPromise = (JsPromise)promise.Cell!;
        if (jsPromise.State == PromiseState.Rejected)
            throw new JsException(jsPromise.Result);

        if (jsPromise.State == PromiseState.Pending)
            throw new InvalidOperationException("The promise of the module is still pending");
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

    private static string GetHarnessFile(string name) => HarnessFiles.GetOrAdd(name, static n => File.ReadAllText(Test262Paths.Harness / n));

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
