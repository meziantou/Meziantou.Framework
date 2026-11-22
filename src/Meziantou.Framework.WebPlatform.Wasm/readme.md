# Meziantou.Framework.WebPlatform.Wasm

Runs the WebAssembly modules of the scripts of [Meziantou.Framework.WebPlatform](../Meziantou.Framework.WebPlatform/readme.md)
with [Meziantou.Framework.WasmEngine](../Meziantou.Framework.WasmEngine/readme.md): a `WebAssemblyRuntime` for the
WebAssembly JavaScript API (`WasmWebAssemblyRuntime`).

<!-- ref:../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/WebAssemblyTests.cs;region=WebPlatformWasm_RunModules;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmWebAssemblyRuntime(); // can be shared by the browsing contexts
using var engine = new JavaScriptScriptEngine();
using var context = new BrowsingContext(new BrowsingContextOptions
{
    ScriptEngine = engine,
    WebAssemblyRuntime = runtime,
});

var document = await context.NavigateAsync("https://example.com/", _ => """
    <output></output>
    <script>
      // (module (func (export "add") (param i32 i32) (result i32) local.get 0 local.get 1 i32.add))
      const bytes = new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0, 1, 7, 1, 96, 2, 127, 127, 1, 127, 3, 2, 1, 0,
        7, 7, 1, 3, 97, 100, 100, 0, 0, 10, 9, 1, 7, 0, 32, 0, 32, 1, 106, 11]);
      WebAssembly.instantiate(bytes).then(({ instance }) => {
        document.querySelector('output').textContent = instance.exports.add(2, 3);
      });
    </script>
    """);
await context.EventLoop.RunUntilIdleAsync();

Assert.Equal("5", document.QuerySelector("output")!.TextContent);
```
<!-- endref -->

See [scripting.md](../Meziantou.Framework.WebPlatform/docs/scripting.md#webassembly).
