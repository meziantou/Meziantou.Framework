using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Meziantou.Framework.WebPlatform.Wasmtime;
using Meziantou.Framework.WebPlatform.WebAssembly;

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

    // The shared memories can only be cloned by a cross-origin isolated global
    private static readonly KeyValuePair<string, string>[] CrossOriginIsolationHeaders =
    [
        new("Content-Type", "text/html"),
        new("Cross-Origin-Opener-Policy", "same-origin"),
        new("Cross-Origin-Embedder-Policy", "require-corp"),
    ];

    /// <summary>The bytes of a module in the text format, as a JavaScript array literal.</summary>
    private static string Wasm(string text) => "new Uint8Array([" + string.Join(',', WasmtimeRuntime.WatToWasm(text)) + "])";

    // Runs a script whose promise sets the title of the document
    private async Task<string> RunAsync(string script, WebPlatformFeatures? features = null, bool withRuntime = true, bool crossOriginIsolated = false, bool waitForTitle = false)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            WebAssemblyRuntime = withRuntime ? Runtime : null,
            Features = features,
            UnhandledScriptError = (_, error) =>
            {
                lock (_errors)
                {
                    _errors.Add(error);
                }
            },
        });

        var html = $"<script>({script})().then(r => document.title = String(r), e => document.title = 'error: ' + e);</script>";
        var document = crossOriginIsolated
            ? context.Navigate("https://example.com/", _ => new DocumentResource(html) { Headers = CrossOriginIsolationHeaders })
            : context.Navigate("https://example.com/", _ => html);

        // The modules are compiled on the thread pool, and the workers run on their own threads
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (waitForTitle && document.Title.Length == 0 && stopwatch.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
            await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        }

        lock (_errors)
        {
            Assert.Empty(_errors);
        }

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
    public async Task JsStringBuiltins_AreProvidedByTheHost()
    {
        var bytes = Wasm("""
            (module
              (type $chars (array (mut i16)))
              (import "wasm:js-string" "length" (func $length (param externref) (result i32)))
              (import "wasm:js-string" "concat" (func $concat (param externref externref) (result (ref extern))))
              (import "wasm:js-string" "fromCharCodeArray" (func $fromCharCodeArray (param (ref null $chars) i32 i32) (result (ref extern))))
              (import "wasm:js-string" "intoCharCodeArray" (func $intoCharCodeArray (param externref (ref null $chars) i32) (result i32)))
              (import "strings" "Hello, " (global $hello (ref extern)))
              (func (export "greet") (param $name externref) (result externref)
                (call $concat (global.get $hello) (local.get $name)))
              (func (export "length") (param externref) (result i32) (call $length (local.get 0)))
              (func (export "copy") (param $s externref) (result externref)
                (local $array (ref null $chars))
                (local.set $array (array.new_default $chars (call $length (local.get $s))))
                (drop (call $intoCharCodeArray (local.get $s) (local.get $array) (i32.const 0)))
                (call $fromCharCodeArray (local.get $array) (i32.const 1) (array.len (local.get $array)))))
            """);
        var result = await RunAsync($$"""
            async () => {
              const options = { builtins: ['js-string'], importedStringConstants: 'strings' };
              const module = new WebAssembly.Module({{bytes}}, options);
              const { exports } = new WebAssembly.Instance(module, {});
              let error;
              try { exports.length(42); } catch (e) { error = e; }
              return [WebAssembly.Module.imports(module).length, exports.greet('World'), exports.length('abc'), exports.copy('☺bc'),
                error instanceof WebAssembly.RuntimeError, WebAssembly.validate({{bytes}}, options), WebAssembly.validate({{bytes}}, { builtins: [] })].join('|');
            }
            """);

        Assert.Equal("0|Hello, World|3|bc|true|true|true", result);
    }

    [Theory]
    [InlineData("(array (mut i8))", "fromCharCodeArray", "(param (ref null $chars) i32 i32) (result (ref extern))")]
    [InlineData("(array (mut i32))", "fromCharCodeArray", "(param (ref null $chars) i32 i32) (result (ref extern))")]
    [InlineData("(array i16)", "fromCharCodeArray", "(param (ref null $chars) i32 i32) (result (ref extern))")]
    [InlineData("(sub (array (mut i16)))", "fromCharCodeArray", "(param (ref null $chars) i32 i32) (result (ref extern))")]
    [InlineData("(array (mut i8))", "intoCharCodeArray", "(param externref (ref null $chars) i32) (result i32)")]
    [InlineData("(array i16)", "intoCharCodeArray", "(param externref (ref null $chars) i32) (result i32)")]
    public async Task JsStringBuiltins_RejectAnArrayThatIsNotAMutableI16Array(string arrayType, string name, string signature)
    {
        var bytes = Wasm($$"""
            (module
              (type $chars {{arrayType}})
              (import "wasm:js-string" "{{name}}" (func {{signature}})))
            """);
        var result = await RunAsync($$"""
            async () => {
              const options = { builtins: ['js-string'] };
              let error;
              try { new WebAssembly.Module({{bytes}}, options); } catch (e) { error = e.name; }
              let asyncError;
              try { await WebAssembly.compile({{bytes}}, options); } catch (e) { asyncError = e.name; }
              return [error, asyncError, WebAssembly.validate({{bytes}}, options), WebAssembly.validate({{bytes}}), new WebAssembly.Module({{bytes}}) instanceof WebAssembly.Module].join();
            }
            """);

        Assert.Equal("CompileError,CompileError,false,true,true", result);
    }

    [Fact]
    public async Task JsStringBuiltins_RejectAnArrayOfAnotherRecursionGroup()
    {
        var bytes = Wasm("""
            (module
              (rec (type $chars (array (mut i16))) (type (struct)))
              (import "wasm:js-string" "fromCharCodeArray" (func (param (ref null $chars) i32 i32) (result (ref extern)))))
            """);
        var result = await RunAsync($$"""
            async () => WebAssembly.validate({{bytes}}, { builtins: ['js-string'] })
            """);

        Assert.Equal("false", result);
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
    public async Task Memory_ResizableBuffer_GrowsWithTheMemory()
    {
        var bytes = Wasm("""
            (module
              (memory (export "memory") 1 4)
              (func (export "load") (param i32) (result i32) (i32.load8_u (local.get 0)))
              (func (export "grow") (param i32) (result i32) (memory.grow (local.get 0))))
            """);
        var result = await RunAsync($$"""
            async () => {
              const { instance } = await WebAssembly.instantiate({{bytes}});
              const { memory, load, grow } = instance.exports;
              const fixed = memory.buffer;
              const buffer = memory.toResizableBuffer();
              const view = new Uint8Array(buffer);
              const created = [fixed.detached, buffer.resizable, buffer.maxByteLength, memory.buffer === buffer, memory.toResizableBuffer() === buffer].join();
              buffer.resize(2 * 65536);
              new Uint8Array(buffer)[65536] = 5;
              grow(1);
              const grown = [buffer.byteLength, view.length, load(65536), memory.buffer === buffer].join();
              const errors = [];
              for (const length of [3 * 65536 + 1, 65536]) {
                try { buffer.resize(length); } catch (e) { errors.push(e.name); }
              }

              const shared = new WebAssembly.Memory({ initial: 1, maximum: 2, shared: true });
              const sharedBuffer = shared.buffer;
              shared.grow(1);
              const growable = shared.toResizableBuffer();
              const sharedResult = [sharedBuffer.byteLength, shared.buffer === sharedBuffer, sharedBuffer.growable, Object.isFrozen(sharedBuffer), growable.growable, growable.byteLength].join();
              const fixedAgain = memory.toFixedLengthBuffer();
              return [created, grown, errors.join(), sharedResult, buffer.detached, fixedAgain.resizable, fixedAgain.byteLength].join('|');
            }
            """);

        Assert.Equal("true,true,262144,true,true|196608,196608,5,true|RangeError,RangeError|65536,false,false,true,true,131072|true|false|196608", result);
    }

    [Fact]
    public async Task SharedMemory_ClonesShareTheGrowth()
    {
        var result = await RunAsync("""
            async () => {
              const memory = new WebAssembly.Memory({ initial: 1, maximum: 4, shared: true });
              const buffer = memory.buffer;
              const copy = structuredClone(memory);
              const copyBuffer = copy.buffer;
              const grown = [copy.grow(1), memory.grow(1)].join();
              const copyRefreshed = [copyBuffer.byteLength, copy.buffer === copyBuffer, copy.buffer.byteLength, copy.buffer === copy.buffer, copy.toFixedLengthBuffer() === copy.buffer].join();
              const memoryBuffer = memory.buffer;
              new Uint8Array(copy.buffer)[2 * 65536] = 7;
              const memoryRefreshed = [buffer.byteLength, memoryBuffer === buffer, memoryBuffer.byteLength, new Uint8Array(memoryBuffer)[2 * 65536], copy.grow(0)].join();
              return [grown, copyRefreshed, memoryRefreshed].join('|');
            }
            """, crossOriginIsolated: true);

        Assert.Equal("1,2|65536,false,196608,true,true|65536,false,196608,7,3", result);
    }

    [Fact]
    public async Task SharedMemory_ClonedBufferKeepsItsLength()
    {
        var result = await RunAsync("""
            async () => {
              const memory = new WebAssembly.Memory({ initial: 1, maximum: 4, shared: true });
              const clone = structuredClone(memory.buffer);
              memory.grow(1);
              new Uint8Array(memory.buffer)[1] = 9;
              let growError;
              try { clone.grow(2 * 65536); } catch (e) { growError = e.name; }
              const fixed = [clone.growable, clone.byteLength, clone.maxByteLength, new Uint8Array(clone)[1], growError].join();
              const growable = structuredClone(memory.toResizableBuffer());
              const resizable = [growable.growable, growable.byteLength, growable.maxByteLength].join();
              const plain = structuredClone(new SharedArrayBuffer(8, { maxByteLength: 16 }));
              return [fixed, resizable, plain.growable, plain.maxByteLength].join('|');
            }
            """, crossOriginIsolated: true);

        Assert.Equal("false,65536,65536,9,TypeError|true,131072,262144|true|16", result);
    }

    [Fact]
    public async Task SharedMemory_ResizableBuffer_GrowsToTheRequestedLength()
    {
        var result = await RunAsync("""
            async () => {
              const memory = new WebAssembly.Memory({ initial: 1, maximum: 2, shared: true });
              const copy = structuredClone(memory);
              const buffer = memory.toResizableBuffer();
              const copyBuffer = copy.toResizableBuffer();
              buffer.grow(2 * 65536);
              copyBuffer.grow(2 * 65536);
              const errors = [];
              for (const length of [65536, 2 * 65536 + 1, 3 * 65536]) {
                try { copyBuffer.grow(length); } catch (e) { errors.push(e.name); }
              }

              return [buffer.byteLength, copyBuffer.byteLength, memory.grow(0), copy.buffer.byteLength, errors.join()].join('|');
            }
            """, crossOriginIsolated: true);

        Assert.Equal("131072|131072|2|131072|RangeError,RangeError,RangeError", result);
    }

    [Fact]
    public async Task SharedMemory_CanBeImportedByAModule()
    {
        var bytes = Wasm("""
            (module
              (import "env" "memory" (memory 1 2 shared))
              (func (export "add") (param i32 i32) (result i32) (i32.atomic.rmw.add (local.get 0) (local.get 1)))
              (func (export "cas") (param i32 i32 i32) (result i32) (i32.atomic.rmw.cmpxchg (local.get 0) (local.get 1) (local.get 2)))
              (func (export "load64") (param i32) (result i64) (i64.atomic.load (local.get 0)))
              (func (export "size") (result i32) (memory.size))
              (func (export "grow") (param i32) (result i32) (memory.grow (local.get 0))))
            """);
        var result = await RunAsync($$"""
            async () => {
              const memory = new WebAssembly.Memory({ initial: 1, maximum: 2, shared: true });
              const { instance } = await WebAssembly.instantiate({{bytes}}, { env: { memory } });
              const e = instance.exports;
              const view = new Int32Array(memory.buffer);
              Atomics.store(view, 1, 40);
              const atomics = [e.add(4, 2), Atomics.load(view, 1), e.cas(4, 42, 7), view[1]].join();
              new BigInt64Array(memory.buffer)[1] = 5n;
              const buffer = memory.buffer;
              const growth = [e.load64(8), e.grow(1), buffer.byteLength, memory.buffer.byteLength, e.size(), e.grow(1), memory.grow(0)].join();
              let linkError;
              try { await WebAssembly.instantiate({{bytes}}, { env: { memory: new WebAssembly.Memory({ initial: 1, maximum: 2 }) } }); } catch (error) { linkError = error.name; }
              return [atomics, growth, linkError, memory.buffer instanceof SharedArrayBuffer].join('|');
            }
            """, crossOriginIsolated: true);

        Assert.Equal("40,42,42,7|5,1,65536,131072,2,-1,2|LinkError|true", result);
    }

    [Fact]
    public async Task SharedMemory_CanBeDefinedByAModule()
    {
        var bytes = Wasm("""
            (module
              (memory (export "memory") 1 4 shared)
              (func (export "store") (param i32 i32) (i32.atomic.store (local.get 0) (local.get 1)))
              (func (export "grow") (param i32) (result i32) (memory.grow (local.get 0))))
            """);
        var result = await RunAsync($$"""
            async () => {
              const { instance } = await WebAssembly.instantiate({{bytes}});
              const { memory, store, grow } = instance.exports;
              store(8, 123);
              const copy = structuredClone(memory);
              const resizable = memory.toResizableBuffer();
              const before = [memory.buffer instanceof SharedArrayBuffer, Atomics.load(new Int32Array(copy.buffer), 2), resizable.growable, resizable.maxByteLength].join();
              const after = [grow(1), resizable.byteLength, copy.buffer.byteLength, memory.grow(1), instance.exports.memory === memory].join();
              resizable.grow(4 * 65536);
              return [before, after, copy.grow(0), WebAssembly.Module.exports(new WebAssembly.Module({{bytes}})).map(e => e.kind).join()].join('|');
            }
            """, crossOriginIsolated: true);

        Assert.Equal("true,123,true,262144|1,131072,131072,2,true|4|memory,function,function", result);
    }

    [Fact]
    public async Task SharedMemory_WaitAndNotifyBetweenWorkerAndWindow()
    {
        var bytes = Wasm("""
            (module
              (import "env" "memory" (memory 1 1 shared))
              (func (export "wait") (param i32 i32) (result i32) (memory.atomic.wait32 (local.get 0) (local.get 1) (i64.const -1)))
              (func (export "storeAndNotify") (param i32 i32) (result i32)
                (i32.atomic.store (local.get 0) (local.get 1))
                (memory.atomic.notify (local.get 0) (i32.const 1))))
            """);

        // The worker waits in WebAssembly until the window notifies it with Atomics.notify, then wakes the window, which
        // waits with Atomics.waitAsync, with memory.atomic.notify
        var result = await RunAsync($$"""
            async () => {
              const workerSource = `onmessage = async ({ data: { module, memory } }) => {
                const { exports } = await WebAssembly.instantiate(module, { env: { memory } });
                postMessage('ready');
                const result = exports.wait(0, 0);
                exports.storeAndNotify(4, result + 10);
              };`;
              const memory = new WebAssembly.Memory({ initial: 1, maximum: 1, shared: true });
              const module = await WebAssembly.compile({{bytes}});
              const view = new Int32Array(memory.buffer);
              const worker = new Worker(URL.createObjectURL(new Blob([workerSource], { type: 'text/javascript' })));
              const ready = new Promise(resolve => worker.onmessage = resolve);
              worker.postMessage({ module, memory });
              await ready;
              const { async, value } = Atomics.waitAsync(view, 1, 0);

              // The timers do not run while the worker is blocked: the window spins until the worker waits
              while (Atomics.notify(view, 0, 1) === 0) {
              }

              return [async, await value, Atomics.load(view, 1)].join();
            }
            """, crossOriginIsolated: true, waitForTitle: true);

        Assert.Equal("true,ok,10", result);
    }

    [Fact]
    public async Task SharedMemory_AtomicsWaitIsNotifiedByWebAssembly()
    {
        var bytes = Wasm("""
            (module
              (import "env" "memory" (memory 1 1 shared))
              (func (export "notify") (param i32) (result i32) (memory.atomic.notify (local.get 0) (i32.const 2))))
            """);

        // The worker blocks in Atomics.wait until the WebAssembly code of the window notifies it
        var result = await RunAsync($$"""
            async () => {
              const workerSource = `onmessage = ({ data: memory }) => {
                const view = new Int32Array(memory.buffer);
                Atomics.store(view, 1, 1);
                Atomics.notify(view, 1);
                const results = [Atomics.wait(view, 0, 0), Atomics.wait(view, 0, 1), Atomics.wait(view, 0, 0, 10)];
                postMessage(results.join());
              };`;
              const memory = new WebAssembly.Memory({ initial: 1, maximum: 1, shared: true });
              const { instance } = await WebAssembly.instantiate({{bytes}}, { env: { memory } });
              const view = new Int32Array(memory.buffer);
              const worker = new Worker(URL.createObjectURL(new Blob([workerSource], { type: 'text/javascript' })));
              const done = new Promise(resolve => worker.onmessage = e => resolve(e.data));
              const { value: started } = Atomics.waitAsync(view, 1, 0);
              worker.postMessage(memory);
              const startResult = await started;

              // The timers do not run while the worker is blocked: the window spins until the worker waits
              let notified;
              while ((notified = instance.exports.notify(0)) === 0) {
              }

              return [startResult, notified, await done].join('|');
            }
            """, crossOriginIsolated: true, waitForTitle: true);

        Assert.Equal("ok|1|ok,not-equal,timed-out", result);
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
    public void CreateTag_AcceptsALargeNumberOfParameters()
    {
        // A script chooses the number of parameters of a tag (new WebAssembly.Tag({ parameters })). The value types of
        // the native function type were allocated on the stack: 800 KB for 100,000 parameters, more than this thread has.
        var type = new WasmTagType([.. Enumerable.Repeat(WasmValueType.I32, 100_000)]);
        WasmTag? tag = null;
        var thread = new Thread(() =>
        {
            using var store = Runtime.CreateStore(new NoInterruptionHost());
            tag = store.CreateTag(type);
        }, maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();

        Assert.NotNull(tag);
    }

    private sealed class NoInterruptionHost : IWasmHost
    {
        public void CheckInterrupt()
        {
        }

        public WasmSharedMemoryBytes CreateSharedMemoryBytes(ulong byteLength, ulong capacity) => throw new WasmAllocationException("The host cannot create a shared memory");
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

    [Fact]
    public void Store_DisposedByAHostFunction_IsDeletedAfterTheCall()
    {
        var module = Runtime.Compile(WasmtimeRuntime.WatToWasm("""
            (module
              (import "env" "f" (func $f (result i32)))
              (func (export "run") (result i32) (i32.add (call $f) (i32.const 1))))
            """));
        using var store = Runtime.CreateStore(new NoInterruptHost());
        var hostFunction = store.CreateHostFunction(new WasmFunctionType([], [WasmValueType.I32]), hostFunction: null, (_, results) =>
        {
            store.Dispose();
            results[0] = WasmValue.FromI32(41);
        });
        var run = (WasmFunction)store.Instantiate(module, [hostFunction]).Exports[0].Value;

        var results = new WasmValue[1];
        run.Invoke([], results);
        Assert.Equal(42, results[0].I32);
        Assert.Throws<ObjectDisposedException>(() => run.Invoke([], results));
        Assert.Throws<ObjectDisposedException>(() => store.CreateGlobal(new WasmGlobalType(WasmValueType.I32, mutable: false), WasmValue.FromI32(0)));
    }

    [Fact]
    public void ExternRef_WhenTheGcHeapIsFull_Throws()
    {
        // Wasmtime drops the data of an externref it cannot allocate, which runs the finalizer that frees the GCHandle of
        // the host value: the store must not free it a second time
        using var runtime = new WasmtimeRuntime(new WasmtimeRuntimeOptions { MaxMemorySize = 1024 * 1024 });
        using var store = runtime.CreateStore(new NoInterruptHost());
        var table = store.CreateTable(new WasmTableType(WasmValueType.ExternRef, minimum: 0, maximum: null, is64: false), WasmValue.Null);
        var first = new WasmHostReference(new object());
        table.Grow(1, WasmValue.FromReference(first));

        WasmAllocationException? exception = null;
        for (var i = 0; i < 10_000_000 && exception is null; i++)
        {
            try
            {
                table.Grow(1, WasmValue.FromReference(new WasmHostReference(new object())));
            }
            catch (WasmAllocationException ex)
            {
                exception = ex;
            }
        }

        Assert.NotNull(exception);
        Assert.Equal("The externref cannot be allocated", exception.Message);
        Assert.Same(first, table.Get(0).Reference);

        table.Set(0, WasmValue.Null);
        Assert.Null(table.Get(0).Reference);
    }

    private sealed class NoInterruptHost : IWasmHost
    {
        public void CheckInterrupt()
        {
        }

        public WasmSharedMemoryBytes CreateSharedMemoryBytes(ulong byteLength, ulong capacity) => throw new WasmAllocationException("The host cannot create a shared memory");
    }

    [Fact]
    public async Task Documentation_WebAssembly()
    {
        #region WebPlatformWasmtime_RunModules
        using var runtime = new WasmtimeRuntime(); // can be shared by the browsing contexts
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
        #endregion
    }

    [Fact]
    public async Task Documentation_WebAssemblySharedMemory()
    {
        #region WebPlatformWasmtime_SharedMemory
        using var runtime = new WasmtimeRuntime();
        using var engine = new JavaScriptScriptEngine();
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            ScriptEngine = engine,
            WebAssemblyRuntime = runtime,
        });

        var document = await context.NavigateAsync("https://example.com/", _ => """
            <output></output>
            <script>
              // (module (import "env" "memory" (memory 1 1 shared))
              //   (func (export "add") (param i32 i32) (result i32) (i32.atomic.rmw.add (local.get 0) (local.get 1))))
              const bytes = new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0, 1, 7, 1, 96, 2, 127, 127, 1, 127, 2, 16, 1, 3, 101,
                110, 118, 6, 109, 101, 109, 111, 114, 121, 2, 3, 1, 1, 3, 2, 1, 0, 7, 7, 1, 3, 97, 100, 100, 0, 0, 10, 12, 1,
                10, 0, 32, 0, 32, 1, 254, 30, 2, 0, 11]);
              const memory = new WebAssembly.Memory({ initial: 1, maximum: 1, shared: true });
              WebAssembly.instantiate(bytes, { env: { memory } }).then(({ instance }) => {
                const counter = new Int32Array(memory.buffer); // a SharedArrayBuffer: postMessage() shares it with the workers
                Atomics.store(counter, 0, 40);
                instance.exports.add(0, 2);
                document.querySelector('output').textContent = Atomics.load(counter, 0);
              });
            </script>
            """);
        await context.EventLoop.RunUntilIdleAsync();

        Assert.Equal("42", document.QuerySelector("output")!.TextContent);
        #endregion
    }
}
