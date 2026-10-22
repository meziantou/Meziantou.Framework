using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Meziantou.Framework.WebPlatform.Wasmtime;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>The WebAssembly JavaScript interface (https://webassembly.github.io/spec/js-api/) on Wasmtime.</summary>
public sealed class WebAssemblyTests : IDisposable
{
    private static readonly WasmtimeRuntime Runtime = new();

    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine;

    public WebAssemblyTests()
        : this(options: null)
    {
    }

    private WebAssemblyTests(TestScriptEngineOptions? options)
    {
        _engine = TestScriptEngines.Create(options);
    }

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    /// <summary>The bytes of a module in the text format, as a JavaScript array literal.</summary>
    private static string Wasm(string text) => "new Uint8Array([" + string.Join(',', WasmtimeRuntime.WatToWasm(text)) + "])";

    // Runs a script whose promise sets the title of the document
    private async Task<string> RunAsync(string script, WebPlatformFeatures? features = null, bool withRuntime = true)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            WebAssemblyRuntime = withRuntime ? Runtime : null,
            Features = features,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => $"<script>({script})().then(r => document.title = String(r), e => document.title = 'error: ' + e);</script>");

        // The modules are compiled on the thread pool
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Empty(_errors);
        return document.Title;
    }

    [Fact]
    public async Task Instantiate_CallsAnExportedFunction()
    {
        var bytes = Wasm("""(module (func (export "add") (param i32 i32) (result i32) (i32.add (local.get 0) (local.get 1))))""");
        var result = await RunAsync($$"""
            async () => {
              const { module, instance } = await WebAssembly.instantiate({{bytes}});
              const add = instance.exports.add;
              return [add(2, 3), add(0x7fffffff, 1), add.name, add.length, typeof add, Object.isFrozen(instance.exports),
                Object.getPrototypeOf(instance.exports), WebAssembly.Module.exports(module).map(e => e.name + ':' + e.kind).join(),
                Object.prototype.toString.call(module), String(WebAssembly)].join('|');
            }
            """);

        Assert.Equal("5|-2147483648|0|2|function|true||add:function|[object WebAssembly.Module]|[object WebAssembly]", result);
    }

    [Fact]
    public async Task Module_IsCompiledSynchronously()
    {
        var bytes = Wasm("""(module (import "env" "f" (func)) (memory (export "m") 1) (@custom "hello" "abc"))""");
        var result = await RunAsync($$"""
            async () => {
              const module = new WebAssembly.Module({{bytes}});
              const imports = WebAssembly.Module.imports(module).map(i => i.module + '.' + i.name + ':' + i.kind).join();
              const sections = WebAssembly.Module.customSections(module, 'hello').map(b => new TextDecoder().decode(b)).join();
              return [imports, sections, WebAssembly.validate({{bytes}}), WebAssembly.validate(new Uint8Array([0, 1, 2]))].join('|');
            }
            """);

        Assert.Equal("env.f:function|abc|true|false", result);
    }

    [Fact]
    public async Task Module_IsAModuleSource()
    {
        var bytes = Wasm("(module)");
        var result = await RunAsync($$"""
            async () => {
              const module = new WebAssembly.Module({{bytes}});
              const AbstractModuleSource = Object.getPrototypeOf(WebAssembly.Module);
              const getToStringTag = Object.getOwnPropertyDescriptor(AbstractModuleSource.prototype, Symbol.toStringTag).get;
              return [AbstractModuleSource.name, typeof globalThis.AbstractModuleSource, Object.getPrototypeOf(WebAssembly.Module.prototype) === AbstractModuleSource.prototype,
                module instanceof AbstractModuleSource, getToStringTag.call(module), getToStringTag.call({})].join('|');
            }
            """);

        Assert.Equal("AbstractModuleSource|undefined|true|true|WebAssembly.Module|", result);
    }

    [Fact]
    public async Task Compile_RejectsAnInvalidModule()
    {
        var result = await RunAsync("""
            async () => {
              let sync, async;
              try { new WebAssembly.Module(new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0, 1])); } catch (e) { sync = e; }
              try { await WebAssembly.compile(new Uint8Array([1, 2, 3])); } catch (e) { async = e; }
              return [sync instanceof WebAssembly.CompileError, sync instanceof Error, sync.name, async instanceof WebAssembly.CompileError,
                Object.prototype.toString.call(sync), WebAssembly.CompileError.length, new WebAssembly.LinkError('x').message, WebAssembly.RuntimeError('y') instanceof WebAssembly.RuntimeError].join();
            }
            """);

        Assert.Equal("true,true,CompileError,true,[object Error],1,x,true", result);
    }

    [Fact]
    public async Task Imports_CallJavaScriptFunctions()
    {
        var bytes = Wasm("""
            (module
              (import "env" "log" (func $log (param i32 i64 f64)))
              (import "env" "pair" (func $pair (result i32 i32)))
              (func (export "run") (result i32)
                (call $log (i32.const 7) (i64.const -5) (f64.const 1.5))
                (call $pair)
                i32.add))
            """);
        var result = await RunAsync($$"""
            async () => {
              const log = [];
              const { instance } = await WebAssembly.instantiate({{bytes}}, { env: { log: (a, b, c) => log.push(a, typeof b, b, c), pair: () => [40, 2] } });
              return instance.exports.run() + ' ' + log.join();
            }
            """);

        Assert.Equal("42 7,bigint,-5,1.5", result);
    }

    [Fact]
    public async Task Instance_ThrowsLinkErrorForAMissingFunction()
    {
        var bytes = Wasm("""(module (import "env" "f" (func)))""");
        var result = await RunAsync($$"""
            async () => {
              const module = new WebAssembly.Module({{bytes}});
              const errors = [];
              try { new WebAssembly.Instance(module, { env: { f: 1 } }); } catch (e) { errors.push(e.name); }
              try { new WebAssembly.Instance(module, {}); } catch (e) { errors.push(e.name); }
              try { new WebAssembly.Instance(module); } catch (e) { errors.push(e.name); }
              return errors.join();
            }
            """);

        Assert.Equal("LinkError,TypeError,TypeError", result);
    }

    [Fact]
    public async Task Memory_SharesItsBytesWithTheArrayBuffer()
    {
        var bytes = Wasm("""
            (module
              (memory (export "memory") 1 3)
              (func (export "load") (param i32) (result i32) (i32.load8_u (local.get 0)))
              (func (export "store") (param i32 i32) (i32.store8 (local.get 0) (local.get 1)))
              (func (export "grow") (param i32) (result i32) (memory.grow (local.get 0))))
            """);
        var result = await RunAsync($$"""
            async () => {
              const { instance } = await WebAssembly.instantiate({{bytes}});
              const { memory, load, store, grow } = instance.exports;
              const buffer = memory.buffer;
              new Uint8Array(buffer)[10] = 42;
              store(11, 7);
              const before = [buffer.byteLength, load(10), new Uint8Array(buffer)[11], memory.buffer === buffer].join();
              const previous = grow(1);
              const afterWasmGrow = [previous, buffer.byteLength, memory.buffer.byteLength, new Uint8Array(memory.buffer)[10]].join();
              const buffer2 = memory.buffer;
              const previous2 = memory.grow(1);
              const afterJsGrow = [previous2, buffer2.byteLength, memory.buffer.byteLength, grow(1)].join();
              let transferError;
              try { memory.buffer.transfer(); } catch (e) { transferError = e.name; }
              return [before, afterWasmGrow, afterJsGrow, transferError].join('|');
            }
            """);

        Assert.Equal("65536,42,7,true|1,0,131072,42|2,0,196608,-1|TypeError", result);
    }

    [Fact]
    public async Task Memory_CanBeImported()
    {
        var bytes = Wasm("""(module (import "env" "memory" (memory 1)) (func (export "load") (param i32) (result i32) (i32.load (local.get 0))))""");
        var result = await RunAsync($$"""
            async () => {
              const memory = new WebAssembly.Memory({ initial: 1, maximum: 2 });
              new DataView(memory.buffer).setInt32(4, 123456, true);
              const { instance } = await WebAssembly.instantiate({{bytes}}, { env: { memory } });
              let rangeError;
              try { memory.grow(2); } catch (e) { rangeError = e.name; }
              return [instance.exports.load(4), Object.prototype.toString.call(memory), rangeError].join();
            }
            """);

        Assert.Equal("123456,[object WebAssembly.Memory],RangeError", result);
    }

    [Fact]
    public async Task Globals_ConvertTheirValues()
    {
        var bytes = Wasm("""
            (module
              (global (export "g") (mut i64) (i64.const 5))
              (global (export "c") f32 (f32.const 1.5))
              (func (export "get") (result i64) (global.get 0)))
            """);
        var result = await RunAsync($$"""
            async () => {
              const { instance } = await WebAssembly.instantiate({{bytes}});
              const { g, c, get } = instance.exports;
              g.value = 2n ** 64n + 3n;
              let error;
              try { c.value = 1; } catch (e) { error = e.name; }
              const own = new WebAssembly.Global({ value: 'i32', mutable: true }, 42.9);
              return [g.value, get(), c.value, c.valueOf(), error, own.value, Object.prototype.toString.call(own)].join();
            }
            """);

        Assert.Equal("3,3,1.5,1.5,TypeError,42,[object WebAssembly.Global]", result);
    }

    [Fact]
    public async Task Tables_KeepTheIdentityOfTheFunctions()
    {
        var bytes = Wasm("""
            (module
              (table (export "table") 2 funcref)
              (func $f (result i32) (i32.const 1))
              (elem (i32.const 0) $f)
              (export "f" (func $f))
              (func (export "call") (param i32) (result i32) (call_indirect (result i32) (local.get 0))))
            """);
        var result = await RunAsync($$"""
            async () => {
              const { instance } = await WebAssembly.instantiate({{bytes}});
              const { table, f, call } = instance.exports;
              const results = [table.length, table.get(0) === f, table.get(1)];
              table.set(1, f);
              results.push(call(1));
              let error;
              try { table.set(0, () => 1); } catch (e) { error = e.name; }
              results.push(error, table.grow(1), table.length);
              const externs = new WebAssembly.Table({ element: 'externref', initial: 1 });
              const object = {};
              externs.set(0, object);
              results.push(externs.get(0) === object);
              return results.join();
            }
            """);

        Assert.Equal("2,true,,1,TypeError,2,3,true", result);
    }

    [Fact]
    public async Task Traps_ThrowRuntimeErrors()
    {
        var bytes = Wasm("""
            (module
              (func (export "div") (param i32 i32) (result i32) (i32.div_s (local.get 0) (local.get 1)))
              (func $loop (export "recurse") (call $loop)))
            """);
        var result = await RunAsync($$"""
            async () => {
              const { instance } = await WebAssembly.instantiate({{bytes}});
              const errors = [];
              try { instance.exports.div(1, 0); } catch (e) { errors.push(e instanceof WebAssembly.RuntimeError); }
              try { instance.exports.recurse(); } catch (e) { errors.push(e.name); }
              errors.push(instance.exports.div(7, 2));
              return errors.join();
            }
            """);

        Assert.Equal("true,RangeError,3", result);
    }

    [Fact]
    public async Task Exceptions_CrossTheBoundary()
    {
        var bytes = Wasm("""
            (module
              (import "env" "throw" (func $throw))
              (tag $tag (export "tag") (param i32))
              (func (export "catchAll") (result i32)
                (block $handler
                  (try_table (catch_all $handler) (call $throw))
                  (return (i32.const 0)))
                (i32.const 1))
              (func (export "rethrow") (call $throw))
              (func (export "throwTag") (param i32) (throw $tag (local.get 0))))
            """);
        var result = await RunAsync($$"""
            async () => {
              const error = new Error('boom');
              const { instance } = await WebAssembly.instantiate({{bytes}}, { env: { throw: () => { throw error; } } });
              const { catchAll, rethrow, throwTag, tag } = instance.exports;
              const results = [catchAll()];
              try { rethrow(); } catch (e) { results.push(e === error); }
              try { throwTag(42); } catch (e) { results.push(e instanceof WebAssembly.Exception, e.is(tag), e.getArg(tag, 0), e instanceof Error); }
              const exception = new WebAssembly.Exception(tag, [7]);
              results.push(exception.getArg(tag, 0), exception.is(WebAssembly.JSTag), WebAssembly.JSTag === WebAssembly.JSTag);
              return results.join();
            }
            """);

        Assert.Equal("1,true,true,true,42,false,7,false,true", result);
    }

    [Fact]
    public async Task ExportedFunctions_HaveTheirIndexAsName()
    {
        var bytes = Wasm("""
            (module
              (import "env" "f" (func $f))
              (func $g)
              (export "f" (func $f))
              (export "g" (func $g))
              (export "g2" (func $g)))
            """);
        var result = await RunAsync($$"""
            async () => {
              const f = () => {};
              const { instance } = await WebAssembly.instantiate({{bytes}}, { env: { f } });
              const exports = instance.exports;
              let constructError;
              try { new exports.g(); } catch (e) { constructError = e.name; }
              return [exports.f.name, exports.g.name, exports.g === exports.g2, exports.f === f, constructError].join();
            }
            """);

        Assert.Equal("0,1,true,false,TypeError", result);
    }

    [Fact]
    public async Task StructuredClone_SharesTheModule()
    {
        var bytes = Wasm("""(module (func (export "f") (result i32) (i32.const 7)))""");
        var result = await RunAsync($$"""
            async () => {
              const module = new WebAssembly.Module({{bytes}});
              const clone = structuredClone(module);
              let memoryError;
              try { structuredClone(new WebAssembly.Memory({ initial: 1 })); } catch (e) { memoryError = e.name; }
              return [clone === module, clone instanceof WebAssembly.Module, new WebAssembly.Instance(clone).exports.f(), memoryError].join();
            }
            """);

        Assert.Equal("false,true,7,DataCloneError", result);
    }

    [Fact]
    public async Task Namespace_IsNotDefinedWithoutRuntime()
    {
        Assert.Equal("undefined", await RunAsync("async () => typeof WebAssembly", withRuntime: false));
        Assert.Equal("undefined", await RunAsync("async () => typeof WebAssembly", features: new WebPlatformFeatures { WebAssemblyEnabled = false }));
        Assert.Equal("object", await RunAsync("async () => typeof WebAssembly"));
    }

    [Fact]
    public async Task Timeout_InterruptsAnInfiniteLoop()
    {
        var bytes = Wasm("""(module (func (export "loop") (loop $l (br $l))))""");
        using var test = new WebAssemblyTests(new TestScriptEngineOptions { Timeout = TimeSpan.FromMilliseconds(500) });
        var context = test._contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = test._engine,
            WebAssemblyRuntime = Runtime,
            UnhandledScriptError = (_, error) => test._errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => $"<script>new WebAssembly.Instance(new WebAssembly.Module({bytes})).exports.loop();</script><script>document.title = 'after';</script>");
        await test._contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Single(test._errors);
        Assert.Equal("after", document.Title);
    }
}
