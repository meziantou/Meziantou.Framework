using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.NodeJs.Tests;

public sealed partial class NodeJsHostTests
{
    private static readonly TimeSpan OutputTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Evaluate_ReturnsValue()
    {
        await using var node = await StartNodeAsync();

        var result = await node.EvaluateAsync("return 1 + 2;", XunitCancellationToken);

        Assert.Equal(3, result.GetInt32());
        Assert.StartsWith("v", node.NodeVersion);
    }

    [Fact]
    public async Task Evaluate_UndefinedResult_ReturnsNull()
    {
        await using var node = await StartNodeAsync();

        var result = await node.EvaluateAsync("const a = 1;", XunitCancellationToken);

        Assert.Equal(JsonValueKind.Null, result.ValueKind);
    }

    [Fact]
    public async Task Evaluate_GlobalsPersistAcrossCalls()
    {
        await using var node = await StartNodeAsync();

        await node.EvaluateAsync("globalThis.counter = 41;", XunitCancellationToken);
        var result = await node.EvaluateAsync("return ++globalThis.counter;", XunitCancellationToken);

        Assert.Equal(42, result.GetInt32());
    }

    [Fact]
    public async Task Evaluate_RequireAndAwait()
    {
        await using var node = await StartNodeAsync();

        var result = await node.EvaluateAsync("""
            const path = require('node:path');
            await new Promise(resolve => setTimeout(resolve, 10));
            return path.posix.join('a', 'b');
            """, XunitCancellationToken);

        Assert.Equal("a/b", result.GetString());
    }

    [Fact]
    public async Task Evaluate_TypedResult()
    {
        await using var node = await StartNodeAsync();

        var result = await node.EvaluateAsync("return { name: 'John', age: 42 };", NodeJsTestJsonContext.Default.Person, XunitCancellationToken);

        Assert.NotNull(result);
        Assert.Equal("John", result.Name);
        Assert.Equal(42, result.Age);
    }

