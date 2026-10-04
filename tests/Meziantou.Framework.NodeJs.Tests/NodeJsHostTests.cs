using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.NodeJs.Tests;

public sealed partial class NodeJsHostTests
{
    private static readonly TimeSpan OutputTimeout = TimeSpan.FromSeconds(30);

    // Only detects a hang: a test waiting for this long must not depend on the time it takes, which varies a lot on CI agents
    private static readonly TimeSpan HangTimeout = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task Evaluate_ReturnsValue()
    {
        await using var node = await StartNodeAsync();

        var result = await node.EvaluateAsync("return 1 + 2;", XunitCancellationToken);

        Assert.Equal(3, result.GetInt32());
        Assert.StartsWith("v", node.NodeVersion);
    }

    [Fact]
    public async Task BootstrapScript_IsNotOnCommandLine()
    {
        var options = new NodeJsHostOptions();
        options.NodeArguments.Add("--no-warnings");
        await using var node = await StartNodeAsync(options);

        // The script is read from the standard input, so the command line only contains the arguments
        var result = await node.EvaluateAsync("return [process.execArgv, process.argv.length];", XunitCancellationToken);

        Assert.Equal(["--no-warnings", "--input-type=module"], result[0].EnumerateArray().Select(argument => argument.GetString()).ToArray());
        Assert.Equal(1, result[1].GetInt32());
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
    public async Task Reflection_DefaultOptions_UseCamelCaseNames()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("person.mjs", "export const create = (name, age) => ({ name, age }); export const describe = person => `${person.name} ${person.age} ${Object.keys(person)}`;");
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });

        var person = await node.InvokeAsync<Person>("./person.mjs", "create", ["Jane", 31], options: null, XunitCancellationToken);
        var description = await node.InvokeAsync<string>("./person.mjs", "describe", [new Person { Name = "John", Age = 42 }], options: null, XunitCancellationToken);
        var specialValues = await node.EvaluateAsync<double[]>("return [NaN, -Infinity, 1];", options: null, XunitCancellationToken);
        var numberAsString = await node.EvaluateAsync<int>("return '42';", options: null, XunitCancellationToken);

        Assert.NotNull(person);
        Assert.Equal("Jane", person.Name);
        Assert.Equal(31, person.Age);
        Assert.Equal("John 42 name,age", description);
        Assert.NotNull(specialValues);
        Assert.True(double.IsNaN(specialValues[0]));
        Assert.Equal(double.NegativeInfinity, specialValues[1]);
        Assert.Equal(42, numberAsString);
    }

    [Fact]
    public async Task Reflection_CancellationTokenOnlyOverloads()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("values.mjs", "export const answer = () => 42; export const create = () => ({ value: 10 });");
        var options = new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath };
        await using var node = await StartNodeAsync(options);
        await using var pool = await NodeJsHostPool.StartAsync(1, options, XunitCancellationToken);
        await using var reference = await node.InvokeReferenceAsync("./values.mjs", "create", arguments: null, XunitCancellationToken);

        var evaluated = await node.EvaluateAsync<int>("return 1;", XunitCancellationToken);
        var invoked = await node.InvokeAsync<int>("./values.mjs", "answer", XunitCancellationToken);
        var poolEvaluated = await pool.EvaluateAsync<int>("return 2;", XunitCancellationToken);
        var poolInvoked = await pool.InvokeAsync<int>("./values.mjs", "answer", XunitCancellationToken);
        var member = await reference.InvokeAsync<int>("value", XunitCancellationToken);
        var value = await reference.GetValueAsync<Dictionary<string, int>>(XunitCancellationToken);

        Assert.Equal(1, evaluated);
        Assert.Equal(42, invoked);
        Assert.Equal(2, poolEvaluated);
        Assert.Equal(42, poolInvoked);
        Assert.Equal(10, member);
        Assert.NotNull(value);
        Assert.Equal(10, value["value"]);
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
    public async Task Evaluate_ErrorWithCode_Throws()
    {
        await using var node = await StartNodeAsync();

        var fileSystem = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("return require('node:fs').readFileSync('/path/that/does/not/exist');", XunitCancellationToken));
        var numberCode = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("throw Object.assign(new Error('a'), { code: 42 });", XunitCancellationToken));
        var objectCode = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("throw Object.assign(new Error('a'), { code: { value: 1 } });", XunitCancellationToken));

        Assert.Equal("ENOENT", fileSystem.JavaScriptErrorCode);
        Assert.Equal("42", numberCode.JavaScriptErrorCode);
        Assert.Null(objectCode.JavaScriptErrorCode);
    }

    [Fact]
    public async Task Evaluate_ErrorWithCause_Throws()
    {
        await using var node = await StartNodeAsync();

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("throw new Error('outer', { cause: Object.assign(new TypeError('inner', { cause: 'root' }), { code: 'E_INNER' }) });", XunitCancellationToken));
        var cyclic = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("const error = new Error('loop'); error.cause = error; throw error;", XunitCancellationToken));
        var withoutCause = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("throw new Error('a', { cause: undefined });", XunitCancellationToken));

        Assert.Equal("Error: outer", exception.Message);
        var inner = Assert.IsType<NodeJsException>(exception.InnerException);
        Assert.Equal("TypeError: inner", inner.Message);
        Assert.Equal("TypeError", inner.JavaScriptErrorName);
        Assert.Equal("E_INNER", inner.JavaScriptErrorCode);
        Assert.Contains("inner", inner.JavaScriptStack);
        var root = Assert.IsType<NodeJsException>(inner.InnerException);
        Assert.Equal("string: root", root.Message);
        Assert.Null(root.InnerException);

        var depth = 0;
        for (Exception? current = cyclic; current is not null; current = current.InnerException)
        {
            Assert.Equal("Error: loop", current.Message);
            depth++;
        }

        Assert.Equal(9, depth);
        Assert.Null(withoutCause.InnerException);
    }

    [Fact]
    public async Task Evaluate_ThrowObject_Throws()
    {
        await using var node = await StartNodeAsync();

        var obj = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("throw { code: 'E1', detail: [1, new Map([['a', 2]])] };", XunitCancellationToken));
        var cyclic = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("const value = {}; value.self = value; throw value;", XunitCancellationToken));

        Assert.Equal("""object: {"code":"E1","detail":[1,{"a":2}]}""", obj.Message);
        Assert.Equal("object", obj.JavaScriptErrorName);
        Assert.Equal("object: [object Object]", cyclic.Message);
    }

    [Fact]
    public async Task Evaluate_ThrowNonError_Throws()
    {
        await using var node = await StartNodeAsync();

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("throw 'oops';", XunitCancellationToken));

        Assert.Equal("string: oops", exception.Message);
    }

    [Fact]
    public async Task Evaluate_ResultWithoutJsonRepresentation_Throws()
    {
        await using var node = await StartNodeAsync();

        var function = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("return () => 1;", XunitCancellationToken));
        var symbol = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("return Symbol('a');", NodeJsTestJsonContext.Default.Int32, XunitCancellationToken));
        var toJson = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("return { toJSON() { return undefined; } };", XunitCancellationToken));
        await using var reference = await node.EvaluateReferenceAsync("return () => 1;", XunitCancellationToken);
        await node.EvaluateVoidAsync("return Symbol('a');", XunitCancellationToken);
        var nested = await node.EvaluateAsync("return { a: 1, f() { }, s: Symbol('a') };", XunitCancellationToken);

        Assert.Equal("TypeError", function.JavaScriptErrorName);
        Assert.Contains("The result is a function", function.Message);
        Assert.Contains("The result is a symbol", symbol.Message);
        Assert.Contains("JSON representation is undefined", toJson.Message);
        Assert.Equal(1, (await reference.InvokeAsync(methodName: null, arguments: null, XunitCancellationToken)).GetInt32());
        Assert.Equal("""{"a":1}""", nested.GetRawText());
    }

    [Fact]
    public async Task Evaluate_NonSerializableResult_Throws()
    {
        await using var node = await StartNodeAsync();

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("const value = {}; value.self = value; return value;", XunitCancellationToken));

        Assert.Equal("TypeError", exception.JavaScriptErrorName);
    }

    [Fact]
    public async Task Evaluate_BigIntResult()
    {
        await using var node = await StartNodeAsync();

        var result = await node.EvaluateAsync("return { small: 1n, big: 10n ** 30n, list: [-5n, 2] };", XunitCancellationToken);
        var root = await node.EvaluateAsync("return -(2n ** 64n);", XunitCancellationToken);

        Assert.Equal(1, result.GetProperty("small").GetInt64());
        Assert.Equal("1000000000000000000000000000000", result.GetProperty("big").GetRawText());
        Assert.Equal(-5, result.GetProperty("list")[0].GetInt64());
        Assert.Equal(2, result.GetProperty("list")[1].GetInt32());
        Assert.Equal("-18446744073709551616", root.GetRawText());
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
    public async Task ProcessId_IsAvailableAfterDispose()
    {
        var node = await StartNodeAsync();
        var processId = (await node.EvaluateAsync("return process.pid;", XunitCancellationToken)).GetInt32();

        await node.DisposeAsync();

        Assert.Equal(processId, node.ProcessId);
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

        // Splitting the received chunks into lines must be linear: rescanning the whole buffer for each chunk used 27s of CPU
        // time for this payload, instead of about 0.2s. The CPU time of the Node.js process is measured instead of the elapsed
        // time, which varies too much on loaded CI agents (from 3s to 22s for the same commit on Windows).
        await node.EvaluateAsync("globalThis.cpuUsageBeforeLargePayload = process.cpuUsage();", XunitCancellationToken);
        var result = await node.InvokeAsync("node:util", "format", [value], XunitCancellationToken).WaitAsync(TimeSpan.FromMinutes(2), XunitCancellationToken);
        var cpuUsage = await node.EvaluateAsync("const usage = process.cpuUsage(globalThis.cpuUsageBeforeLargePayload); return (usage.user + usage.system) / 1000;", XunitCancellationToken);

        Assert.Equal(value, result.GetString());
        var cpuTime = TimeSpan.FromMilliseconds(cpuUsage.GetDouble());
        Assert.True(cpuTime < TimeSpan.FromSeconds(5), $"Node.js used {cpuTime} of CPU time to process the message");
    }

    [Fact]
    public async Task ChildProcessInheritingOutput_DoesNotDelayExitDetection()
    {
        // Without a timeout, waiting for the output to be closed hangs until the child process exits. The time it takes is not
        // checked, as observing the exit can take more than 10s on a loaded CI agent, like waiting for the output used to.
        var node = await StartNodeAsync(new NodeJsHostOptions { StandardErrorReceived = _ => { }, ExitTimeout = Timeout.InfiniteTimeSpan });
        var childProcessId = 0;
        try
        {
            childProcessId = await StartChildProcessInheritingOutputAsync(node);
            using var childProcess = Process.GetProcessById(childProcessId);

            var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("process.exit(4);", XunitCancellationToken).WaitAsync(HangTimeout, XunitCancellationToken));
            await node.DisposeAsync().AsTask().WaitAsync(HangTimeout, XunitCancellationToken);

            Assert.Equal(4, exception.ExitCode);

            // The child process still keeps the output open
            Assert.False(childProcess.HasExited);
        }
        finally
        {
            // Disposing waits for the output to be closed if the exit is not detected
            KillProcess(childProcessId);
            await node.DisposeAsync();
        }
    }

    [Fact]
    public async Task Dispose_ChildProcessInheritingOutput_DoesNotDelayDispose()
    {
        var node = await StartNodeAsync(new NodeJsHostOptions { ExitTimeout = Timeout.InfiniteTimeSpan });
        var childProcessId = 0;
        try
        {
            childProcessId = await StartChildProcessInheritingOutputAsync(node);
            using var childProcess = Process.GetProcessById(childProcessId);
            using var process = Process.GetProcessById(node.ProcessId);

            await node.DisposeAsync().AsTask().WaitAsync(HangTimeout, XunitCancellationToken);

            Assert.True(process.HasExited);

            // The child process still keeps the output open
            Assert.False(childProcess.HasExited);
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
    public async Task Cancellation_DuringLargeWrite_DoesNotCorruptProtocol()
    {
        await using var node = await StartNodeAsync();
        var value = new string('a', 20_000_000);

        // A write canceled in the middle of a message used to leave a partial line, so the next message could not be parsed and the calls never completed
        for (var i = 0; i < 5; i++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
            var canceled = node.InvokeAsync("node:util", "format", [value], cts.Token);
            await Task.Delay(i, XunitCancellationToken);
            await cts.CancelAsync();
            try
            {
                await canceled;
            }
            catch (OperationCanceledException)
            {
            }

            var result = await node.EvaluateAsync("return 1;", XunitCancellationToken).WaitAsync(TimeSpan.FromSeconds(30), XunitCancellationToken);
            Assert.Equal(1, result.GetInt32());
        }
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
        var startupModePath = temporaryDirectory.GetFullPath("startup-mode.txt");
        await using var pool = await NodeJsHostPool.StartAsync(1, CreateStartupModeOptions(startupModePath), XunitCancellationToken);

        await File.WriteAllTextAsync(startupModePath, "fail", XunitCancellationToken);
        await Assert.ThrowsAsync<NodeJsException>(() => pool.EvaluateAsync("process.exit(1);", XunitCancellationToken));
        var startException = await Assert.ThrowsAsync<NodeJsException>(() => pool.EvaluateAsync("return 1;", XunitCancellationToken));
        File.Delete(startupModePath);
        var result = await pool.EvaluateAsync("return 1;", XunitCancellationToken);

        Assert.Contains("exited before it was ready", startException.Message);
        Assert.Equal(1, result.GetInt32());
    }

    [Fact]
    public async Task Pool_CanceledWhileReplacementStarts_Throws()
    {
        SkipIfNodeIsNotInstalled();
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var startupModePath = temporaryDirectory.GetFullPath("startup-mode.txt");
        await using var pool = await NodeJsHostPool.StartAsync(1, CreateStartupModeOptions(startupModePath), XunitCancellationToken);
        await File.WriteAllTextAsync(startupModePath, "slow", XunitCancellationToken);
        await Assert.ThrowsAsync<NodeJsException>(() => pool.EvaluateAsync("process.exit(1);", XunitCancellationToken));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pool.EvaluateAsync("return 1;", cts.Token));
    }

    [Fact]
    public async Task Pool_DisposedWhileReplacementStarts()
    {
        SkipIfNodeIsNotInstalled();
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var startupModePath = temporaryDirectory.GetFullPath("startup-mode.txt");
        var pool = await NodeJsHostPool.StartAsync(1, CreateStartupModeOptions(startupModePath), XunitCancellationToken);
        await File.WriteAllTextAsync(startupModePath, "slow", XunitCancellationToken);
        await Assert.ThrowsAsync<NodeJsException>(() => pool.EvaluateAsync("process.exit(1);", XunitCancellationToken));
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

    [Fact]
    public async Task Invoke_JSValueArguments()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("describe.mjs", DescribeModule);
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });
        var offsetDate = new DateTimeOffset(2024, 1, 2, 3, 4, 5, 678, TimeSpan.FromHours(2));
        var localDate = new DateTime(2024, 6, 7, 8, 9, 10, 11, DateTimeKind.Local);

        var result = await node.InvokeAsync("./describe.mjs", "describeAll",
        [
            JSValue.Undefined,
            JSValue.Null,
            JSValue.String("text \"é\" 😀"),
            JSValue.Boolean(true),
            JSValue.Boolean(false),
            JSValue.BigInt(BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture)),
            JSValue.BigInt(-BigInteger.Pow(2, 100)),
            JSValue.BigInt(long.MinValue),
            JSValue.BigInt(Int128.MaxValue),
            JSValue.Number(double.NaN),
            JSValue.Number(double.PositiveInfinity),
            JSValue.Number(float.NegativeInfinity),
            JSValue.Number(-0.0),
            JSValue.Number(Half.NegativeZero),
            JSValue.Number(1.5),
            JSValue.Number(0.1m),
            JSValue.Number(42),
            JSValue.Number((byte)255),
            JSValue.Number(Int128.MaxValue),
            JSValue.Number(ulong.MaxValue),
            JSValue.Date(offsetDate),
            JSValue.Date(new DateTime(2024, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc)),
            JSValue.Date(localDate),
            JSValue.Uint8Array(new byte[] { 1, 2, 255 }),
        ], XunitCancellationToken);

        string[][] expected =
        [
            ["undefined", "undefined"],
            ["object", "null"],
            ["string", "text \"é\" 😀"],
            ["boolean", "true"],
            ["boolean", "false"],
            ["bigint", "123456789012345678901234567890"],
            ["bigint", "-1267650600228229401496703205376"],
            ["bigint", "-9223372036854775808"],
            ["bigint", "170141183460469231731687303715884105727"],
            ["number", "NaN"],
            ["number", "Infinity"],
            ["number", "-Infinity"],
            ["number", "-0"],
            ["number", "-0"],
            ["number", "1.5"],
            ["number", "0.1"],
            ["number", "42"],
            ["number", "255"],
            ["number", "1.7014118346046923e+38"],
            ["number", "18446744073709552000"],
            ["Date", "2024-01-02T01:04:05.678Z"],
            ["Date", "2024-01-02T03:04:05.678Z"],
            ["Date", new DateTimeOffset(localDate).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)],
            ["Uint8Array", "1,2,255"],
        ];
        Assert.Equal(expected.Length, result.GetArrayLength());
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i][0], result[i].GetProperty("type").GetString());
            Assert.Equal(expected[i][1], result[i].GetProperty("text").GetString());
        }
    }

    [Fact]
    public async Task Invoke_NestedJSValueArguments()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("describe.mjs", DescribeModule);
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });
        var argument = new JsonObject
        {
            ["missing"] = JSValue.Undefined,
            ["deep"] = new JsonObject { ["list"] = new JsonArray(JSValue.BigInt(7).ToJsonNode(), 1, JSValue.Undefined.ToJsonNode(), JSValue.Null.ToJsonNode(), JSValue.String("s").ToJsonNode(), JSValue.Boolean(true).ToJsonNode()) },
            ["__proto__"] = JSValue.BigInt(5),
            ["text"] = "value",
        };

        var result = await node.InvokeAsync("./describe.mjs", "describeNested", [argument], XunitCancellationToken);

        Assert.True(result.GetProperty("hasMissing").GetBoolean());
        Assert.False(result.TryGetProperty("missing", out _));
        Assert.Equal("7n,1,undefined,null,s,true", result.GetProperty("list").GetString());
        Assert.True(result.GetProperty("ownProto").GetBoolean());
        Assert.True(result.GetProperty("prototypeUnchanged").GetBoolean());
        Assert.Equal("value", result.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Invoke_JSValueArguments_Reflection()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("describe.mjs", DescribeModule);
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });

        var result = await node.InvokeAsync<string[]>("./describe.mjs", "types", [JSValue.Undefined, JSValue.BigInt(1), new JsonObject { ["a"] = JSValue.Undefined }, 5], options: null, XunitCancellationToken);

        string[] expected = ["undefined", "bigint", "object:undefined", "number"];
        Assert.Equal(expected, result);
    }

    [Fact]
    public void JSValue_ToString()
    {
        Assert.Equal("undefined", JSValue.Undefined.ToString());
        Assert.Equal("null", JSValue.Null.ToString());
        Assert.Equal("text", JSValue.String("text").ToString());
        Assert.Equal("true", JSValue.Boolean(true).ToString());
        Assert.Same(JSValue.Boolean(false), JSValue.Boolean(false));
        Assert.Throws<ArgumentNullException>(() => JSValue.String(null!));
        Assert.Equal("-5n", JSValue.BigInt(-5).ToString());
        Assert.Equal("NaN", JSValue.Number(double.NaN).ToString());
        Assert.Equal("-0", JSValue.Number(-0.0).ToString());
        Assert.Equal("1.5", JSValue.Number(1.5f).ToString());
        Assert.Equal("0.1", JSValue.Number(0.1m).ToString());
        Assert.Equal("2024-01-02T01:04:05.678Z", JSValue.Date(new DateTimeOffset(2024, 1, 2, 3, 4, 5, 678, TimeSpan.FromHours(2))).ToString());
        Assert.Equal("Uint8Array(3)", JSValue.Uint8Array(new byte[3]).ToString());
    }

    [Fact]
    public void JSValue_SerializedOutsideArguments_Throws()
    {
        JsonNode node = JSValue.Undefined.ToJsonNode();

        Assert.Throws<NotSupportedException>(() => node.ToJsonString());
        Assert.Throws<NotSupportedException>(() => node.DeepClone());
    }

    [Fact]
    public async Task InvokeVoid_IgnoresResult()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("store.mjs", "export function store(value) { globalThis.stored = value; const result = {}; result.self = result; return result; }");
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });

        await node.InvokeVoidAsync("./store.mjs", "store", [42], XunitCancellationToken);
        await node.InvokeVoidAsync("./store.mjs", "store", [43, JSValue.Undefined], options: null, XunitCancellationToken);
        await node.EvaluateVoidAsync("globalThis.evaluated = true; const result = {}; result.self = result; return result;", XunitCancellationToken);

        var result = await node.EvaluateAsync("return [globalThis.stored, globalThis.evaluated];", XunitCancellationToken);
        Assert.Equal(43, result[0].GetInt32());
        Assert.True(result[1].GetBoolean());
    }

    [Fact]
    public async Task InvokeVoid_JavaScriptError_Throws()
    {
        await using var node = await StartNodeAsync();

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateVoidAsync("throw new RangeError('void error');", XunitCancellationToken));

        Assert.Equal("RangeError: void error", exception.Message);
    }

    [Fact]
    public async Task Reference_InvokeMembers()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("counter.mjs", CounterModule);
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });

        await using var counter = await node.InvokeReferenceAsync("./counter.mjs", "createCounter", [10], XunitCancellationToken);
        var incremented = await counter.InvokeAsync("increment", [5], XunitCancellationToken);
        var reflectionIncremented = await counter.InvokeAsync<int>("increment", [1], options: null, XunitCancellationToken);
        await counter.InvokeVoidAsync("increment", [1], XunitCancellationToken);
        var property = await counter.InvokeAsync("value", arguments: null, XunitCancellationToken);
        var value = await counter.GetValueAsync(XunitCancellationToken);
        var typedValue = await counter.GetValueAsync<Dictionary<string, int>>(options: null, XunitCancellationToken);
        var missing = await Assert.ThrowsAsync<NodeJsException>(() => counter.InvokeAsync("missing", arguments: null, XunitCancellationToken));
        var notFunction = await Assert.ThrowsAsync<NodeJsException>(() => counter.InvokeAsync(methodName: null, arguments: null, XunitCancellationToken));

        Assert.Same(node, counter.Host);
        Assert.Equal(15, incremented.GetInt32());
        Assert.Equal(16, reflectionIncremented);
        Assert.Equal(17, property.GetInt32());
        Assert.Equal("""{"value":17}""", value.GetRawText());
        Assert.NotNull(typedValue);
        Assert.Equal(17, typedValue["value"]);
        Assert.Contains("does not have a member 'missing'", missing.Message);
        Assert.Contains("not a function", notFunction.Message);
    }

    [Fact]
    public async Task Reference_Function()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("counter.mjs", CounterModule);
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });

        await using var adder = await node.InvokeReferenceAsync("./counter.mjs", "makeAdder", [2], XunitCancellationToken);
        var result = await adder.InvokeAsync(methodName: null, [3], XunitCancellationToken);

        Assert.Equal(5, result.GetInt32());
    }

    [Fact]
    public async Task CreateInstance()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("shapes.mjs", """
            export default class Rectangle {
                constructor(width, height) { this.width = width; this.height = height; }
                area() { return this.width * this.height; }
            }

            export class Square extends Rectangle {
                constructor(side) { super(side, side); }
            }

            export const shapes = { Circle: class { constructor(radius) { this.radius = radius; } } };
            export const notConstructor = () => 1;
            """);
        var options = new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath };
        await using var node = await StartNodeAsync(options);
        await using var pool = await NodeJsHostPool.StartAsync(1, options, XunitCancellationToken);

        await using var rectangle = await node.CreateInstanceAsync("./shapes.mjs", exportName: null, [2, 3], XunitCancellationToken);
        await using var square = await node.CreateInstanceAsync("./shapes.mjs", "Square", [4], XunitCancellationToken);
        await using var reflectionSquare = await node.CreateInstanceAsync("./shapes.mjs", "Square", [5], options: null, XunitCancellationToken);
        await using var poolSquare = await pool.CreateInstanceAsync("./shapes.mjs", "Square", [6], XunitCancellationToken);
        await using var shapes = await node.InvokeReferenceAsync("./shapes.mjs", "shapes", arguments: null, XunitCancellationToken);
        await using var circle = await shapes.CreateInstanceAsync("Circle", [1], XunitCancellationToken);
        await using var pointClass = await node.EvaluateReferenceAsync("return class { constructor(x) { this.x = x; } };", XunitCancellationToken);
        await using var point = await pointClass.CreateInstanceAsync(memberName: null, [7], XunitCancellationToken);
        await using var functionConstructor = await node.EvaluateReferenceAsync("return Function;", XunitCancellationToken);
        var notConstructor = await Assert.ThrowsAsync<NodeJsException>(() => node.CreateInstanceAsync("./shapes.mjs", "notConstructor", arguments: null, XunitCancellationToken));
        var methodMember = await Assert.ThrowsAsync<NodeJsException>(() => rectangle.CreateInstanceAsync("area", arguments: null, XunitCancellationToken));
        var compilesCode = await Assert.ThrowsAsync<NodeJsException>(() => functionConstructor.CreateInstanceAsync(memberName: null, ["return 1;"], XunitCancellationToken));

        Assert.Equal(6, (await rectangle.InvokeAsync("area", arguments: null, XunitCancellationToken)).GetInt32());
        Assert.Equal(16, (await square.InvokeAsync("area", arguments: null, XunitCancellationToken)).GetInt32());
        Assert.Equal(25, (await reflectionSquare.InvokeAsync("area", arguments: null, XunitCancellationToken)).GetInt32());
        Assert.Equal(36, (await poolSquare.InvokeAsync("area", arguments: null, XunitCancellationToken)).GetInt32());
        Assert.Equal("""{"radius":1}""", (await circle.GetValueAsync(XunitCancellationToken)).GetRawText());
        Assert.Equal("""{"x":7}""", (await point.GetValueAsync(XunitCancellationToken)).GetRawText());
        Assert.Contains("The export 'notConstructor' of the module './shapes.mjs' is not a constructor", notConstructor.Message);
        Assert.Contains("The member 'area' of the referenced value is not a constructor", methodMember.Message);
        Assert.Contains("Functions that compile code", compilesCode.Message);
    }

    [Fact]
    public async Task Reference_Evaluate()
    {
        await using var node = await StartNodeAsync();

        await using var map = await node.EvaluateReferenceAsync("return new Map([['a', 1]]);", XunitCancellationToken);
        await map.InvokeVoidAsync("set", ["b", JSValue.BigInt(2)], XunitCancellationToken);
        var a = await map.InvokeAsync("get", ["a"], XunitCancellationToken);
        var b = await map.InvokeAsync("get", ["b"], XunitCancellationToken);
        var size = await map.InvokeAsync("size", arguments: null, XunitCancellationToken);

        Assert.Equal(1, a.GetInt32());
        Assert.Equal(2, b.GetInt32());
        Assert.Equal(2, size.GetInt32());
    }

    [Fact]
    public async Task Reference_AsArgument()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("counter.mjs", CounterModule);
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });
        await using var counter = await node.InvokeReferenceAsync("./counter.mjs", "createCounter", [10], XunitCancellationToken);

        var direct = await node.InvokeAsync("./counter.mjs", "read", [counter], XunitCancellationToken);
        var nested = await node.InvokeAsync("./counter.mjs", "readNested", [new JsonObject { ["counter"] = counter, ["list"] = new JsonArray(counter.ToJsonNode()) }], XunitCancellationToken);
        var reflection = await node.InvokeAsync<int>("./counter.mjs", "read", [counter], options: null, XunitCancellationToken);
        await using var child = await counter.InvokeReferenceAsync("child", arguments: null, XunitCancellationToken);
        var isSameObject = await node.InvokeAsync("./counter.mjs", "isParent", [child, counter], XunitCancellationToken);

        Assert.Equal(10, direct.GetInt32());
        Assert.Equal(20, nested.GetInt32());
        Assert.Equal(10, reflection);
        Assert.True(isSameObject.GetBoolean());
    }

    [Fact]
    public async Task Reference_Dispose_ReleasesValue()
    {
        await using var node = await StartNodeAsync();
        var first = await node.EvaluateReferenceAsync("return {};", XunitCancellationToken);
        var second = await node.EvaluateReferenceAsync("return {};", XunitCancellationToken);
        Assert.Equal(2, await GetReferenceCountAsync(node));

        await first.DisposeAsync();
        Assert.Equal(1, await GetReferenceCountAsync(node));

        second.Dispose();
        await WaitUntilAsync(async () => await GetReferenceCountAsync(node) is 0);

        await first.DisposeAsync();
        second.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => first.InvokeAsync("toString", arguments: null, XunitCancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => second.GetValueAsync(XunitCancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => node.InvokeAsync("node:util", "format", [first], XunitCancellationToken));
    }

    [Fact]
    public async Task Reference_DisposeAfterHost_DoesNotThrow()
    {
        var node = await StartNodeAsync();
        var first = await node.EvaluateReferenceAsync("return {};", XunitCancellationToken);
        var second = await node.EvaluateReferenceAsync("return {};", XunitCancellationToken);

        await node.DisposeAsync();

        await first.DisposeAsync();
        second.Dispose();
    }

    [Fact]
    public async Task Reference_Canceled_ReleasesValue()
    {
        await using var node = await StartNodeAsync();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);

        var canceled = node.EvaluateReferenceAsync("await new Promise(r => setTimeout(r, 200)); globalThis.created = true; return {};", cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);

        // The response of the canceled call arrives while this call is pending
        var created = await node.EvaluateAsync("await new Promise(r => setTimeout(r, 1000)); return globalThis.created;", XunitCancellationToken);
        Assert.True(created.GetBoolean());
        await WaitUntilAsync(async () => await GetReferenceCountAsync(node) is 0);
    }

    [Fact]
    public async Task Reference_FromAnotherHost_Throws()
    {
        await using var node = await StartNodeAsync();
        await using var otherNode = await StartNodeAsync();
        await using var reference = await otherNode.EvaluateReferenceAsync("return {};", XunitCancellationToken);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => node.InvokeAsync("node:util", "format", [new JsonObject { ["value"] = reference }], XunitCancellationToken));

        Assert.Equal("arguments", exception.ParamName);
        Assert.Equal("JSReference(1)", reference.ToString());
    }

    [Fact]
    public async Task Pool_Reference_RunsOnItsHost()
    {
        SkipIfNodeIsNotInstalled();
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("process.mjs", "export const create = () => ({ pid: process.pid }); export const read = value => [value.pid, process.pid];");
        await using var pool = await NodeJsHostPool.StartAsync(3, new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath }, XunitCancellationToken);
        await using var reference = await pool.InvokeReferenceAsync("./process.mjs", "create", arguments: null, XunitCancellationToken);
        await using var evaluated = await pool.EvaluateReferenceAsync("return { pid: process.pid };", XunitCancellationToken);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => pool.InvokeAsync("./process.mjs", "read", [reference], XunitCancellationToken)));
        var evaluatedResults = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => pool.InvokeAsync<int[]>("./process.mjs", "read", [evaluated], options: null, XunitCancellationToken)));
        await pool.InvokeVoidAsync("./process.mjs", "read", [new JsonObject { ["nested"] = reference }], XunitCancellationToken);
        var evaluateResults = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => pool.EvaluateAsync("return [args[0].pid, process.pid];", [reference], XunitCancellationToken)));

        Assert.All(results, result => result[0].GetInt32() == reference.Host.ProcessId && result[1].GetInt32() == reference.Host.ProcessId);
        Assert.All(evaluateResults, result => result[0].GetInt32() == reference.Host.ProcessId && result[1].GetInt32() == reference.Host.ProcessId);
        Assert.All(evaluatedResults, result => result is [var referencedProcessId, var currentProcessId] && referencedProcessId == evaluated.Host.ProcessId && currentProcessId == evaluated.Host.ProcessId);
    }

    [Fact]
    public async Task Pool_ReferenceOfReplacedProcess_ThrowsProcessExitedException()
    {
        SkipIfNodeIsNotInstalled();
        await using var pool = await NodeJsHostPool.StartAsync(1, cancellationToken: XunitCancellationToken);
        await using var reference = await pool.EvaluateReferenceAsync("return { value: 1 };", XunitCancellationToken);

        await Assert.ThrowsAsync<NodeJsException>(() => pool.EvaluateVoidAsync("process.exit(3);", XunitCancellationToken));

        // The process is replaced when the next call selects a host, and its host is disposed
        var result = await pool.EvaluateAsync("return 1;", XunitCancellationToken);
        var valueException = await Assert.ThrowsAsync<NodeJsException>(() => reference.GetValueAsync(XunitCancellationToken));
        var argumentException = await Assert.ThrowsAsync<NodeJsException>(() => pool.EvaluateAsync("return args[0].value;", [reference], XunitCancellationToken));

        Assert.Equal(1, result.GetInt32());
        Assert.Equal(3, valueException.ExitCode);
        Assert.Equal(3, argumentException.ExitCode);
    }

    [Fact]
    public async Task Pool_ReferencesOfDifferentHosts_Throws()
    {
        await using var first = await StartNodeAsync();
        await using var second = await StartNodeAsync();
        await using var pool = await NodeJsHostPool.StartAsync(1, cancellationToken: XunitCancellationToken);
        await using var firstReference = await first.EvaluateReferenceAsync("return {};", XunitCancellationToken);
        await using var secondReference = await second.EvaluateReferenceAsync("return {};", XunitCancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(async () => await pool.InvokeAsync("node:util", "format", [firstReference, secondReference], XunitCancellationToken));
    }

    [Fact]
    public async Task InvalidRequest_FailsOnlyThisCall()
    {
        await using var node = await StartNodeAsync();

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.SendInvalidRequestAsync(XunitCancellationToken));
        var result = await node.EvaluateAsync("return 1;", XunitCancellationToken);

        Assert.Equal("SyntaxError", exception.JavaScriptErrorName);
        Assert.Equal(1, result.GetInt32());
    }

    [Fact]
    public async Task Evaluate_DeeplyNestedResult()
    {
        await using var node = await StartNodeAsync();

        // JSON.stringify is recursive, so the depth is limited by the stack of the Node.js process, which is smaller on Windows
        var result = await node.EvaluateAsync("let value = 'leaf'; for (let i = 0; i < 500; i++) value = [value]; return value;", XunitCancellationToken);
        for (var i = 0; i < 500; i++)
        {
            result = result[0];
        }

        Assert.Equal("leaf", result.GetString());
        Assert.Equal(1, (await node.EvaluateAsync("return 1;", XunitCancellationToken)).GetInt32());
    }

    [Fact]
    public async Task Evaluate_LargeTypedResult()
    {
        await using var node = await StartNodeAsync();

        // The result is read from a buffer that grows to contain the whole message, then shrinks back for the following messages
        var result = await node.EvaluateAsync("return Array.from({ length: 2_000_000 }, (_, i) => i);", NodeJsTestJsonContext.Default.Int32Array, XunitCancellationToken);
        var next = await node.EvaluateAsync("return 'é'.repeat(3);", XunitCancellationToken);

        Assert.NotNull(result);
        Assert.HasCount(2_000_000, result);
        Assert.Equal(1_999_999, result[^1]);
        Assert.Equal("ééé", next.GetString());
    }

    [Fact]
    public async Task Evaluate_TypedResultCannotBeDeserialized_FailsOnlyThisCall()
    {
        await using var node = await StartNodeAsync();

        await Assert.ThrowsAsync<JsonException>(() => node.EvaluateAsync("return 'not a number';", NodeJsTestJsonContext.Default.Int32, XunitCancellationToken));
        var result = await node.EvaluateAsync("return 1;", NodeJsTestJsonContext.Default.Int32, XunitCancellationToken);

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task GetValue_LargeTypedResult()
    {
        await using var node = await StartNodeAsync();
        await using var reference = await node.EvaluateReferenceAsync("return Array.from({ length: 1_000_000 }, (_, i) => i);", XunitCancellationToken);

        var result = await reference.GetValueAsync(NodeJsTestJsonContext.Default.Int32Array, XunitCancellationToken);

        Assert.NotNull(result);
        Assert.Equal(999_999, result[^1]);
    }

    [Fact]
    public async Task Evaluate_ThrowUnprintableValue_Throws()
    {
        await using var node = await StartNodeAsync();

        var nullPrototype = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("throw Object.create(null);", XunitCancellationToken));
        var throwingName = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("const error = new Error('oops'); Object.defineProperty(error, 'name', { get() { throw new Error('name'); } }); throw error;", XunitCancellationToken));

        Assert.Equal("object: {}", nullPrototype.Message);
        Assert.Equal("object", throwingName.JavaScriptErrorName);
    }

    [Fact]
    public async Task OutputCallbacksThrow_DoesNotCrash()
    {
        var calls = 0;
        await using var node = await StartNodeAsync(new NodeJsHostOptions
        {
            StandardOutputReceived = _ => { Interlocked.Increment(ref calls); throw new InvalidOperationException("output"); },
            StandardErrorReceived = _ => { Interlocked.Increment(ref calls); throw new InvalidOperationException("error"); },
        });

        await node.EvaluateVoidAsync("console.log('out'); console.error('err');", XunitCancellationToken);
        await WaitUntilAsync(() => Volatile.Read(ref calls) >= 2);

        Assert.Equal(1, (await node.EvaluateAsync("return 1;", XunitCancellationToken)).GetInt32());
    }

    [Fact]
    public async Task Evaluate_Arguments()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("counter.mjs", CounterModule);
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath });
        await using var counter = await node.InvokeReferenceAsync("./counter.mjs", "createCounter", [10], XunitCancellationToken);

        var sum = await node.EvaluateAsync("return args[0] + args[1];", [1, 2], XunitCancellationToken);
        var types = await node.EvaluateAsync("return args.map(value => typeof value);", [JSValue.Undefined, JSValue.BigInt(1), counter], XunitCancellationToken);
        var empty = await node.EvaluateAsync("return args.length;", arguments: null, XunitCancellationToken);
        var typed = await node.EvaluateAsync("return { name: args[0], age: args[1] };", ["John", 42], NodeJsTestJsonContext.Default.Person, XunitCancellationToken);
        var reflection = await node.EvaluateAsync<int>("return args[0].value * args[1];", [counter, 2], options: null, XunitCancellationToken);
        await node.EvaluateVoidAsync("globalThis.saved = args[0];", ["value"], XunitCancellationToken);
        await node.EvaluateVoidAsync("globalThis.savedReflection = args[0];", [5], options: null, XunitCancellationToken);
        await using var reference = await node.EvaluateReferenceAsync("return { doubled: args[0] * 2 };", [21], XunitCancellationToken);
        await using var reflectionReference = await node.EvaluateReferenceAsync("return args[0];", [counter], options: null, XunitCancellationToken);

        // Without arguments, "args" is not declared, so the code can declare it
        var declared = await node.EvaluateAsync("const args = 'own'; return [args, globalThis.saved, globalThis.savedReflection];", XunitCancellationToken);

        Assert.Equal(3, sum.GetInt32());
        Assert.Equal("""["undefined","bigint","object"]""", types.GetRawText());
        Assert.Equal(0, empty.GetInt32());
        Assert.NotNull(typed);
        Assert.Equal("John", typed.Name);
        Assert.Equal(42, typed.Age);
        Assert.Equal(20, reflection);
        Assert.Equal("""["own","value",5]""", declared.GetRawText());
        Assert.Equal(42, (await reference.GetValueAsync(XunitCancellationToken)).GetProperty("doubled").GetInt32());
        Assert.Equal(11, (await reflectionReference.InvokeAsync("increment", [1], XunitCancellationToken)).GetInt32());
    }

    [Fact]
    public async Task Evaluate_Arguments_SpecialCharactersRoundTrip()
    {
        await using var node = await StartNodeAsync();
        const string Text = "<script>&'\"+ \\ é 日本 😀 \u2028\u2029 \n\r\t \0 \u001f";
        const string Key = "clé <&>";

        var result = await node.EvaluateAsync("return [args[0], Object.keys(args[1]), args[1][Object.keys(args[1])[0]] === undefined];", [Text, new JsonObject { [Key] = JSValue.Undefined }], XunitCancellationToken);

        Assert.Equal(Text, result[0].GetString());
        Assert.Equal(Key, result[1][0].GetString());
        Assert.True(result[2].GetBoolean());
    }

    [Fact]
    public async Task Evaluate_SpecialValuesResult()
    {
        await using var node = await StartNodeAsync();

        var result = await node.EvaluateAsync("""
            return {
                bytes: new Uint8Array([1, 2, 255]),
                buffer: Buffer.from('hi'),
                arrayBuffer: new Uint8Array([7]).buffer,
                view: new Uint8Array([0, 1, 2, 3]).subarray(1, 3),
                clamped: new Uint8ClampedArray([4]),
                floats: new Float64Array([1.5, NaN]),
                bigInts: new BigInt64Array([-1n]),
                map: new Map([['a', 1], [2, new Set([3])], ['__proto__', 4]]),
                set: new Set(['x', 'y']),
                numbers: [NaN, Infinity, -Infinity, -0, 1],
                nested: [new Map([['k', Buffer.from([9])]])],
                notBuffer: { type: 'Buffer', data: [1] },
            };
            """, XunitCancellationToken);

        Assert.Equal([1, 2, 255], result.GetProperty("bytes").GetBytesFromBase64());
        Assert.Equal("hi"u8.ToArray(), result.GetProperty("buffer").GetBytesFromBase64());
        Assert.Equal([7], result.GetProperty("arrayBuffer").GetBytesFromBase64());
        Assert.Equal([1, 2], result.GetProperty("view").GetBytesFromBase64());
        Assert.Equal([4], result.GetProperty("clamped").GetBytesFromBase64());
        Assert.Equal("""[1.5,"NaN"]""", result.GetProperty("floats").GetRawText());
        Assert.Equal("[-1]", result.GetProperty("bigInts").GetRawText());
        // As for any JavaScript object, integer keys come first
        Assert.Equal("""{"2":[3],"a":1,"__proto__":4}""", result.GetProperty("map").GetRawText());
        Assert.Equal("""["x","y"]""", result.GetProperty("set").GetRawText());
        Assert.Equal("""["NaN","Infinity","-Infinity",-0,1]""", result.GetProperty("numbers").GetRawText());
        Assert.Equal("""[{"k":"CQ=="}]""", result.GetProperty("nested").GetRawText());
        Assert.Equal("""{"type":"Buffer","data":[1]}""", result.GetProperty("notBuffer").GetRawText());
    }

    [Fact]
    public async Task Evaluate_SpecialValuesResult_Reflection()
    {
        await using var node = await StartNodeAsync();

        var numbers = await node.EvaluateAsync<double[]>("return [NaN, Infinity, -Infinity, -0, 1.5];", options: null, XunitCancellationToken);
        var bytes = await node.EvaluateAsync<byte[]>("return Buffer.from([1, 2]);", options: null, XunitCancellationToken);
        var map = await node.EvaluateAsync<Dictionary<int, string>>("return new Map([[1, 'a'], [2, 'b']]);", options: null, XunitCancellationToken);
        var set = await node.EvaluateAsync<HashSet<string>>("return new Set(['a', 'b']);", options: null, XunitCancellationToken);

        Assert.NotNull(numbers);
        Assert.True(double.IsNaN(numbers[0]));
        Assert.True(double.IsPositiveInfinity(numbers[1]));
        Assert.True(double.IsNegativeInfinity(numbers[2]));
        Assert.True(double.IsNegative(numbers[3]) && numbers[3] == 0);
        Assert.Equal(1.5, numbers[4]);
        Assert.Equal([1, 2], bytes);
        Assert.Equal(new Dictionary<int, string> { [1] = "a", [2] = "b" }, map);
        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "a", "b" }, set);
    }

    [Fact]
    public async Task Evaluate_UnsupportedMapKeys_Throws()
    {
        await using var node = await StartNodeAsync();

        var objectKey = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("return new Map([[{}, 1]]);", XunitCancellationToken));
        var duplicateKey = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("return new Map([[1, 'a'], ['1', 'b']]);", XunitCancellationToken));

        Assert.Equal("TypeError", objectKey.JavaScriptErrorName);
        Assert.Equal("TypeError", duplicateKey.JavaScriptErrorName);
    }

    [Fact]
    public async Task Invoke_UrlSchemes()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var modulePath = temporaryDirectory.CreateTextFile("module.mjs", "export const value = 42;");
        await using var node = await StartNodeAsync();

        var fileUrl = await node.InvokeAsync(new Uri(modulePath).AbsoluteUri, "value", arguments: null, XunitCancellationToken);
        var builtIn = await node.InvokeAsync("node:path", "basename", ["/a/b"], XunitCancellationToken);
        var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.InvokeAsync("data:text/javascript,export const value = 1;", "value", arguments: null, XunitCancellationToken));

        Assert.Equal(42, fileUrl.GetInt32());
        Assert.Equal("b", builtIn.GetString());
        Assert.Contains("unsupported URL scheme 'data:'", exception.Message);
    }

    [Fact]
    public async Task CodeCompilingMembers_Throws()
    {
        await using var node = await StartNodeAsync();
        await using var function = await node.EvaluateReferenceAsync("return function () { };", XunitCancellationToken);
        await using var obj = await node.EvaluateReferenceAsync("return { constructor: () => 42 };", XunitCancellationToken);
        await using var functionConstructor = await node.EvaluateReferenceAsync("return Function;", XunitCancellationToken);

        var inheritedConstructor = await Assert.ThrowsAsync<NodeJsException>(() => function.InvokeAsync("constructor", ["return 1;"], XunitCancellationToken));
        var prototype = await Assert.ThrowsAsync<NodeJsException>(() => function.InvokeReferenceAsync("__proto__", arguments: null, XunitCancellationToken));
        var exportConstructor = await Assert.ThrowsAsync<NodeJsException>(() => node.InvokeAsync("node:path", "constructor", ["return 1;"], XunitCancellationToken));
        var callFunctionConstructor = await Assert.ThrowsAsync<NodeJsException>(() => functionConstructor.InvokeAsync(methodName: null, ["return 1;"], XunitCancellationToken));
        var ownConstructor = await obj.InvokeAsync("constructor", arguments: null, XunitCancellationToken);

        Assert.Contains("does not have a member 'constructor'", inheritedConstructor.Message);
        Assert.Contains("does not have a member '__proto__'", prototype.Message);
        Assert.Contains("does not export 'constructor'", exportConstructor.Message);
        Assert.Equal("TypeError", callFunctionConstructor.JavaScriptErrorName);
        Assert.Equal(42, ownConstructor.GetInt32());
    }

    [Fact]
    public async Task MaxConcurrentCalls_LimitsCallsInProgress()
    {
        await using var node = await StartNodeAsync(new NodeJsHostOptions { MaxConcurrentCalls = 2 });
        const string Code = """
            globalThis.running = (globalThis.running ?? 0) + 1;
            globalThis.max = Math.max(globalThis.max ?? 0, globalThis.running);
            await new Promise(r => setTimeout(r, 10));
            globalThis.running--;
            """;

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => node.EvaluateVoidAsync(Code, XunitCancellationToken)));
        var max = (await node.EvaluateAsync("return globalThis.max;", XunitCancellationToken)).GetInt32();

        Assert.True(max is 1 or 2, $"Unexpected number of concurrent calls: {max}");
    }

    [Fact]
    public async Task MaxConcurrentCalls_Invalid_Throws()
    {
        SkipIfNodeIsNotInstalled();
        var options = new NodeJsHostOptions { MaxConcurrentCalls = 0 };

        var hostException = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => NodeJsHost.StartAsync(options, XunitCancellationToken));
        var poolException = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => NodeJsHostPool.StartAsync(2, options, XunitCancellationToken));

        Assert.Equal("options", hostException.ParamName);
        Assert.Equal("options", poolException.ParamName);
    }

    [Fact]
    public async Task Pool_MaxConcurrentCalls_UsesAvailableHost()
    {
        SkipIfNodeIsNotInstalled();
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var releasePath = temporaryDirectory.GetFullPath("release.txt");
        await using var pool = await NodeJsHostPool.StartAsync(2, new NodeJsHostOptions { MaxConcurrentCalls = 1 }, XunitCancellationToken);

        // The long call keeps its host busy until the short calls complete, so a short call sent to that host would never complete
        var longCall = pool.EvaluateAsync("""
            const fs = require('node:fs');
            while (!fs.existsSync(args[0])) await new Promise(r => setTimeout(r, 10));
            return process.pid;
            """, [(string)releasePath], XunitCancellationToken);
        var shortCalls = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => pool.EvaluateAsync("return process.pid;", XunitCancellationToken))).WaitAsync(TimeSpan.FromMinutes(1), XunitCancellationToken);
        await File.WriteAllTextAsync(releasePath, "", XunitCancellationToken);
        var longCallProcessId = (await longCall).GetInt32();

        Assert.All(shortCalls, result => result.GetInt32() != longCallProcessId);
    }

    [Fact]
    public async Task Pool_RunAsync_CallsMadeOnThePool_DoNotWaitForCapacity()
    {
        SkipIfNodeIsNotInstalled();
        await using var pool = await NodeJsHostPool.StartAsync(2, new NodeJsHostOptions { MaxConcurrentCalls = 1 }, XunitCancellationToken);
        var runningCallbacks = 0;
        var allRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Each callback holds one of the 2 slots of the pool while it calls the pool
        var callbacks = Enumerable.Range(0, 2).Select(_ => pool.RunAsync(async host =>
        {
            if (Interlocked.Increment(ref runningCallbacks) is 2)
            {
                allRunning.SetResult();
            }

            await allRunning.Task;
            var result = await pool.EvaluateAsync("return 1;", XunitCancellationToken);
            return result.GetInt32();
        }, XunitCancellationToken)).ToArray();
        var results = await Task.WhenAll(callbacks).WaitAsync(TimeSpan.FromMinutes(1), XunitCancellationToken);

        Assert.All(results, result => Assert.Equal(1, result));
    }

    [Fact]
    public async Task Evaluate_ErrorWithLoneSurrogates_Throws()
    {
        await using var node = await StartNodeAsync();

        var error = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("throw new Error('a\\ud83d');", XunitCancellationToken));
        var nonError = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("throw '\\udc00b';", XunitCancellationToken));
        var withoutToWellFormed = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("delete String.prototype.toWellFormed; throw new Error('\\ud83d\\ude00\\ud83d-\\ude00');", XunitCancellationToken));
        var result = await node.EvaluateAsync("return 1;", XunitCancellationToken);

        Assert.Equal("Error: a\uFFFD", error.Message);
        Assert.Contains("a\uFFFD", error.JavaScriptStack);
        Assert.Equal("string: \uFFFDb", nonError.Message);
        Assert.Equal("Error: \U0001F600\uFFFD-\uFFFD", withoutToWellFormed.Message);
        Assert.Equal(1, result.GetInt32());
    }

    [Fact]
    public async Task Evaluate_ResultWithLoneSurrogates()
    {
        await using var node = await StartNodeAsync();

        var result = await node.EvaluateAsync("""
            const emoji = '\u{1F600}';
            return {
                cut: emoji.slice(0, 1),
                low: emoji.slice(1),
                valid: emoji,
                escapedBackslash: '\\ud83d',
                mixed: '\\' + emoji.slice(0, 1) + '\\\\' + emoji.slice(1),
                ['key' + emoji.slice(0, 1)]: 1,
                map: new Map([['map' + emoji.slice(1), 2]]),
            };
            """, XunitCancellationToken);
        var typed = await node.EvaluateAsync("return 'a' + '\\u{1F600}'.slice(0, 1);", NodeJsTestJsonContext.Default.String, XunitCancellationToken);

        Assert.Equal("\uFFFD", result.GetProperty("cut").GetString());
        Assert.Equal("\uFFFD", result.GetProperty("low").GetString());
        Assert.Equal("\U0001F600", result.GetProperty("valid").GetString());
        Assert.Equal("\\ud83d", result.GetProperty("escapedBackslash").GetString());
        Assert.Equal("\\\uFFFD\\\\\uFFFD", result.GetProperty("mixed").GetString());
        Assert.Equal(1, result.GetProperty("key\uFFFD").GetInt32());
        Assert.Equal(2, result.GetProperty("map").GetProperty("map\uFFFD").GetInt32());
        Assert.Equal("a\uFFFD", typed);
    }

    [Fact]
    public async Task Evaluate_RawJsonArgumentWithLineBreaks()
    {
        await using var node = await StartNodeAsync();
        var argument = JsonValue.Create(new RawJson("{\n  \"text\": \"a\\nb\",\n  \"list\": [1,\r\n 2]\n}"), NodeJsTestJsonContext.Default.RawJson);

        var result = await node.EvaluateAsync("return [args[0], typeof args[1]];", [argument, JSValue.Undefined], XunitCancellationToken);
        var next = await node.EvaluateAsync("return 1;", XunitCancellationToken);

        Assert.Equal("a\nb", result[0].GetProperty("text").GetString());
        Assert.Equal(2, result[0].GetProperty("list").GetArrayLength());
        Assert.Equal("undefined", result[1].GetString());
        Assert.Equal(1, next.GetInt32());
    }

    [Fact]
    public async Task UnresponsiveTimeout_KillsBlockedProcess()
    {
        var output = new ConcurrentQueue<string>();
        await using var node = await StartNodeAsync(new NodeJsHostOptions { UnresponsiveTimeout = TimeSpan.FromMilliseconds(500), StandardOutputReceived = output.Enqueue });
        using var process = Process.GetProcessById(node.ProcessId);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        var pending = node.EvaluateAsync("await new Promise(r => setTimeout(r, 60_000));", XunitCancellationToken);

        var blocking = node.EvaluateVoidAsync(BlockingCode, cts.Token);
        await WaitUntilAsync(() => output.Contains("blocked"));
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocking);
        await node.WaitForResponsivenessCheckAsync();
        await WaitUntilAsync(() => process.HasExited);

        var pendingException = await Assert.ThrowsAsync<NodeJsException>(() => pending);
        var nextException = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("return 1;", XunitCancellationToken));
        Assert.Contains("a canceled call blocked its event loop", pendingException.Message);
        Assert.Contains("a canceled call blocked its event loop", nextException.Message);
    }

    [Fact]
    public async Task UnresponsiveTimeout_DoesNotKillResponsiveProcess()
    {
        await using var node = await StartNodeAsync(new NodeJsHostOptions { UnresponsiveTimeout = TimeSpan.FromMinutes(1) });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);

        var canceled = node.EvaluateAsync("await new Promise(r => setTimeout(r, 60_000));", cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        await node.WaitForResponsivenessCheckAsync();
        var result = await node.EvaluateAsync("return 1;", XunitCancellationToken);

        Assert.Equal(1, result.GetInt32());
    }

    [Fact]
    public async Task UnresponsiveTimeout_DoesNotKillProcessBusyWithCallsThatAreNotCanceled()
    {
        var output = new ConcurrentQueue<string>();
        await using var node = await StartNodeAsync(new NodeJsHostOptions { UnresponsiveTimeout = TimeSpan.FromMilliseconds(500), StandardOutputReceived = output.Enqueue });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);

        // The canceled call only awaits a timer, while the calls that are not canceled keep the event loop busy for longer than the timeout
        var canceled = node.EvaluateVoidAsync("require('node:fs').writeSync(1, 'waiting\\n'); await new Promise(r => setTimeout(r, 60_000));", cts.Token);
        await WaitUntilAsync(() => output.Contains("waiting"));
        var busy = Enumerable.Range(0, 3).Select(_ => node.EvaluateAsync("require('node:fs').writeSync(1, 'busy\\n'); const end = Date.now() + 1000; while (Date.now() < end) { } return 1;", XunitCancellationToken)).ToArray();
        await WaitUntilAsync(() => output.Contains("busy"));
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        var results = await Task.WhenAll(busy);
        await node.WaitForResponsivenessCheckAsync();
        var next = await node.EvaluateAsync("return 1;", XunitCancellationToken);

        Assert.All(results, result => Assert.Equal(1, result.GetInt32()));
        Assert.Equal(1, next.GetInt32());
    }

    [Fact]
    public async Task UnresponsiveTimeout_KillsProcessBlockedLaterByCanceledCall()
    {
        var output = new ConcurrentQueue<string>();
        await using var node = await StartNodeAsync(new NodeJsHostOptions { UnresponsiveTimeout = TimeSpan.FromMilliseconds(500), StandardOutputReceived = output.Enqueue });
        using var process = Process.GetProcessById(node.ProcessId);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);

        // The event loop responds when the call is canceled, and is blocked once the timer completes
        var canceled = node.EvaluateVoidAsync("require('node:fs').writeSync(1, 'waiting\\n'); await new Promise(r => setTimeout(r, 200)); " + BlockingCode, cts.Token);
        await WaitUntilAsync(() => output.Contains("waiting"));
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        await WaitUntilAsync(() => output.Contains("blocked"));
        await WaitUntilAsync(() => process.HasExited);

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("return 1;", XunitCancellationToken));
        Assert.Contains("a canceled call blocked its event loop", exception.Message);
    }

    [Fact]
    public async Task UnresponsiveTimeout_KillsProcessBlockedByMicrotasksOfCanceledCall()
    {
        var output = new ConcurrentQueue<string>();
        await using var node = await StartNodeAsync(new NodeJsHostOptions { UnresponsiveTimeout = TimeSpan.FromMilliseconds(500), StandardOutputReceived = output.Enqueue });
        using var process = Process.GetProcessById(node.ProcessId);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);

        // Promises that are already resolved never let the event loop run other callbacks
        var canceled = node.EvaluateVoidAsync("require('node:fs').writeSync(1, 'blocked\\n'); while (true) { await null; }", cts.Token);
        await WaitUntilAsync(() => output.Contains("blocked"));
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        await WaitUntilAsync(() => process.HasExited);

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("return 1;", XunitCancellationToken));
        Assert.Contains("a canceled call blocked its event loop", exception.Message);
    }

    [Fact]
    public async Task UnresponsiveTimeout_DoesNotKillProcessRunningCallsInResourcesCreatedByCanceledCall()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();

        // The jobs run in a recursive timeout started when the module is loaded, so by the call that loads it first
        temporaryDirectory.CreateTextFile("queue.mjs", """
            import fs from "node:fs";
            const jobs = [];
            function poll() {
                while (jobs.length > 0) jobs.shift()();
                setTimeout(poll, 10);
            }

            setTimeout(poll, 10);
            export function hang() {
                fs.writeSync(1, "hanging\n");
                return new Promise(() => { });
            }

            export function busy(milliseconds) {
                return new Promise(resolve => jobs.push(() => {
                    const end = Date.now() + milliseconds;
                    while (Date.now() < end) { }
                    resolve(1);
                }));
            }
            """);
        var output = new ConcurrentQueue<string>();
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkingDirectory = temporaryDirectory.FullPath, UnresponsiveTimeout = TimeSpan.FromMilliseconds(500), StandardOutputReceived = output.Enqueue });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);

        var loadingCall = node.InvokeVoidAsync("./queue.mjs", "hang", arguments: null, cts.Token);
        await WaitUntilAsync(() => output.Contains("hanging"));

        // The jobs run in an interval created by a call
        var intervalCall = node.EvaluateVoidAsync("""
            globalThis.intervalJobs = [];
            setInterval(() => { while (intervalJobs.length > 0) intervalJobs.shift()(); }, 10);
            require('node:fs').writeSync(1, 'interval\n');
            await new Promise(() => { });
            """, cts.Token);
        await WaitUntilAsync(() => output.Contains("interval"));
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loadingCall);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => intervalCall);

        // The jobs of calls that are not canceled block the event loop for longer than the timeout
        var moduleJob = await node.InvokeAsync("./queue.mjs", "busy", [1500], XunitCancellationToken);
        var intervalJob = await node.EvaluateAsync("""
            return await new Promise(resolve => intervalJobs.push(() => {
                const end = Date.now() + 1500;
                while (Date.now() < end) { }
                resolve(2);
            }));
            """, XunitCancellationToken);
        await node.WaitForResponsivenessCheckAsync();
        var next = await node.EvaluateAsync("return 3;", XunitCancellationToken);

        Assert.Equal(1, moduleJob.GetInt32());
        Assert.Equal(2, intervalJob.GetInt32());
        Assert.Equal(3, next.GetInt32());
    }

    [Fact]
    public async Task UnresponsiveTimeout_DoesNotKillProcessWhenResponsesAreSlowToRead()
    {
        var output = new ConcurrentQueue<string>();
        await using var node = await StartNodeAsync(new NodeJsHostOptions { UnresponsiveTimeout = TimeSpan.FromMilliseconds(500), StandardOutputReceived = output.Enqueue });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);

        // The canceled call keeps the event loop busy, but lets it run other callbacks every 20 ms
        var canceled = node.EvaluateVoidAsync("require('node:fs').writeSync(1, 'cooperative\\n'); while (true) { const end = Date.now() + 20; while (Date.now() < end) { } await new Promise(r => setImmediate(r)); }", cts.Token);
        await WaitUntilAsync(() => output.Contains("cooperative"));
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);

        // Reading this result delays the reading of all the responses for longer than the timeout
        var slowOptions = new JsonSerializerOptions { Converters = { new SlowInt32Converter(TimeSpan.FromSeconds(2)) }, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true };
        var slow = await node.EvaluateAsync<int>("return 1;", slowOptions, XunitCancellationToken);
        var next = await node.EvaluateAsync("return 2;", XunitCancellationToken);

        Assert.Equal(1, slow);
        Assert.Equal(2, next.GetInt32());
    }

    [Fact]
    public async Task Options_ChangedAfterStart_AreIgnored()
    {
        var options = new NodeJsHostOptions { UnresponsiveTimeout = TimeSpan.FromMinutes(1) };
        await using var node = await StartNodeAsync(options);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);

        // With a timeout of zero, the process would be killed as soon as a call is canceled
        options.UnresponsiveTimeout = TimeSpan.Zero;
        var canceled = node.EvaluateAsync("await new Promise(r => setTimeout(r, 60_000));", cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        await node.WaitForResponsivenessCheckAsync();
        var result = await node.EvaluateAsync("return 1;", XunitCancellationToken);

        Assert.Equal(1, result.GetInt32());
    }

    [Fact]
    public async Task Pool_OptionsChangedAfterStart_AreIgnoredByReplacedProcess()
    {
        SkipIfNodeIsNotInstalled();
        var options = new NodeJsHostOptions();
        options.EnvironmentVariables["MEZIANTOU_TEST"] = "a";
        await using var pool = await NodeJsHostPool.StartAsync(1, options, XunitCancellationToken);
        var processId = (await pool.EvaluateAsync("return process.pid;", XunitCancellationToken)).GetInt32();

        options.EnvironmentVariables["MEZIANTOU_TEST"] = "b";
        await Assert.ThrowsAsync<NodeJsException>(() => pool.EvaluateVoidAsync("process.exit(0);", XunitCancellationToken));
        var result = await pool.EvaluateAsync("return [process.pid, process.env.MEZIANTOU_TEST];", XunitCancellationToken);

        Assert.NotEqual(processId, result[0].GetInt32());
        Assert.Equal("a", result[1].GetString());
    }

    [Fact]
    public void Options_Clone_CopiesAllProperties()
    {
        Action<string> standardOutput = _ => { };
        Action<string> standardError = _ => { };
        var options = new NodeJsHostOptions
        {
            NodeExecutablePath = "node-path",
            WorkingDirectory = "working-directory",
            StartupTimeout = TimeSpan.FromSeconds(1),
            MaxConcurrentCalls = 2,
            UnresponsiveTimeout = TimeSpan.FromSeconds(3),
            WorkerThreads = 4,
            StandardOutputReceived = standardOutput,
            StandardErrorReceived = standardError,
        };
        options.NodeArguments.Add("--a");
        options.EnvironmentVariables["A"] = "1";
        options.EnvironmentVariables["B"] = null;

        var clone = options.Clone();
        options.NodeArguments.Add("--b");
        options.EnvironmentVariables["C"] = "3";

        Assert.Equal("node-path", clone.NodeExecutablePath);
        Assert.Equal("working-directory", clone.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(1), clone.StartupTimeout);
        Assert.Equal(2, clone.MaxConcurrentCalls);
        Assert.Equal(TimeSpan.FromSeconds(3), clone.UnresponsiveTimeout);
        Assert.Equal(4, clone.WorkerThreads);
        Assert.Same(standardOutput, clone.StandardOutputReceived);
        Assert.Same(standardError, clone.StandardErrorReceived);
        Assert.Equal(["--a"], clone.NodeArguments);
        Assert.HasCount(2, clone.EnvironmentVariables);
        Assert.Equal("1", clone.EnvironmentVariables["A"]);
        Assert.Null(clone.EnvironmentVariables["B"]);

        // Fails when a property is added without being copied by Clone
        Assert.HasCount(10, typeof(NodeJsHostOptions).GetProperties());
    }

    [Fact]
    public async Task StartupTimeout_Invalid_Throws()
    {
        SkipIfNodeIsNotInstalled();

        var zero = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => NodeJsHost.StartAsync(new NodeJsHostOptions { StartupTimeout = TimeSpan.Zero }, XunitCancellationToken));
        var negative = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => NodeJsHostPool.StartAsync(2, new NodeJsHostOptions { StartupTimeout = TimeSpan.FromSeconds(-1) }, XunitCancellationToken));
        await using var infinite = await NodeJsHost.StartAsync(new NodeJsHostOptions { StartupTimeout = Timeout.InfiniteTimeSpan }, XunitCancellationToken);

        Assert.Equal("options", zero.ParamName);
        Assert.Equal("options", negative.ParamName);
        Assert.Equal(1, (await infinite.EvaluateAsync("return 1;", XunitCancellationToken)).GetInt32());
    }

    [Fact]
    public async Task UnresponsiveTimeout_Invalid_Throws()
    {
        SkipIfNodeIsNotInstalled();

        var zero = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => NodeJsHost.StartAsync(new NodeJsHostOptions { UnresponsiveTimeout = TimeSpan.Zero }, XunitCancellationToken));
        var tooLarge = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => NodeJsHostPool.StartAsync(2, new NodeJsHostOptions { UnresponsiveTimeout = TimeSpan.FromDays(30) }, XunitCancellationToken));

        Assert.Equal("options", zero.ParamName);
        Assert.Equal("options", tooLarge.ParamName);
    }

    [Fact]
    public async Task Pool_BlockedByCanceledCall_IsAvoided()
    {
        SkipIfNodeIsNotInstalled();
        var output = new ConcurrentQueue<string>();
        await using var pool = await NodeJsHostPool.StartAsync(2, new NodeJsHostOptions { MaxConcurrentCalls = 1, StandardOutputReceived = output.Enqueue }, XunitCancellationToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        var blockedProcessId = 0;

        var blocking = pool.RunAsync(async host =>
        {
            blockedProcessId = host.ProcessId;
            await host.EvaluateVoidAsync(BlockingCode, cts.Token);
        }, XunitCancellationToken);
        await WaitUntilAsync(() => output.Contains("blocked"));
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocking);

        // Without UnresponsiveTimeout, the blocked process is kept, but calls are sent to the other one
        var processIds = new List<int>();
        for (var i = 0; i < 10; i++)
        {
            var result = await pool.EvaluateAsync("return process.pid;", XunitCancellationToken).WaitAsync(TimeSpan.FromMinutes(1), XunitCancellationToken);
            processIds.Add(result.GetInt32());
        }

        Assert.All(processIds, processId => processId != blockedProcessId);
    }

    [Fact]
    public async Task Pool_UnresponsiveTimeout_ReplacesBlockedProcess()
    {
        SkipIfNodeIsNotInstalled();
        var output = new ConcurrentQueue<string>();
        await using var pool = await NodeJsHostPool.StartAsync(1, new NodeJsHostOptions { UnresponsiveTimeout = TimeSpan.FromMilliseconds(500), StandardOutputReceived = output.Enqueue }, XunitCancellationToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        NodeJsHost? blockedHost = null;
        var blockedProcessId = 0;

        var blocking = pool.RunAsync(async host =>
        {
            blockedHost = host;
            blockedProcessId = host.ProcessId;
            await host.EvaluateVoidAsync(BlockingCode, cts.Token);
        }, XunitCancellationToken);
        await WaitUntilAsync(() => output.Contains("blocked"));
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocking);
        await blockedHost!.WaitForResponsivenessCheckAsync();
        var processId = (await pool.EvaluateAsync("return process.pid;", XunitCancellationToken)).GetInt32();

        Assert.NotEqual(blockedProcessId, processId);
    }

    [Fact]
    public async Task WorkerThreads_RunsCpuBoundCallsInParallel()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var node = await StartNodeAsync(new NodeJsHostOptions { WorkerThreads = 2 });

        // Each call blocks its event loop until both calls have started, which is only possible when they run on different threads
        var code = CreateBarrierCode(temporaryDirectory.GetFullPath("barrier.txt"), count: 2, "[process.pid, require('node:worker_threads').threadId, require('node:worker_threads').isMainThread]");
        var results = await Task.WhenAll(node.EvaluateAsync(code, XunitCancellationToken), node.EvaluateAsync(code, XunitCancellationToken));

        Assert.All(results, result => Assert.Equal(node.ProcessId, result[0].GetInt32()));
        Assert.All(results, result => Assert.False(result[2].GetBoolean()));
        Assert.NotEqual(results[0][1].GetInt32(), results[1][1].GetInt32());

        using var process = Process.GetProcessById(node.ProcessId);
        await node.DisposeAsync();
        Assert.True(process.HasExited);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => node.EvaluateAsync("return 1;", XunitCancellationToken));
    }

    [Fact]
    public async Task WorkerThreads_StateIsPerWorkerThread()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkerThreads = 2 });
        var code = CreateBarrierCode(temporaryDirectory.GetFullPath("barrier.txt"), count: 2, "globalThis.marker = require('node:worker_threads').threadId");
        await Task.WhenAll(node.EvaluateAsync(code, XunitCancellationToken), node.EvaluateAsync(code, XunitCancellationToken));

        var threadIds = new HashSet<int>();
        for (var i = 0; i < 10; i++)
        {
            var result = await node.EvaluateAsync("return [require('node:worker_threads').threadId, globalThis.marker];", XunitCancellationToken);
            Assert.Equal(result[0].GetInt32(), result[1].GetInt32());
            threadIds.Add(result[0].GetInt32());
        }

        Assert.HasCount(2, threadIds);
    }

    [Fact]
    public async Task WorkerThreads_Reference_RunsOnItsWorkerThread()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkerThreads = 2 });
        var (first, second) = await CreateReferencePerWorkerThreadAsync(node, temporaryDirectory.GetFullPath("barrier.txt"));
        await using var firstReference = first;
        await using var secondReference = second;

        foreach (var reference in new[] { first, second })
        {
            var threadId = (await reference.GetValueAsync(XunitCancellationToken)).GetProperty("threadId").GetInt32();
            for (var i = 0; i < 5; i++)
            {
                Assert.Equal(threadId, (await node.EvaluateAsync("return [args[0].threadId, require('node:worker_threads').threadId];", [reference], XunitCancellationToken))[1].GetInt32());
                Assert.Equal(threadId, (await node.EvaluateAsync("return require('node:worker_threads').threadId;", [new JsonObject { ["value"] = reference }], XunitCancellationToken)).GetInt32());
                Assert.Equal(threadId, (await reference.InvokeAsync("currentThreadId", cancellationToken: XunitCancellationToken)).GetInt32());

                await using var self = await reference.InvokeReferenceAsync("self", cancellationToken: XunitCancellationToken);
                Assert.Equal(threadId, (await self.InvokeAsync("currentThreadId", cancellationToken: XunitCancellationToken)).GetInt32());

                await using var instance = await reference.CreateInstanceAsync("Child", cancellationToken: XunitCancellationToken);
                Assert.Equal(threadId, (await instance.GetValueAsync(XunitCancellationToken)).GetProperty("threadId").GetInt32());
            }
        }

        // The value is released on the worker thread that keeps it
        var count = await GetReferenceCountAsync(node, first);
        var extra = await first.InvokeReferenceAsync("self", cancellationToken: XunitCancellationToken);
        Assert.Equal(count + 1, await GetReferenceCountAsync(node, first));
        await extra.DisposeAsync();
        await WaitUntilAsync(async () => await GetReferenceCountAsync(node, first) == count);
    }

    [Fact]
    public async Task WorkerThreads_ReferencesOfDifferentWorkerThreads_Throws()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkerThreads = 2 });
        var (first, second) = await CreateReferencePerWorkerThreadAsync(node, temporaryDirectory.GetFullPath("barrier.txt"));
        await using var firstReference = first;
        await using var secondReference = second;
        await using var otherNode = await StartNodeAsync();
        await using var otherReference = await otherNode.EvaluateReferenceAsync("return {};", XunitCancellationToken);

        var arguments = await Assert.ThrowsAsync<ArgumentException>(() => node.EvaluateAsync("return 1;", [first, new JsonObject { ["value"] = second }], XunitCancellationToken));
        var target = await Assert.ThrowsAsync<ArgumentException>(() => first.InvokeAsync("currentThreadId", [second], XunitCancellationToken));
        var otherProcess = await Assert.ThrowsAsync<ArgumentException>(() => node.EvaluateAsync("return 1;", [otherReference], XunitCancellationToken));

        Assert.Equal("arguments", arguments.ParamName);
        Assert.Equal("arguments", target.ParamName);
        Assert.Equal("arguments", otherProcess.ParamName);
        Assert.Contains("different worker threads", arguments.Message);
        Assert.Contains("another Node.js process", otherProcess.Message);
    }

    [Fact]
    public async Task WorkerThreads_UnresponsiveTimeout_TerminatesOnlyBlockedWorkerThread()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var output = new ConcurrentQueue<string>();
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkerThreads = 2, UnresponsiveTimeout = TimeSpan.FromMilliseconds(500), StandardOutputReceived = output.Enqueue });
        using var process = Process.GetProcessById(node.ProcessId);
        var (blocked, other) = await CreateReferencePerWorkerThreadAsync(node, temporaryDirectory.GetFullPath("barrier.txt"));
        await using var blockedReference = blocked;
        await using var otherReference = other;
        var otherThreadId = (await other.GetValueAsync(XunitCancellationToken)).GetProperty("threadId").GetInt32();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);

        var blocking = node.EvaluateVoidAsync(BlockingCode, [blocked], cts.Token);
        await WaitUntilAsync(() => output.Contains("blocked"));
        var running = node.EvaluateAsync("await new Promise(r => setTimeout(r, 1000)); return require('node:worker_threads').threadId;", XunitCancellationToken);
        var queued = blocked.GetValueAsync(XunitCancellationToken);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocking);
        await node.WaitForResponsivenessCheckAsync();

        var queuedException = await Assert.ThrowsAsync<NodeJsException>(() => queued);
        Assert.Contains("a canceled call blocked its event loop", queuedException.Message);
        Assert.Null(queuedException.ExitCode);
        Assert.Equal(otherThreadId, (await running).GetInt32());
        Assert.Equal(otherThreadId, (await other.GetValueAsync(XunitCancellationToken)).GetProperty("threadId").GetInt32());
        var referenceException = await Assert.ThrowsAsync<NodeJsException>(() => blocked.GetValueAsync(XunitCancellationToken));
        Assert.Contains("a canceled call blocked its event loop", referenceException.Message);

        // The worker thread is restarted, so calls run on two worker threads again
        await WaitUntilAsync(async () => (await node.EvaluateAsync("return require('node:worker_threads').threadId;", XunitCancellationToken)).GetInt32() != otherThreadId);
        Assert.False(process.HasExited);
        Assert.Equal(process.Id, node.ProcessId);
    }

    [Fact]
    public async Task WorkerThreads_UnresponsiveTimeout_DoesNotTerminateWorkerThreadBusyWithCallsThatAreNotCanceled()
    {
        var output = new ConcurrentQueue<string>();
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkerThreads = 1, UnresponsiveTimeout = TimeSpan.FromMilliseconds(500), StandardOutputReceived = output.Enqueue });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        var threadId = (await node.EvaluateAsync("return require('node:worker_threads').threadId;", XunitCancellationToken)).GetInt32();

        // The canceled call only awaits a timer, while the calls that are not canceled keep the event loop busy for longer than the timeout
        var canceled = node.EvaluateVoidAsync("require('node:fs').writeSync(1, 'waiting\\n'); await new Promise(r => setTimeout(r, 60_000));", cts.Token);
        await WaitUntilAsync(() => output.Contains("waiting"));
        var busy = Enumerable.Range(0, 3).Select(_ => node.EvaluateAsync("require('node:fs').writeSync(1, 'busy\\n'); const end = Date.now() + 1000; while (Date.now() < end) { } return 1;", XunitCancellationToken)).ToArray();
        await WaitUntilAsync(() => output.Contains("busy"));
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        var results = await Task.WhenAll(busy);
        await node.WaitForResponsivenessCheckAsync();
        var next = await node.EvaluateAsync("return require('node:worker_threads').threadId;", XunitCancellationToken);

        Assert.All(results, result => Assert.Equal(1, result.GetInt32()));
        Assert.Equal(threadId, next.GetInt32());
    }

    [Fact]
    public async Task WorkerThreads_WorkerThreadExit_FailsItsCallsAndRestartsIt()
    {
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkerThreads = 1 });
        using var process = Process.GetProcessById(node.ProcessId);
        var threadId = (await node.EvaluateAsync("globalThis.value = 1; return require('node:worker_threads').threadId;", XunitCancellationToken)).GetInt32();
        await using var reference = await node.EvaluateReferenceAsync("return {};", XunitCancellationToken);

        var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateVoidAsync("process.exit(5);", XunitCancellationToken));
        var result = await node.EvaluateAsync("return [typeof globalThis.value, require('node:worker_threads').threadId];", XunitCancellationToken);
        var referenceException = await Assert.ThrowsAsync<NodeJsException>(() => reference.GetValueAsync(XunitCancellationToken));

        Assert.Contains("worker thread exited unexpectedly with exit code 5", exception.Message);
        Assert.Null(exception.ExitCode);
        Assert.Equal("undefined", result[0].GetString());
        Assert.NotEqual(threadId, result[1].GetInt32());
        Assert.Contains("exit code 5", referenceException.Message);
        Assert.False(process.HasExited);
    }

    [Fact]
    public async Task WorkerThreads_MaxConcurrentCalls_UsesAvailableWorkerThread()
    {
        await using var node = await StartNodeAsync(new NodeJsHostOptions { WorkerThreads = 2, MaxConcurrentCalls = 2 });

        // The long call keeps a worker thread busy, so the following calls run on the other one
        var longCall = node.EvaluateAsync("const end = Date.now() + 3000; while (Date.now() < end) { } return require('node:worker_threads').threadId;", XunitCancellationToken);
        var threadIds = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            threadIds.Add((await node.EvaluateAsync("return require('node:worker_threads').threadId;", XunitCancellationToken)).GetInt32());
        }

        var longCallThreadId = (await longCall).GetInt32();
        Assert.All(threadIds, threadId => Assert.NotEqual(longCallThreadId, threadId));
    }

    [Fact]
    public async Task WorkerThreads_Pool()
    {
        SkipIfNodeIsNotInstalled();
        await using var temporaryDirectory = TemporaryDirectory.Create();
        await using var pool = await NodeJsHostPool.StartAsync(2, new NodeJsHostOptions { WorkerThreads = 2, MaxConcurrentCalls = 2 }, XunitCancellationToken);

        // 4 calls run at the same time: 2 per process, 1 per worker thread
        var code = CreateBarrierCode(temporaryDirectory.GetFullPath("barrier.txt"), count: 4, "`${process.pid}:${require('node:worker_threads').threadId}`");
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => pool.EvaluateAsync(code, XunitCancellationToken)));
        var threads = results.Select(result => result.GetString()!).ToArray();

        Assert.HasCount(4, threads.Distinct(StringComparer.Ordinal));
        Assert.HasCount(2, threads.Select(thread => thread.Split(':')[0]).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task WorkerThreads_Invalid_Throws()
    {
        SkipIfNodeIsNotInstalled();

        var zero = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => NodeJsHost.StartAsync(new NodeJsHostOptions { WorkerThreads = 0 }, XunitCancellationToken));
        var negative = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => NodeJsHostPool.StartAsync(2, new NodeJsHostOptions { WorkerThreads = -1 }, XunitCancellationToken));

        Assert.Equal("options", zero.ParamName);
        Assert.Equal("options", negative.ParamName);
    }

    // The marker is written synchronously, so it is received even though the event loop is blocked.
    // Waiting for it ensures the call was sent before it is canceled: a call canceled while waiting to be sent never runs.
    private const string BlockingCode = "require('node:fs').writeSync(1, 'blocked\\n'); while (true) { }";

    private const string DescribeModule = """
        const describe = value => ({
            type: value instanceof Date ? "Date" : value instanceof Uint8Array ? value.constructor.name : typeof value,
            text: value instanceof Date ? value.toISOString() : value instanceof Uint8Array ? Array.from(value).join(",") : Object.is(value, -0) ? "-0" : String(value),
        });
        const format = value => typeof value === "bigint" ? `${value}n` : String(value);
        export const describeAll = (...args) => args.map(describe);
        export const types = (...args) => args.map(value => typeof value === "object" ? `object:${typeof value.a}` : typeof value);
        export const describeNested = value => ({
            hasMissing: "missing" in value && value.missing === undefined,
            missing: value.missing,
            list: value.deep.list.map(format).join(","),
            ownProto: Object.getOwnPropertyDescriptor(value, "__proto__")?.value === 5n,
            prototypeUnchanged: Object.getPrototypeOf(value) === Object.prototype,
            text: value.text,
        });
        """;

    private const string CounterModule = """
        export function createCounter(start) {
            return {
                value: start,
                increment(n) { return this.value += n; },
                child() { return { parent: this }; },
            };
        }
        export const makeAdder = n => x => x + n;
        export const read = counter => counter.value;
        export const readNested = options => options.counter.value + options.list[0].value;
        export const isParent = (child, parent) => child.parent === parent;
        """;

    private static async Task<int> GetReferenceCountAsync(NodeJsHost node, JSReference? target = null)
    {
        var information = await node.GetDebugInformationAsync(target, XunitCancellationToken);
        return information.GetProperty("references").GetInt32();
    }

    // Each call blocks its event loop until all the calls have started, then returns the expression.
    // With worker threads, calls are sent to the least busy worker thread, so concurrent calls run on different worker threads.
    private static string CreateBarrierCode(string barrierPath, int count, string expression)
    {
        return $$"""
            const fs = require('node:fs');
            const path = {{JsonSerializer.Serialize(barrierPath)}};
            fs.appendFileSync(path, 'x');
            const deadline = Date.now() + 60_000;
            while (fs.readFileSync(path, 'utf8').length < {{count.ToString(CultureInfo.InvariantCulture)}}) {
                if (Date.now() > deadline) throw new Error('The other calls did not start');
            }
            return {{expression}};
            """;
    }

    // Creates a reference on each of the 2 worker threads of the host
    private static async Task<(JSReference First, JSReference Second)> CreateReferencePerWorkerThreadAsync(NodeJsHost node, string barrierPath)
    {
        var code = CreateBarrierCode(barrierPath, count: 2, """
            {
                threadId: require('node:worker_threads').threadId,
                currentThreadId() { return require('node:worker_threads').threadId; },
                self() { return this; },
                Child: class { constructor() { this.threadId = require('node:worker_threads').threadId; } },
            }
            """);
        var references = await Task.WhenAll(node.EvaluateReferenceAsync(code, XunitCancellationToken), node.EvaluateReferenceAsync(code, XunitCancellationToken));
        var firstThreadId = (await references[0].GetValueAsync(XunitCancellationToken)).GetProperty("threadId").GetInt32();
        var secondThreadId = (await references[1].GetValueAsync(XunitCancellationToken)).GetProperty("threadId").GetInt32();
        Assert.NotEqual(firstThreadId, secondThreadId);
        return (references[0], references[1]);
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

    // Options are copied when a pool starts, so the processes that replace the ones that exit read how to start from a file:
    // "slow" never completes the startup, "fail" exits before connecting, and a missing file starts normally
    private static NodeJsHostOptions CreateStartupModeOptions(string startupModePath)
    {
        var code = $$"""
            import fs from "node:fs";
            const path = {{JsonSerializer.Serialize(startupModePath)}};
            const mode = fs.existsSync(path) ? fs.readFileSync(path, "utf8") : "";
            if (mode === "fail") process.exit(3);
            if (mode === "slow") await new Promise(r => setTimeout(r, 60_000));
            """;
        var options = new NodeJsHostOptions();
        options.NodeArguments.Add("--import");
        options.NodeArguments.Add("data:text/javascript," + Uri.EscapeDataString(code));
        return options;
    }

    // The child process outlives the hang timeout, so a wait for the output to be closed cannot complete before it
    private static async Task<int> StartChildProcessInheritingOutputAsync(NodeJsHost node)
    {
        var result = await node.EvaluateAsync("""
            const { spawn } = require('node:child_process');
            const child = spawn(process.execPath, ['-e', 'setTimeout(() => {}, 600_000)'], { stdio: 'inherit', detached: true });
            child.unref();
            return child.pid;
            """, XunitCancellationToken);
        return result.GetInt32();
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

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.True(stopwatch.Elapsed < OutputTimeout, "Timed out waiting for the condition");
            await Task.Delay(10, XunitCancellationToken);
        }
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

    // Writes the JSON as is, including its whitespace
    [JsonConverter(typeof(RawJsonConverter))]
    private sealed record RawJson(string Json);

    private sealed class RawJsonConverter : JsonConverter<RawJson>
    {
        public override RawJson Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, RawJson value, JsonSerializerOptions options) => writer.WriteRawValue(value.Json);
    }

    // Blocks the thread that reads the responses of the Node.js process while the result is read
    private sealed class SlowInt32Converter(TimeSpan delay) : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Thread.Sleep(delay);
            return reader.GetInt32();
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
    [JsonSerializable(typeof(Person))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(int[]))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(RawJson))]
    private sealed partial class NodeJsTestJsonContext : JsonSerializerContext;
}
