using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.NodeJs.Samples;

// The examples of src/Meziantou.Framework.NodeJs/readme.md. They call the JavaScript modules and npm packages of a
// project that is not in the repository, so they are compiled but not run. The tests of NodeJs cover the same calls.
public static class ReadmeExamples
{
    public static async Task<JsonElement[]> Usage(string userInput)
    {
        #region NodeJs_Usage
        await using var node = await NodeJsHost.StartAsync(new NodeJsHostOptions
        {
            // Module specifiers (relative paths and npm packages) are resolved from this directory
            WorkingDirectory = "path/to/js/project",
        });

        // Evaluate code as the body of an async function
        JsonElement sum = await node.EvaluateAsync("return 1 + 2;");

        // Values stored on globalThis persist across calls, and require is available
        await node.EvaluateAsync("globalThis.path = require('node:path');");

        // Pass values as arguments, available in the args array, instead of inserting them in the code
        JsonElement joined = await node.EvaluateAsync("return path.join(...args);", ["a", userInput]);

        // Call an export of a module (ESM or CommonJS): npm package, relative path, absolute path, or URL
        JsonElement html = await node.InvokeAsync("marked", "parse", ["# Hello"]);
        JsonElement result = await node.InvokeAsync("./scripts/math.mjs", "add", [1, 2]);

        // Call the default export
        JsonElement greeting = await node.InvokeAsync("./greet.mjs", exportName: null, ["World"]);
        #endregion

        return [sum, joined, html, result, greeting];
    }

    public static async Task<Person?[]> TypedResults(NodeJsHost node)
    {
        #region NodeJs_TypedResults
        // Trimming and Native AOT compatible
        Person? person = await node.InvokeAsync("./people.mjs", "get", [42], MyJsonContext.Default.Person);

        // Reflection-based serialization of arguments and results
        Person? samePerson = await node.InvokeAsync<Person>("./people.mjs", "get", [42]);
        #endregion

        return [person, samePerson];
    }

    public static async Task IgnoreResult(NodeJsHost node)
    {
        #region NodeJs_IgnoreResult
        await node.InvokeVoidAsync("./cache.mjs", "warmUp");
        await node.EvaluateVoidAsync("globalThis.server = require('node:http').createServer();");
        #endregion
    }

    public static async Task JavaScriptValues(NodeJsHost node)
    {
        #region NodeJs_JavaScriptValues
        await node.InvokeAsync("./module.mjs", "run", [JSValue.Undefined, JSValue.BigInt(long.MaxValue), new JsonObject { ["date"] = JSValue.Date(DateTimeOffset.UtcNow) }]);
        #endregion
    }

    public static async Task<JsonElement[]> References(NodeJsHost node)
    {
        #region NodeJs_References
        await using var client = await node.InvokeReferenceAsync("./database.mjs", "connect", ["connection-string"]);

        // Call a method of the referenced value, with the value as "this"
        JsonElement rows = await client.InvokeAsync("query", ["SELECT 1"]);

        // Pass the referenced value to a function, directly or nested in a JsonObject or JsonArray argument
        await node.InvokeVoidAsync("./database.mjs", "seed", [client]);

        // Call a referenced function
        await using var add = await node.EvaluateReferenceAsync("return (a, b) => a + b;");
        JsonElement sum = await add.InvokeAsync(methodName: null, [1, 2]);

        // Create an instance of an exported class (new), or of a class that is a member of a referenced value
        await using var parser = await node.CreateInstanceAsync("./parser.mjs", "Parser", [new JsonObject { ["strict"] = true }]);

        // Get the JSON representation of the referenced value
        JsonElement value = await client.GetValueAsync();
        #endregion

        return [rows, sum, value];
    }

    public static async Task<JsonElement[]> Pool(IEnumerable<string> documents)
    {
        #region NodeJs_Pool
        await using var pool = await NodeJsHostPool.StartAsync(Environment.ProcessorCount, new NodeJsHostOptions { WorkingDirectory = "path/to/js/project", MaxConcurrentCalls = 1 });
        var results = await Task.WhenAll(documents.Select(document => pool.InvokeAsync("./render.mjs", "render", [document])));

        // Run several calls on the same process
        await pool.RunAsync(async host =>
        {
            await host.EvaluateAsync("globalThis.cache = new Map();");
            await host.InvokeAsync("./render.mjs", "warmUp");
        });
        #endregion

        return results;
    }

    public static async Task<JsonElement[]> WorkerThreads(IEnumerable<string> documents)
    {
        #region NodeJs_WorkerThreads
        await using var node = await NodeJsHost.StartAsync(new NodeJsHostOptions { WorkerThreads = Environment.ProcessorCount, MaxConcurrentCalls = Environment.ProcessorCount });
        var results = await Task.WhenAll(documents.Select(document => node.InvokeAsync("./render.mjs", "render", [document])));
        #endregion

        return results;
    }
}