    [Fact]
    public async Task Evaluate_TypedResult_Reflection()
    {
        await using var node = await StartNodeAsync();

        var result = await node.EvaluateAsync<int[]>("return [1, 2, 3];", options: null, XunitCancellationToken);

        int[] expected = [1, 2, 3];
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task Invoke_EsModule_NamedExport()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("math.mjs", """
            export function add(a, b) { return a + b; }
            export async function addAsync(a, b) { await new Promise(r => setTimeout(r, 10)); return a + b; }
            """);
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });

        var result = await node.InvokeAsync("./math.mjs", "add", [1, 2], XunitCancellationToken);
        var asyncResult = await node.InvokeAsync("./math.mjs", "addAsync", [3, 4], XunitCancellationToken);

        Assert.Equal(3, result.GetInt32());
        Assert.Equal(7, asyncResult.GetInt32());
    }

    [Fact]
    public async Task Invoke_EsModule_DefaultExport()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("greet.mjs", "export default name => `Hello ${name}`;");
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });

        var result = await node.InvokeAsync("./greet.mjs", exportName: null, ["World"], XunitCancellationToken);

        Assert.Equal("Hello World", result.GetString());
    }

    [Fact]
    public async Task Invoke_CommonJsModule()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("math.cjs", "module.exports = { add: (a, b) => a + b, name: 'math' };");
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });

        var result = await node.InvokeAsync("./math.cjs", "add", [1, 2], XunitCancellationToken);
        var name = await node.InvokeAsync("./math.cjs", "name", arguments: null, XunitCancellationToken);

        Assert.Equal(3, result.GetInt32());
        Assert.Equal("math", name.GetString());
    }

    [Fact]
    public async Task Invoke_Method_IsCalledWithContainerAsThis()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("literal.cjs", "module.exports = { prefix: 'literal-', format(value) { return this.prefix + value; } };");
        temporaryDirectory.CreateTextFile("assigned.cjs", "exports.prefix = 'assigned-'; exports.format = function (value) { return this.prefix + value; };");
        temporaryDirectory.CreateTextFile("esm.mjs", "export const prefix = 'esm-'; export function format(value) { return this.prefix + value; }");
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });

        var literal = await node.InvokeAsync("./literal.cjs", "format", ["a"], XunitCancellationToken);
        var assigned = await node.InvokeAsync("./assigned.cjs", "format", ["b"], XunitCancellationToken);
        var esm = await node.InvokeAsync("./esm.mjs", "format", ["c"], XunitCancellationToken);

        Assert.Equal("literal-a", literal.GetString());
        Assert.Equal("assigned-b", assigned.GetString());
        Assert.Equal("esm-c", esm.GetString());
    }

    [Fact]
    public async Task Invoke_NpmInstalledPackage()
    {
        var npmPath = ExecutableFinder.GetFullExecutablePath("npm");
        global::Xunit.Assert.SkipWhen(npmPath is null, "npm is not installed.");

        // A "file:" dependency is installed from the local folder, so the test does not need the npm registry
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("lib/package.json", """{ "name": "greeting-lib", "version": "1.0.0", "main": "index.js" }""");
        temporaryDirectory.CreateTextFile("lib/index.js", """
            module.exports = {
                punctuation: '!',
                greet(name) { return `Hello ${name}${this.punctuation}`; },
            };
            """);
        temporaryDirectory.CreateTextFile("app/package.json", """{ "name": "app", "version": "1.0.0", "private": true, "dependencies": { "greeting-lib": "file:../lib" } }""");
        temporaryDirectory.CreateTextFile("app/index.js", """
            const lib = require('greeting-lib');
            module.exports = { greetAll: names => names.map(name => lib.greet(name)) };
            """);
        await RunNpmInstallAsync(npmPath, temporaryDirectory.GetFullPath("app"));
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.GetFullPath("app") });

        var fromIndex = await node.InvokeAsync("./index.js", "greetAll", [new JsonArray("Alice", "Bob")], XunitCancellationToken);
        var fromPackage = await node.InvokeAsync("greeting-lib", "greet", ["Carol"], XunitCancellationToken);
        var fromEvaluate = await node.EvaluateAsync("return require('greeting-lib').greet('Dave');", XunitCancellationToken);

        Assert.Equal(new[] { "Hello Alice!", "Hello Bob!" }, fromIndex.EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("Hello Carol!", fromPackage.GetString());
        Assert.Equal("Hello Dave!", fromEvaluate.GetString());
    }

    [Fact]
    public async Task Invoke_AbsolutePath()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var path = temporaryDirectory.CreateTextFile("sub dir/value.mjs", "export const value = 42;");
        await using var node = await StartNodeAsync();

        var result = await node.InvokeAsync(path, "value", arguments: null, XunitCancellationToken);

        Assert.Equal(42, result.GetInt32());
    }

    [Fact]
    public async Task Invoke_Packages_ResolvedFromWorkingDirectory()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("node_modules/esm-package/package.json", """{ "name": "esm-package", "type": "module", "exports": { "import": "./index.js" } }""");
        temporaryDirectory.CreateTextFile("node_modules/esm-package/index.js", "export const upper = s => s.toUpperCase();");
        temporaryDirectory.CreateTextFile("node_modules/cjs-package/package.json", """{ "name": "cjs-package", "main": "main.js" }""");
        temporaryDirectory.CreateTextFile("node_modules/cjs-package/main.js", "module.exports = { lower: s => s.toLowerCase() };");
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });

        var upper = await node.InvokeAsync("esm-package", "upper", ["abc"], XunitCancellationToken);
        var lower = await node.InvokeAsync("cjs-package", "lower", ["ABC"], XunitCancellationToken);

        Assert.Equal("ABC", upper.GetString());
        Assert.Equal("abc", lower.GetString());
    }

    [Fact]
    public async Task Invoke_ComplexArgumentsRoundTrip()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("echo.mjs", "export const echo = (...args) => args;");
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });
        var argument = JsonNode.Parse("""{ "array": [1, 2.5, true, null], "text": "line1\nline2\r\n\"quoted\" é 😀 \u2028", "nested": { "empty": {} } }""");

        var result = await node.InvokeAsync("./echo.mjs", "echo", [argument, null], XunitCancellationToken);

        Assert.True(JsonNode.DeepEquals(new JsonArray(argument!.DeepClone(), null), JsonNode.Parse(result.GetRawText())));
    }

    [Fact]
    public async Task Invoke_TypedResult()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("person.mjs", "export const create = (name, age) => ({ name, age });");
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });

        var result = await node.InvokeAsync("./person.mjs", "create", ["Jane", 30], NodeJsTestJsonContext.Default.Person, XunitCancellationToken);
        var reflectionResult = await node.InvokeAsync<Person>("./person.mjs", "create", ["Jane", 31], new JsonSerializerOptions(JsonSerializerDefaults.Web) { RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true }, XunitCancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Jane", result.Name);
        Assert.Equal(30, result.Age);
        Assert.NotNull(reflectionResult);
        Assert.Equal("Jane", reflectionResult.Name);
        Assert.Equal(31, reflectionResult.Age);
    }

    [Fact]
    public async Task Invoke_MissingExport_Throws()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("module.mjs", "export const a = 1;");
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.InvokeAsync("./module.mjs", "missing", arguments: null, XunitCancellationToken));

        Assert.Contains("does not export 'missing'", exception.Message);
    }

    [Fact]
    public async Task Invoke_MissingModule_Throws()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.InvokeAsync("package-that-does-not-exist", "a", arguments: null, XunitCancellationToken));

        Assert.Equal("Error", exception.JavaScriptErrorName);
        Assert.Contains("package-that-does-not-exist", exception.Message);
    }

    [Fact]
    public async Task Evaluate_JavaScriptError_Throws()
    {
        await using var node = await StartNodeAsync();

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("throw new TypeError('invalid value');", XunitCancellationToken));

        Assert.Equal("TypeError: invalid value", exception.Message);
        Assert.Equal("TypeError", exception.JavaScriptErrorName);
        Assert.Contains("invalid value", exception.JavaScriptStack);
        Assert.Null(exception.ExitCode);

        var result = await node.EvaluateAsync("return 'still alive';", XunitCancellationToken);
        Assert.Equal("still alive", result.GetString());
    }

    [Fact]
    public async Task Evaluate_ThrowNonError_Throws()
    {
        await using var node = await StartNodeAsync();

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("throw 'oops';", XunitCancellationToken));

        Assert.Equal("string: oops", exception.Message);
    }

    [Fact]
    public async Task Evaluate_NonSerializableResult_Throws()
    {
        await using var node = await StartNodeAsync();

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("return 1n;", XunitCancellationToken));

        Assert.Equal("TypeError", exception.JavaScriptErrorName);
    }

    [Fact]
    public async Task Evaluate_ConcurrentCalls()
    {
        await using var node = await StartNodeAsync();

        var tasks = Enumerable.Range(0, 100)
            .Select(i => node.EvaluateAsync($"await new Promise(r => setTimeout(r, {100 - i})); return {i};", XunitCancellationToken))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(Enumerable.Range(0, 100), results.Select(r => r.GetInt32()));
    }

    [Fact]
    public async Task StandardOutputAndError_DoNotBreakProtocol()
    {
        var output = new ConcurrentQueue<string>();
        var error = new ConcurrentQueue<string>();
        await using var node = await StartNodeAsync(new NodeJsHostOptions
        {
            StandardOutputReceived = output.Enqueue,
            StandardErrorReceived = error.Enqueue,
        });

        var result = await node.EvaluateAsync("""
            console.log('{"id":1,"result":"fake"}');
            console.error('error line');
            process.stdout.write('partial');
            return 'done';
            """, XunitCancellationToken);
        await node.EvaluateAsync("console.log(' line');", XunitCancellationToken);

        Assert.Equal("done", result.GetString());
        await WaitUntilAsync(() => output.Contains("partial line") && error.Contains("error line"));
        Assert.Contains("""{"id":1,"result":"fake"}""", output);
    }

    [Fact]
    public async Task EnvironmentVariablesAndArguments()
    {
        await using var node = await StartNodeAsync(new NodeJsHostOptions
        {
            EnvironmentVariables = { ["MEZIANTOU_NODEJS_TEST"] = "value" },
            NodeArguments = { "--max-old-space-size=256" },
        });

        var result = await node.EvaluateAsync("return [process.env.MEZIANTOU_NODEJS_TEST, process.execArgv.includes('--max-old-space-size=256'), process.env.MEZIANTOU_NODEJS_TOKEN ?? null];", XunitCancellationToken);

        Assert.Equal("value", result[0].GetString());
        Assert.True(result[1].GetBoolean());
        Assert.Equal(JsonValueKind.Null, result[2].ValueKind);
    }

    [Fact]
    public async Task Cancellation_StopsWaiting()
    {
        await using var node = await StartNodeAsync();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);

        var task = node.EvaluateAsync("await new Promise(r => setTimeout(r, 60_000));", cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        var result = await node.EvaluateAsync("return 1;", XunitCancellationToken);
        Assert.Equal(1, result.GetInt32());
    }

    [Fact]
    public async Task ProcessExit_FailsPendingAndFutureCalls()
    {
        await using var node = await StartNodeAsync(new NodeJsHostOptions { StandardErrorReceived = _ => { } });

        var pending = node.EvaluateAsync("await new Promise(r => setTimeout(r, 60_000));", XunitCancellationToken);
        var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("console.error('exiting'); process.exit(3);", XunitCancellationToken));

        Assert.Equal(3, exception.ExitCode);
        Assert.Contains("exiting", exception.Message);
        Assert.Equal(3, (await Assert.ThrowsAsync<NodeJsException>(() => pending)).ExitCode);
        Assert.Equal(3, (await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("return 1;", XunitCancellationToken))).ExitCode);
    }

    [Fact]
    public async Task Dispose_StopsProcess()
    {
        var node = await StartNodeAsync();
        using var process = Process.GetProcessById(node.ProcessId);
        var pending = node.EvaluateAsync("await new Promise(r => setTimeout(r, 60_000));", XunitCancellationToken);

        await node.DisposeAsync();

        Assert.True(process.HasExited);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => node.EvaluateAsync("return 1;", XunitCancellationToken));
        await node.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_StopsBusyProcess()
    {
        var output = new ConcurrentQueue<string>();
        var node = await StartNodeAsync(new NodeJsHostOptions { StandardOutputReceived = output.Enqueue });
        using var process = Process.GetProcessById(node.ProcessId);

        // The event loop is blocked after the call completes, so the process cannot react to the connection being closed.
        // The marker is written synchronously, so it is received even though the event loop is blocked.
        await node.EvaluateAsync("setTimeout(() => { require('node:fs').writeSync(1, 'busy\\n'); while (true) { } }, 0);", XunitCancellationToken);
        await WaitUntilAsync(() => output.Contains("busy"));

        await node.DisposeAsync();

        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task Start_InvalidExecutablePath_Throws()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();

        await Assert.ThrowsAsync<NodeJsException>(() => NodeJsHost.StartAsync(new NodeJsHostOptions { NodeExecutablePath = temporaryDirectory.GetFullPath("node-does-not-exist") }, XunitCancellationToken));
    }

    [Fact]
    public async Task Start_ProcessExitsImmediately_Throws()
    {
        SkipIfNodeIsNotInstalled();

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => NodeJsHost.StartAsync(new NodeJsHostOptions { NodeArguments = { "--unknown-option-for-test" } }, XunitCancellationToken));

        Assert.NotNull(exception.ExitCode);
        Assert.Contains("--unknown-option-for-test", exception.Message);
    }

    [Fact]
    public async Task Invoke_LargePayload()
    {
        await using var node = await StartNodeAsync();

        // The JSON writer escapes 'é' as "\u00E9", so Node.js receives a 60 MB message split into many chunks
        var value = new string('é', 10_000_000);

        // Splitting the received chunks into lines must be linear: rescanning the whole buffer for each chunk took 24s for this payload, instead of about 1s
        var result = await node.InvokeAsync("node:util", "format", [value], XunitCancellationToken).WaitAsync(TimeSpan.FromSeconds(15), XunitCancellationToken);

        Assert.Equal(value, result.GetString());
    }

    [Fact]
    public async Task ChildProcessInheritingOutput_DoesNotDelayExitDetection()
    {
        var node = await StartNodeAsync(new NodeJsHostOptions { StandardErrorReceived = _ => { } });
        var childProcessId = 0;
        try
        {
            // The child process keeps the redirected standard output and error open after the Node.js process exits
            childProcessId = (await node.EvaluateAsync("""
                const { spawn } = require('node:child_process');
                const child = spawn(process.execPath, ['-e', 'setTimeout(() => {}, 60_000)'], { stdio: 'inherit', detached: true });
                child.unref();
                return child.pid;
                """, XunitCancellationToken)).GetInt32();

            var stopwatch = Stopwatch.StartNew();
            var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("process.exit(4);", XunitCancellationToken));
            var exitDetectionDuration = stopwatch.Elapsed;

            stopwatch.Restart();
            await node.DisposeAsync();
            var disposeDuration = stopwatch.Elapsed;

            // Waiting for the output to be closed took 10s to detect the exit, and 20s to dispose
            Assert.Equal(4, exception.ExitCode);
            Assert.True(exitDetectionDuration < TimeSpan.FromSeconds(8), $"Exit detected after {exitDetectionDuration}");
            Assert.True(disposeDuration < TimeSpan.FromSeconds(8), $"Disposed after {disposeDuration}");
        }
        finally
        {
            await node.DisposeAsync();
            KillProcess(childProcessId);
        }
    }

    [Fact]
    public async Task Dispose_ChildProcessInheritingOutput_DoesNotDelayDispose()
    {
        var node = await StartNodeAsync();
        var childProcessId = 0;
        try
        {
            childProcessId = (await node.EvaluateAsync("""
                const { spawn } = require('node:child_process');
                const child = spawn(process.execPath, ['-e', 'setTimeout(() => {}, 60_000)'], { stdio: 'inherit', detached: true });
                child.unref();
                return child.pid;
                """, XunitCancellationToken)).GetInt32();
            using var process = Process.GetProcessById(node.ProcessId);

            var stopwatch = Stopwatch.StartNew();
            await node.DisposeAsync();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), $"Disposed after {stopwatch.Elapsed}");
            Assert.True(process.HasExited);
        }
        finally
        {
            KillProcess(childProcessId);
        }
    }

    [Fact]
    public async Task UncaughtException_DoesNotStopHost()
    {
        var error = new ConcurrentQueue<string>();
        await using var node = await StartNodeAsync(new NodeJsHostOptions { StandardErrorReceived = error.Enqueue });
        var pending = node.EvaluateAsync("await new Promise(r => setTimeout(r, 500)); return 'pending';", XunitCancellationToken);

        await node.EvaluateAsync("setTimeout(() => { throw new Error('error from timer'); }, 0);", XunitCancellationToken);

        Assert.Equal("pending", (await pending).GetString());
        Assert.Equal(1, (await node.EvaluateAsync("return 1;", XunitCancellationToken)).GetInt32());
        await WaitUntilAsync(() => error.Any(line => line.Contains("error from timer", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task UnhandledRejection_DoesNotStopHost()
    {
        var error = new ConcurrentQueue<string>();
        await using var node = await StartNodeAsync(new NodeJsHostOptions { StandardErrorReceived = error.Enqueue });
        var pending = node.EvaluateAsync("await new Promise(r => setTimeout(r, 500)); return 'pending';", XunitCancellationToken);

        await node.EvaluateAsync("Promise.reject(new Error('rejection nobody handles'));", XunitCancellationToken);

        Assert.Equal("pending", (await pending).GetString());
        Assert.Equal(1, (await node.EvaluateAsync("return 1;", XunitCancellationToken)).GetInt32());
        await WaitUntilAsync(() => error.Any(line => line.Contains("rejection nobody handles", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Evaluate_CanceledToken_Throws()
    {
        await using var node = await StartNodeAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => node.EvaluateAsync("return 1;", cts.Token));
        Assert.Equal(1, (await node.EvaluateAsync("return 1;", XunitCancellationToken)).GetInt32());
    }

    [Fact]
    public async Task Cancellation_LateResponseIsIgnored()
    {
        await using var node = await StartNodeAsync();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);

        var canceled = node.EvaluateAsync("await new Promise(r => setTimeout(r, 200)); return 'late';", cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);

        // The response of the canceled call arrives while this call is pending
        var result = await node.EvaluateAsync("await new Promise(r => setTimeout(r, 1000)); return 'next';", XunitCancellationToken);
        Assert.Equal("next", result.GetString());
    }

    [Fact]
    public async Task Dispose_Concurrent()
    {
        var node = await StartNodeAsync();
        using var process = Process.GetProcessById(node.ProcessId);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => node.DisposeAsync().AsTask()));

        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task Start_Timeout_Throws()
    {
        SkipIfNodeIsNotInstalled();

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => NodeJsHost.StartAsync(CreateSlowStartOptions(TimeSpan.FromSeconds(1)), XunitCancellationToken));

        Assert.Contains("did not start within", exception.Message);
    }

    [Fact]
    public async Task Start_Canceled_Throws()
    {
        SkipIfNodeIsNotInstalled();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NodeJsHost.StartAsync(CreateSlowStartOptions(), cts.Token));
    }

    [Fact]
    public async Task Pool_RunsCpuBoundCallsInParallel()
    {
        SkipIfNodeIsNotInstalled();
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var barrierPath = temporaryDirectory.GetFullPath("barrier.txt");
        var pool = await NodeJsHostPool.StartAsync(2, cancellationToken: XunitCancellationToken);

        // Each call blocks its event loop until both calls have started, which is only possible when they run in different processes
        var code = $$"""
            const fs = require('node:fs');
            const path = {{JsonSerializer.Serialize((string)barrierPath)}};
            fs.appendFileSync(path, 'x');
            const deadline = Date.now() + 60_000;
            while (fs.readFileSync(path, 'utf8').length < 2) {
                if (Date.now() > deadline) throw new Error('The other call did not start');
            }
            return process.pid;
            """;
        var results = await Task.WhenAll(pool.EvaluateAsync(code, XunitCancellationToken), pool.EvaluateAsync(code, XunitCancellationToken));
        var processIds = results.Select(result => result.GetInt32()).ToArray();

        Assert.Equal(2, pool.Size);
        Assert.NotEqual(processIds[0], processIds[1]);

        var processes = processIds.Select(Process.GetProcessById).ToArray();
        try
        {
            await pool.DisposeAsync();
            Assert.All(processes, process => process.HasExited);
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pool.EvaluateAsync("return 1;", XunitCancellationToken));
        await pool.DisposeAsync();
    }

    [Fact]
    public async Task Pool_RunAsync_UsesSameHost()
    {
        SkipIfNodeIsNotInstalled();
        await using var pool = await NodeJsHostPool.StartAsync(3, cancellationToken: XunitCancellationToken);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => pool.RunAsync(async host =>
        {
            var first = await host.EvaluateAsync("await new Promise(r => setTimeout(r, 10)); return process.pid;", XunitCancellationToken);
            var second = await host.EvaluateAsync("return process.pid;", XunitCancellationToken);
            return (First: first.GetInt32(), Second: second.GetInt32(), host.ProcessId);
        }, XunitCancellationToken)));

        Assert.All(results, result => result.First == result.ProcessId && result.Second == result.ProcessId);
        Assert.HasCount(3, results.Select(result => result.ProcessId).Distinct());
    }

    [Fact]
    public async Task Pool_ReplacesExitedHost()
    {
        SkipIfNodeIsNotInstalled();
        await using var pool = await NodeJsHostPool.StartAsync(1, cancellationToken: XunitCancellationToken);
        var firstProcessId = (await pool.EvaluateAsync("return process.pid;", XunitCancellationToken)).GetInt32();

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => pool.EvaluateAsync("process.exit(5);", XunitCancellationToken));
        var secondProcessId = (await pool.EvaluateAsync("return process.pid;", XunitCancellationToken)).GetInt32();

        Assert.Equal(5, exception.ExitCode);
        Assert.NotEqual(firstProcessId, secondProcessId);
    }

    [Fact]
    public async Task Pool_ReplacementFailsToStart_RecoversLater()
    {
        SkipIfNodeIsNotInstalled();
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var options = new NodeJsHostOptions();
        await using var pool = await NodeJsHostPool.StartAsync(1, options, XunitCancellationToken);

        // The pool starts replacements with the same options instance
        options.NodeExecutablePath = temporaryDirectory.GetFullPath("node-does-not-exist");
        await Assert.ThrowsAsync<NodeJsException>(() => pool.EvaluateAsync("process.exit(1);", XunitCancellationToken));
        var startException = await Assert.ThrowsAsync<NodeJsException>(() => pool.EvaluateAsync("return 1;", XunitCancellationToken));
        options.NodeExecutablePath = null;
        var result = await pool.EvaluateAsync("return 1;", XunitCancellationToken);

        Assert.Contains("Cannot start", startException.Message);
        Assert.Equal(1, result.GetInt32());
    }

    [Fact]
    public async Task Pool_CanceledWhileReplacementStarts_Throws()
    {
        SkipIfNodeIsNotInstalled();
        var options = new NodeJsHostOptions();
        await using var pool = await NodeJsHostPool.StartAsync(1, options, XunitCancellationToken);
        await Assert.ThrowsAsync<NodeJsException>(() => pool.EvaluateAsync("process.exit(1);", XunitCancellationToken));
        ConfigureSlowStart(options);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pool.EvaluateAsync("return 1;", cts.Token));
    }

    [Fact]
    public async Task Pool_DisposedWhileReplacementStarts()
    {
        SkipIfNodeIsNotInstalled();
        var options = new NodeJsHostOptions();
        var pool = await NodeJsHostPool.StartAsync(1, options, XunitCancellationToken);
        await Assert.ThrowsAsync<NodeJsException>(() => pool.EvaluateAsync("process.exit(1);", XunitCancellationToken));
        ConfigureSlowStart(options);
        var pending = pool.EvaluateAsync("return 1;", XunitCancellationToken);

        var stopwatch = Stopwatch.StartNew();
        await pool.DisposeAsync();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), $"Disposed after {stopwatch.Elapsed}");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task Pool_InvalidSize_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => NodeJsHostPool.StartAsync(0, cancellationToken: XunitCancellationToken));
    }

    [Fact]
    public async Task Pool_StartFailure_Throws()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();

        await Assert.ThrowsAsync<NodeJsException>(() => NodeJsHostPool.StartAsync(2, new NodeJsHostOptions { NodeExecutablePath = temporaryDirectory.GetFullPath("node-does-not-exist") }, XunitCancellationToken));
    }

    // The bootstrap script waits for this module to be imported before connecting, so the startup does not complete
    private static NodeJsHostOptions CreateSlowStartOptions(TimeSpan? startupTimeout = null)
    {
        var options = new NodeJsHostOptions();
        if (startupTimeout is not null)
        {
            options.StartupTimeout = startupTimeout.Value;
        }

        ConfigureSlowStart(options);
        return options;
    }

    private static void ConfigureSlowStart(NodeJsHostOptions options)
    {
        options.NodeArguments.Add("--import");
        options.NodeArguments.Add("data:text/javascript,await new Promise(r => setTimeout(r, 60_000));");
    }

    private static void KillProcess(int processId)
    {
        if (processId is 0)
            return;

        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
        }
        catch (ArgumentException)
        {
            // The process has already exited
        }
        catch (InvalidOperationException)
        {
            // The process has already exited
        }
    }

    private static async Task RunNpmInstallAsync(string npmPath, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(npmPath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("install");
        startInfo.ArgumentList.Add("--offline");
        startInfo.ArgumentList.Add("--no-audit");
        startInfo.ArgumentList.Add("--no-fund");

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync(XunitCancellationToken);
        var error = process.StandardError.ReadToEndAsync(XunitCancellationToken);
        await process.WaitForExitAsync(XunitCancellationToken);
        Assert.Equal(0, process.ExitCode, $"npm install failed:\n{await output}\n{await error}");
    }

    private static Task<NodeJsHost> StartNodeAsync(NodeJsHostOptions? options = null)
    {
        SkipIfNodeIsNotInstalled();
        return NodeJsHost.StartAsync(options, XunitCancellationToken);
    }

    private static void SkipIfNodeIsNotInstalled()
    {
        global::Xunit.Assert.SkipWhen(ExecutableFinder.GetFullExecutablePath("node") is null, "node is not installed.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < OutputTimeout, "Timed out waiting for the condition");
            await Task.Delay(10, XunitCancellationToken);
        }
    }

    private sealed class Person
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
    [JsonSerializable(typeof(Person))]
    private sealed partial class NodeJsTestJsonContext : JsonSerializerContext;
}
