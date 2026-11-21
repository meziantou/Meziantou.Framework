using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Meziantou.Framework.Diagnostics;

namespace Meziantou.Framework.WasmEngine.Tests;

public sealed class WasmRuntimeTests
{
    private static readonly WasmRuntime Runtime = new();

    static WasmRuntimeTests()
    {
        // A failure of the compiler to .NET code fails the tests, instead of interpreting the function
        JitOps.ThrowOnInvalidProgram = true;
    }

    private static WasmModule Compile(string text) => Runtime.Compile(WasmRuntime.WatToWasm(text));

    [Fact]
    public void Documentation_GettingStarted()
    {
        #region WasmEngine_GettingStarted
        // A runtime compiles the modules: create one and share it (it can be used from any thread)
        using var runtime = new WasmRuntime();
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (func (export "add") (param i32 i32) (result i32)
                (i32.add (local.get 0) (local.get 1))))
            """));

        // A store holds the instances: use it from one thread at a time
        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module);
        var add = instance.GetFunction("add")!;

        var results = add.Call(WasmValue.FromI32(40), WasmValue.FromI32(2));
        Assert.Equal(42, results[0].I32);
        #endregion
    }

    [Fact]
    public void Documentation_HostFunctions()
    {
        #region WasmEngine_HostFunctions
        using var runtime = new WasmRuntime();
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (import "env" "log" (func $log (param i32)))
              (func (export "run")
                (call $log (i32.const 1))
                (call $log (i32.const 2))))
            """));

        using var store = runtime.CreateStore();
        var logged = new List<int>();
        var log = store.CreateFunction(
            new WasmFunctionType([WasmValueType.I32], []),
            (arguments, results) => logged.Add(arguments[0].I32));

        // The imports are resolved by their module and name
        var instance = store.Instantiate(module, new WasmImports().Define("env", "log", log));
        instance.GetFunction("run")!.Call();

        Assert.Equal([1, 2], logged);
        #endregion
    }

    [Fact]
    public void Documentation_Memory()
    {
        #region WasmEngine_Memory
        using var runtime = new WasmRuntime();
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (memory (export "memory") 1 4)
              (data (i32.const 0) "Hello")
              (func (export "grow") (result i32) (memory.grow (i32.const 1))))
            """));

        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module);
        var memory = instance.GetMemory("memory")!;
        Assert.Equal("Hello", Encoding.ASCII.GetString(memory.GetSpan()[..5]));

        // The bytes of a memory are a .NET array, replaced when the memory grows
        var changes = 0;
        memory.DataChanged += (sender, e) => changes++;
        instance.GetFunction("grow")!.Call();
        Assert.Equal(2UL, memory.Size); // pages of 64 KiB
        Assert.HasCount(2 * 65536, memory.Data);
        Assert.Equal(1, changes);
        #endregion
    }

    [Fact]
    public void Documentation_GlobalsAndTables()
    {
        #region WasmEngine_GlobalsAndTables
        using var runtime = new WasmRuntime();
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (global (export "counter") (mut i32) (i32.const 0))
              (table (export "callbacks") 1 funcref)
              (func (export "increment")
                (global.set 0 (i32.add (global.get 0) (i32.const 1)))))
            """));

        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module);
        var counter = instance.GetGlobal("counter")!;
        var callbacks = instance.GetTable("callbacks")!;

        // A table contains references: put the exported function in it
        callbacks.Set(0, WasmValue.FromReference(instance.GetFunction("increment")));
        var increment = (WasmFunction)callbacks.Get(0).Reference!;
        increment.Call();
        increment.Call();
        Assert.Equal(2, counter.Get().I32);

        counter.Set(WasmValue.FromI32(40));
        Assert.Equal(40, counter.Get().I32);
        #endregion
    }

    [Fact]
    public void Documentation_Exceptions()
    {
        #region WasmEngine_Exceptions
        using var runtime = new WasmRuntime();
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (import "env" "fail" (func $fail))
              (tag $error (export "error") (param i32))
              (func (export "throw") (param i32) (throw $error (local.get 0)))
              (func (export "catch") (result i32)
                (block $handler (result i32)
                  (try_table (catch $error $handler) (call $fail))
                  (i32.const -1)))
              (func (export "divide") (param i32 i32) (result i32) (i32.div_s (local.get 0) (local.get 1))))
            """));

        using var store = runtime.CreateStore();
        WasmInstance? instance = null;

        // A host function throws an exception in WebAssembly with a WasmThrowException
        var fail = store.CreateFunction(new WasmFunctionType([], []), (arguments, results) =>
            throw new WasmThrowException(store.CreateException(instance!.GetTag("error")!, [WasmValue.FromI32(42)])));
        instance = store.Instantiate(module, [fail]);
        Assert.Equal(42, instance.GetFunction("catch")!.Call()[0].I32);

        // An exception thrown by WebAssembly is a WasmThrowException
        var thrown = Assert.Throws<WasmThrowException>(() => instance.GetFunction("throw")!.Call(WasmValue.FromI32(7)));
        Assert.Same(instance.GetTag("error"), thrown.Exception.Tag);
        Assert.Equal(7, thrown.Exception.GetArgument(0).I32);

        // A trap is a WasmTrapException
        var trap = Assert.Throws<WasmTrapException>(() => instance.GetFunction("divide")!.Call(WasmValue.FromI32(1), WasmValue.FromI32(0)));
        Assert.Equal(WasmTrapKind.Other, trap.TrapKind);
        #endregion
    }

    [Fact]
    public void Documentation_Interruption()
    {
        #region WasmEngine_Interruption
        using var runtime = new WasmRuntime(new WasmRuntimeOptions
        {
            InterruptionCheckInterval = TimeSpan.FromMilliseconds(10),
        });
        var module = runtime.Compile(WasmRuntime.WatToWasm("""(module (func (export "loop") (loop $l (br $l))))"""));

        // The running code calls CheckInterrupt periodically: the exception it throws stops the code
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var store = runtime.CreateStore(new WasmStoreOptions
        {
            CheckInterrupt = cancellation.Token.ThrowIfCancellationRequested,
        });

        var loop = store.Instantiate(module).GetFunction("loop")!;
        Assert.Throws<OperationCanceledException>(() => loop.Call());
        #endregion
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Interruption_RecursionWithoutLoops_IsInterrupted(int threshold)
    {
        // 2^60 calls without loops: the functions that make calls check the interruption ($leaf does not need to)
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = threshold, InterruptionCheckInterval = TimeSpan.FromMilliseconds(10) });
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (func $leaf (param i32) (result i32) (i32.add (local.get 0) (i32.const 1)))
              (func $f (export "f") (param i32) (result i32)
                (if (result i32) (i32.eqz (local.get 0))
                  (then (call $leaf (i32.const 0)))
                  (else
                    (i32.add
                      (call $f (i32.sub (local.get 0) (i32.const 1)))
                      (call $f (i32.sub (local.get 0) (i32.const 1))))))))
            """));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var store = runtime.CreateStore(new WasmStoreOptions { CheckInterrupt = cancellation.Token.ThrowIfCancellationRequested });
        var f = store.Instantiate(module).GetFunction("f")!;
        Assert.Throws<OperationCanceledException>(() => f.Call(WasmValue.FromI32(60)));
    }

    [Fact]
    public void Interruption_Timer_RunsWhileStoresCanBeInterrupted()
    {
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { InterruptionCheckInterval = TimeSpan.FromMilliseconds(10) });
        Assert.False(runtime.Interruptions.IsRunning);

        using (runtime.CreateStore())
        {
            Assert.False(runtime.Interruptions.IsRunning);
        }

        var first = runtime.CreateStore(new WasmStoreOptions { CheckInterrupt = () => { } });
        var second = runtime.CreateStore(new WasmStoreOptions { CheckInterrupt = () => { } });
        Assert.True(runtime.Interruptions.IsRunning);
        first.Dispose();
        Assert.True(runtime.Interruptions.IsRunning);
        second.Dispose();
        Assert.False(runtime.Interruptions.IsRunning);
    }

    [Fact]
    public void Interruption_OnlyTheStoresWithAnAction_AreInterrupted()
    {
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { InterruptionCheckInterval = TimeSpan.FromMilliseconds(10) });
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (func (export "count") (param i32) (result i32)
                (loop $next
                  (br_if $next (local.tee 0 (i32.sub (local.get 0) (i32.const 1)))))
                (local.get 0)))
            """));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        using var interrupted = runtime.CreateStore(new WasmStoreOptions { CheckInterrupt = cancellation.Token.ThrowIfCancellationRequested });
        using var other = runtime.CreateStore();
        var count = other.Instantiate(module).GetFunction("count")!;
        var spin = interrupted.Instantiate(module).GetFunction("count")!;

        // 2^32 - 1 iterations: the store is interrupted before the end
        Assert.Throws<OperationCanceledException>(() => spin.Call(WasmValue.FromI32(0)));
        Assert.Equal(0, count.Call(WasmValue.FromI32(1_000_000))[0].I32);
    }

    [Fact]
    public void Documentation_Compilation()
    {
        #region WasmEngine_Compilation
        // A function is interpreted, and compiled to .NET code once it is called CompilationThreshold times (when the
        // platform supports dynamic code)
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = 10 });
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (func $fib (export "fib") (param i32) (result i32)
                (if (result i32) (i32.lt_u (local.get 0) (i32.const 2))
                  (then (local.get 0))
                  (else
                    (i32.add
                      (call $fib (i32.sub (local.get 0) (i32.const 1)))
                      (call $fib (i32.sub (local.get 0) (i32.const 2))))))))
            """));
        using var store = runtime.CreateStore();
        var fib = store.Instantiate(module).GetFunction("fib")!;
        Assert.Equal(6765, fib.Call(WasmValue.FromI32(20))[0].I32);
        #endregion

        Assert.Equal(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported, ((CodeFunction)fib).Code.Compiled is not null);
    }

    [Fact]
    public void Options_WhenNotValid_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WasmRuntime(new WasmRuntimeOptions { InterruptionCheckInterval = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WasmRuntime(new WasmRuntimeOptions { MaxStackSize = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WasmRuntime(new WasmRuntimeOptions { MaxMemorySize = -1 }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void CompilationThreshold_InterpretsOrCompilesTheFunctions(int threshold)
    {
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = threshold });
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (memory 1)
              (tag $e (param i32))
              (func $thrower (param i32) (throw $e (local.get 0)))
              (func (export "run") (param i32) (result i32)
                (local $sum i32)
                (block $done
                  (loop $next
                    (br_if $done (i32.eqz (local.get 0)))
                    (i32.store (i32.const 0) (i32.add (i32.load (i32.const 0)) (local.get 0)))
                    (local.set 0 (i32.sub (local.get 0) (i32.const 1)))
                    (br $next)))
                (block $caught (result i32)
                  (try_table (catch $e $caught)
                    (call $thrower (i32.load (i32.const 0))))
                  (i32.const -1))))
            """));
        using var store = runtime.CreateStore();
        var run = store.Instantiate(module).GetFunction("run")!;
        Assert.Equal(5050, run.Call(WasmValue.FromI32(100))[0].I32);
        Assert.Equal(threshold >= 0 && System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported, ((CodeFunction)run).Code.Compiled is not null);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public void Calls_CaughtExceptions_EndTheFramesTheyUnwind(int threshold)
    {
        // One iteration out of two throws through two frames ($middle calls $leaf directly once it returned): the stack would
        // be exhausted if the end of these frames was not counted
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = threshold });
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (tag $e (param i32))
              (func $leaf (param i32) (result i32)
                (if (i32.eqz (local.get 0)) (then (throw $e (i32.const 7))))
                (i32.sub (local.get 0) (i32.const 1)))
              (func $middle (param i32) (result i32)
                (call $leaf (call $leaf (local.get 0))))
              (func $catcher (param i32) (result i32)
                (block $caught (result i32)
                  (try_table (catch $e $caught)
                    (return (call $middle (local.get 0))))
                  (unreachable)))
              (func (export "run") (param $n i32) (result i32)
                (local $sum i32)
                (loop $next
                  (local.set $sum (i32.add (local.get $sum) (call $catcher (i32.add (i32.and (local.get $n) (i32.const 1)) (i32.const 1)))))
                  (br_if $next (local.tee $n (i32.sub (local.get $n) (i32.const 1)))))
                (local.get $sum)))
            """));
        using var store = runtime.CreateStore();
        var run = store.Instantiate(module).GetFunction("run")!;
        Assert.Equal(350_000, run.Call(WasmValue.FromI32(100_000))[0].I32);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public void Calls_ImportedAndIndirect_UseTheInstanceOfTheCallee(int threshold)
    {
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = threshold });
        var library = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (global $counter (mut i64) (i64.const 1000))
              (func (export "next") (param i64) (result i64)
                (global.set $counter (i64.add (global.get $counter) (local.get 0)))
                (global.get $counter)))
            """));
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (type $plain (func (param i64) (result i64)))
              (type $base (sub (func (param i64) (result i64))))
              (type $derived (sub $base (func (param i64) (result i64))))
              (type $other (func (param i32) (result i32)))
              (import "lib" "next" (func $next (type $plain)))
              (global $calls (mut i64) (i64.const 0))
              (table $table 4 funcref)
              (elem (table $table) (i32.const 0) func $next $local $derived_impl)
              (func $local (type $plain)
                (global.set $calls (i64.add (global.get $calls) (i64.const 1)))
                (local.get 0))
              (func $derived_impl (type $derived) (i64.mul (local.get 0) (i64.const 2)))
              (func (export "run") (param $n i32) (result i64)
                (local $sum i64)
                (loop $next_iteration
                  (local.set $sum (i64.add (local.get $sum) (call $next (i64.const 1))))
                  (local.set $sum (i64.add (local.get $sum) (call_indirect $table (type $plain) (i64.const 1) (i32.const 0))))
                  (local.set $sum (i64.add (local.get $sum) (call_indirect $table (type $plain) (i64.const 5) (i32.const 1))))
                  (local.set $sum (i64.add (local.get $sum) (call_indirect $table (type $base) (i64.const 3) (i32.const 2))))
                  (local.set $sum (i64.add (local.get $sum) (call_ref $plain (i64.const 7) (ref.func $local))))
                  (br_if $next_iteration (local.tee $n (i32.sub (local.get $n) (i32.const 1)))))
                (i64.add (local.get $sum) (global.get $calls)))
              (func (export "mismatch") (result i32) (call_indirect $table (type $other) (i32.const 1) (i32.const 0)))
              (func (export "uninitialized") (result i64) (call_indirect $table (type $plain) (i64.const 1) (i32.const 3)))
              (func (export "undefined") (result i64) (call_indirect $table (type $plain) (i64.const 1) (i32.const 9))))
            """));
        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module, new WasmImports().Define("lib", store.Instantiate(library)));

        // An iteration k returns 1000 + 2k - 1 and 1000 + 2k (the counter of the library), 5, 6 and 7, and counts 2 calls
        Assert.Equal((2017L * 1000) + (2L * 1000 * 1001) + (2 * 1000), instance.GetFunction("run")!.Call(WasmValue.FromI32(1000))[0].I64);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal("indirect call type mismatch", Assert.Throws<WasmTrapException>(() => instance.GetFunction("mismatch")!.Call()).Message);
            Assert.Equal("uninitialized element", Assert.Throws<WasmTrapException>(() => instance.GetFunction("uninitialized")!.Call()).Message);
            Assert.Equal("undefined element", Assert.Throws<WasmTrapException>(() => instance.GetFunction("undefined")!.Call()).Message);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public void Calls_Indirect_ReadTheTableAtEachCall(int threshold)
    {
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = threshold });
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (type $plain (func (param i64) (result i64)))
              (import "env" "triple" (func $triple (type $plain)))
              (table $table 2 funcref)
              (elem (table $table) (i32.const 0) func $add1 $triple)
              (elem declare func $add100)
              (func $add1 (type $plain) (i64.add (local.get 0) (i64.const 1)))
              (func $add100 (type $plain) (i64.add (local.get 0) (i64.const 100)))
              (func $call (export "call") (param $value i64) (param $index i32) (result i64)
                (call_indirect $table (type $plain) (local.get $value) (local.get $index)))
              (func (export "run") (result i64)
                (local $i i32) (local $sum i64)
                (loop $next
                  ;; The element 0 is replaced at the iteration 10, and restored at the iteration 20
                  (if (i32.eq (local.get $i) (i32.const 10)) (then (table.set $table (i32.const 0) (ref.func $add100))))
                  (if (i32.eq (local.get $i) (i32.const 20)) (then (table.set $table (i32.const 0) (ref.func $add1))))
                  (local.set $sum (i64.add (local.get $sum) (call $call (i64.const 1) (i32.const 0))))
                  (local.set $sum (i64.add (local.get $sum) (call $call (i64.const 1) (i32.const 1))))
                  (br_if $next (i32.lt_u (local.tee $i (i32.add (local.get $i) (i32.const 1))) (i32.const 30))))
                (local.get $sum)))
            """));
        using var store = runtime.CreateStore();
        var triple = store.CreateFunction(
            new WasmFunctionType([WasmValueType.I64], [WasmValueType.I64]),
            (arguments, results) => results[0] = WasmValue.FromI64(arguments[0].I64 * 3));
        var instance = store.Instantiate(module, new WasmImports().Define("env", "triple", triple));

        // 20 calls of $add1 (2), 10 calls of $add100 (101), and 30 calls of the host function (3)
        Assert.Equal((20 * 2L) + (10 * 101L) + (30 * 3L), instance.GetFunction("run")!.Call()[0].I64);

        // The index of a 32-bit table is unsigned: -1 is out of the table
        var call = instance.GetFunction("call")!;
        Assert.Equal("undefined element", Assert.Throws<WasmTrapException>(() => call.Call(WasmValue.FromI64(1), WasmValue.FromI32(-1))).Message);
        Assert.Equal("undefined element", Assert.Throws<WasmTrapException>(() => call.Call(WasmValue.FromI64(1), WasmValue.FromI32(2))).Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public void Calls_SmallFunctions_BehaveAsCalls(int threshold)
    {
        // The compiled code inlines these functions: their locals are zero at each call, their returns end the call, and
        // their traps are the ones of the call
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = threshold });
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (memory 1)
              (global $count (mut i32) (i32.const 0))
              (func $accumulate (param i32) (result i32)
                (local $x i32)
                (local.set $x (i32.add (local.get $x) (local.get 0)))
                (local.get $x))
              (func $clamp (param i32) (result i32)
                (if (i32.lt_s (local.get 0) (i32.const 0)) (then (return (i32.const 0))))
                (if (i32.gt_s (local.get 0) (i32.const 10)) (then (return (i32.const 10))))
                (local.get 0))
              (func $store (param i32 i32)
                (i32.store (local.get 0) (local.get 1))
                (global.set $count (i32.add (global.get $count) (i32.const 1))))
              (func $scale (param f64 f32) (result f64)
                (local $t f64)
                (local.set $t (f64.add (local.get $t) (f64.promote_f32 (local.get 1))))
                (f64.mul (local.get 0) (local.get $t)))
              (func $load (param i32) (result i32) (i32.load (local.get 0)))
              (func $fail (param i32) (result i32) (unreachable))
              (func (export "run") (result f64)
                (local $i i32) (local $sum i32) (local $f f64)
                (loop $next
                  (local.set $sum (i32.add (local.get $sum) (call $accumulate (i32.const 3))))
                  (local.set $sum (i32.add (local.get $sum) (call $clamp (i32.sub (local.get $i) (i32.const 5)))))
                  (call $store (i32.mul (local.get $i) (i32.const 4)) (local.get $i))
                  (local.set $f (f64.add (local.get $f) (call $scale (f64.const 2) (f32.const 0.5))))
                  (br_if $next (i32.lt_u (local.tee $i (i32.add (local.get $i) (i32.const 1))) (i32.const 20))))
                (f64.add (f64.add (f64.convert_i32_s (local.get $sum)) (local.get $f))
                  (f64.convert_i32_s (i32.add (call $load (i32.const 76)) (global.get $count)))))
              (func (export "out_of_bounds") (result i32) (call $load (i32.const 65535)))
              (func (export "unreachable") (result i32) (call $fail (i32.const 1))))
            """));
        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module);

        // $accumulate: 20 * 3; $clamp of -5..14: 1 + ... + 10 + 4 * 10; $scale: 20 * 1; the value at 76 (19) + 20 stores
        Assert.Equal((20 * 3) + 55 + 40 + 20 + 19 + 20, instance.GetFunction("run")!.Call()[0].F64);
        Assert.Equal("out of bounds memory access", Assert.Throws<WasmTrapException>(() => instance.GetFunction("out_of_bounds")!.Call()).Message);
        Assert.Equal("unreachable", Assert.Throws<WasmTrapException>(() => instance.GetFunction("unreachable")!.Call()).Message);
    }

    [Fact]
    public void Compilation_LongLoops_ContinueInCompiledCode()
    {
        // The functions are called once: their loops continue in .NET code after 100 iterations (CompilationThreshold * 100)
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = 1 });
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (tag $e (param i32))
              (func (export "sum") (param $n i32) (result f64)
                (local $i i32) (local $total f64)
                (loop $next
                  (local.set $total (f64.add (local.get $total) (f64.convert_i32_s (local.get $i))))
                  (br_if $next (i32.lt_s (local.tee $i (i32.add (local.get $i) (i32.const 1))) (local.get $n))))
                (local.get $total))
              (func (export "count") (param $n i32) (result i32)
                (local $v i32)
                (i32.const 0)
                (loop $next (param i32) (result i32)
                  (local.set $v (i32.add (i32.const 1)))
                  (br_if $next (local.get $v) (i32.lt_s (local.get $v) (local.get $n)))))
              (func $throws (export "throws") (param $n i32) (result i32)
                (local $i i32)
                (loop $next
                  (br_if $next (i32.lt_s (local.tee $i (i32.add (local.get $i) (i32.const 1))) (local.get $n))))
                (throw $e (local.get $i)))
              (func (export "catches") (result i32)
                (block $caught (result i32)
                  (try_table (catch $e $caught)
                    (drop (call $throws (i32.const 1000))))
                  (i32.const -1))))
            """));
        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module);
        Assert.Equal(499_500d, instance.GetFunction("sum")!.Call(WasmValue.FromI32(1000))[0].F64);
        Assert.Equal(1000, instance.GetFunction("count")!.Call(WasmValue.FromI32(1000))[0].I32);
        Assert.Equal(1000, instance.GetFunction("catches")!.Call()[0].I32);

        var compiled = System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;
        Assert.Equal(compiled, ((CodeFunction)instance.GetFunction("sum")!).Code.Osr is not null);
        Assert.Equal(compiled, ((CodeFunction)instance.GetFunction("count")!).Code.Osr is not null);
        Assert.Equal(compiled, ((CodeFunction)instance.GetFunction("throws")!).Code.Osr is not null);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public void Calls_StackOverflow_CanBeCalledAgain(int threshold)
    {
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = threshold });
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (func $runaway (export "runaway") (param i32) (result i32)
                (call $runaway (i32.add (local.get 0) (i32.const 1))))
              (func $sum (export "sum") (param i32) (result i64)
                (if (result i64) (i32.eqz (local.get 0))
                  (then (i64.const 0))
                  (else (i64.add (i64.extend_i32_u (local.get 0)) (call $sum (i32.sub (local.get 0) (i32.const 1))))))))
            """));
        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module);
        for (var i = 0; i < 3; i++)
        {
            var exception = Assert.Throws<WasmTrapException>(() => instance.GetFunction("runaway")!.Call(WasmValue.FromI32(0)));
            Assert.Equal(WasmTrapKind.StackOverflow, exception.TrapKind);
            Assert.Equal(500_500L, instance.GetFunction("sum")!.Call(WasmValue.FromI32(1000))[0].I64);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void HostFunctions_CalledRecursively_KeepTheirArguments(int threshold)
    {
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = threshold });
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (import "env" "outer" (func $outer (param i32 i64) (result i64)))
              (import "env" "leaf" (func $leaf (param i64) (result i64)))
              (func (export "inner") (param i64) (result i64)
                (call $leaf (i64.add (local.get 0) (i64.const 1))))
              (func (export "run") (param i32) (result i64)
                (call $outer (local.get 0) (i64.const 100))))
            """));
        using var store = runtime.CreateStore();
        WasmFunction? run = null;
        WasmFunction? inner = null;
        var outer = store.CreateFunction(
            new WasmFunctionType([WasmValueType.I32, WasmValueType.I64], [WasmValueType.I64]),
            (arguments, results) =>
            {
                // The nested calls use the buffer of the arguments and the results: they must not overwrite these ones
                var nested = arguments[0].I32 == 0
                    ? inner!.Call(arguments[1])[0].I64
                    : run!.Call(WasmValue.FromI32(arguments[0].I32 - 1))[0].I64 + (arguments[0].I32 * 1000);
                results[0] = WasmValue.FromI64(nested + arguments[1].I64);
            });
        var leaf = store.CreateFunction(
            new WasmFunctionType([WasmValueType.I64], [WasmValueType.I64]),
            (arguments, results) => results[0] = WasmValue.FromI64(arguments[0].I64 * 2));
        var instance = store.Instantiate(module, new WasmImports().Define("env", "outer", outer).Define("env", "leaf", leaf));
        run = instance.GetFunction("run");
        inner = instance.GetFunction("inner");

        // run(0) = inner(100) + 100 = 302, run(n) = run(n - 1) + n * 1000 + 100
        Assert.Equal(302L + (210 * 1000) + (20 * 100), run!.Call(WasmValue.FromI32(20))[0].I64);
    }

    [Fact]
    public async Task Documentation_SharedMemory()
    {
        #region WasmEngine_SharedMemory
        using var runtime = new WasmRuntime();
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (import "env" "memory" (memory 1 1 shared))
              (func (export "wait") (result i32)
                (memory.atomic.wait32 (i32.const 0) (i32.const 0) (i64.const -1))))
            """));

        // A shared memory can be imported by the stores of several threads
        using var store = runtime.CreateStore();
        var memory = store.CreateMemory(new WasmMemoryType(minimum: 1, maximum: 1, shared: true));

        var waiter = Task.Run(() =>
        {
            using var waiterStore = runtime.CreateStore();
            var instance = waiterStore.Instantiate(module, [memory]);
            return instance.GetFunction("wait")!.Call()[0].I32; // 0: notified
        });

        // Wait until the other thread waits, then wake it
        while (!waiter.IsCompleted && memory.AtomicNotify(address: 0, count: 1) == 0)
        {
            await Task.Delay(10);
        }

        Assert.Equal(0, await waiter);
        #endregion
    }

    [Fact]
    public void Documentation_Telemetry()
    {
        #region WasmEngine_Telemetry
        // Collect the activities of the current test only (ScopedActivityListener is in Meziantou.Framework)
        using var listener = new ScopedActivityListener(new ScopedActivityListenerOptions
        {
            SourceNames = ["Meziantou.Framework.WasmEngine"],
            SamplingResult = ActivitySamplingResult.AllDataAndRecorded,
        });

        using var runtime = new WasmRuntime();
        var module = runtime.Compile(WasmRuntime.WatToWasm("""(module (func (export "f")))"""));
        using var store = runtime.CreateStore();
        store.Instantiate(module);

        Assert.Equal(["wasm.compile", "wasm.instantiate"], listener.Activities.Select(activity => activity.OperationName));
        var compile = listener.Activities.First();
        Assert.Equal(module.Bytes.Length, compile.GetTagItem("wasm.module.size"));
        #endregion
    }

    [Fact]
    public void Validate_And_Compile_RejectInvalidModules()
    {
        byte[] invalid = [0, 97, 115, 109, 1, 0, 0, 0, 1, 2, 3];
        Assert.False(Runtime.Validate(invalid));
        Assert.Throws<WasmCompileException>(() => Runtime.Compile(invalid));

        var valid = WasmRuntime.WatToWasm("(module)");
        Assert.True(Runtime.Validate(valid));
        Assert.Empty(Runtime.Compile(valid).Exports);
    }

    [Fact]
    public void WatToWasm_InvalidText_Throws()
    {
        Assert.Throws<FormatException>(() => WasmRuntime.WatToWasm("(module (func (export"));
    }

    [Fact]
    public void Module_ExposesImportsExportsAndCustomSections()
    {
        var bytes = WasmRuntime.WatToWasm("""
            (module
              (import "env" "f" (func (param i32) (result i64)))
              (import "env" "m" (memory 1 2 shared))
              (import "env" "t" (table i64 3 externref))
              (import "env" "g" (global (mut f32)))
              (import "env" "e" (tag (param i32)))
              (func (export "h")))
            """);

        // A custom section: id 0, size, name "hello", contents "world"
        byte[] custom = [0, 11, 5, .. "hello"u8, .. "world"u8];
        var module = Runtime.Compile([.. bytes, .. custom]);

        Assert.Equal(["f", "m", "t", "g", "e"], module.Imports.Select(import => import.Name));
        var function = Assert.IsType<WasmFunctionType>(module.Imports[0].Type);
        Assert.Equal([WasmValueType.I32], function.Parameters);
        Assert.Equal([WasmValueType.I64], function.Results);
        var memory = Assert.IsType<WasmMemoryType>(module.Imports[1].Type);
        Assert.Equal((1UL, (ulong?)2UL, false, true), (memory.Minimum, memory.Maximum, memory.Is64, memory.Shared));
        var table = Assert.IsType<WasmTableType>(module.Imports[2].Type);
        Assert.Equal((WasmValueType.ExternRef, 3UL, (ulong?)null, true), (table.ElementType, table.Minimum, table.Maximum, table.Is64));
        var global = Assert.IsType<WasmGlobalType>(module.Imports[3].Type);
        Assert.Equal((WasmValueType.F32, true), (global.ValueType, global.Mutable));
        Assert.Equal([WasmValueType.I32], Assert.IsType<WasmTagType>(module.Imports[4].Type).Parameters);

        var export = Assert.Single(module.Exports);
        Assert.Equal(("h", WasmExternKind.Function), (export.Name, export.Type.Kind));
        Assert.Equal("world", Encoding.ASCII.GetString(Assert.Single(module.GetCustomSections("hello")).Span));
        Assert.Empty(module.GetCustomSections("other"));
    }

    [Fact]
    public void WatToWasm_CustomAnnotations_AreCustomSections()
    {
        using var runtime = new WasmRuntime();
        var module = runtime.Compile(WasmRuntime.WatToWasm("""(module (@custom "a" "1") (@"custom" "b" (before first) "2" "3") (func) (@other "c"))"""));
        Assert.Equal("1", Encoding.ASCII.GetString(Assert.Single(module.GetCustomSections("a")).Span));
        Assert.Equal("23", Encoding.ASCII.GetString(Assert.Single(module.GetCustomSections("b")).Span));
        Assert.Empty(module.GetCustomSections("c"));
    }

    [Fact]
    public void Instantiate_MissingOrMismatchedImport_ThrowsLinkException()
    {
        var module = Compile("""(module (import "env" "f" (func (param i32))))""");
        using var store = Runtime.CreateStore();

        var missing = Assert.Throws<WasmLinkException>(() => store.Instantiate(module, new WasmImports()));
        Assert.Contains("'env'.'f'", missing.Message, StringComparison.Ordinal);

        var wrongType = store.CreateFunction(new WasmFunctionType([WasmValueType.I64], []), (_, _) => { });
        Assert.Throws<WasmLinkException>(() => store.Instantiate(module, [wrongType]));
        Assert.Throws<WasmLinkException>(() => store.Instantiate(module));
    }

    [Fact]
    public void Instantiate_ModuleOfAnotherRuntime_Throws()
    {
        using var otherRuntime = new WasmRuntime();
        var module = otherRuntime.Compile(WasmRuntime.WatToWasm("(module)"));
        using var store = Runtime.CreateStore();
        Assert.Throws<ArgumentException>(() => store.Instantiate(module));
    }

    [Fact]
    public void Instance_ReturnsTheSameObjectsForTheSameAddresses()
    {
        var module = Compile("""
            (module
              (func $f (export "f"))
              (export "g" (func $f))
              (memory (export "m") 1))
            """);
        using var store = Runtime.CreateStore();
        var instance = store.Instantiate(module);

        Assert.Same(instance.GetFunction("f"), instance.GetFunction("g"));
        Assert.Same(instance.GetMemory("m"), instance.GetMemory("m"));
        Assert.Null(instance.GetFunction("m"));
        Assert.Null(instance.GetExport("missing"));
    }

    [Fact]
    public void HostFunction_ExceptionIsThrownByTheCall()
    {
        var module = Compile("""
            (module
              (import "env" "f" (func $f))
              (func (export "run") (call $f)))
            """);
        using var store = Runtime.CreateStore();
        var state = new object();
        var function = store.CreateFunction(new WasmFunctionType([], []), (_, _) => throw new InvalidOperationException("host failure"), state);
        Assert.Same(state, function.HostState);

        var run = store.Instantiate(module, [function]).GetFunction("run")!;
        var exception = Assert.Throws<InvalidOperationException>(() => run.Call());
        Assert.Equal("host failure", exception.Message);
    }

    [Fact]
    public void ExternRef_KeepsTheHostValue()
    {
        var module = Compile("""(module (func (export "identity") (param externref) (result externref) (local.get 0)))""");
        using var store = Runtime.CreateStore();
        var identity = store.Instantiate(module).GetFunction("identity")!;

        var value = new object();
        var result = Assert.IsType<WasmHostReference>(identity.Call(WasmValue.FromHostValue(value))[0].Reference);
        Assert.Same(value, result.Value);
        Assert.Null(identity.Call(WasmValue.Null)[0].Reference);
    }

    [Fact]
    public void Invoke_WrongArguments_ThrowsTypeMismatch()
    {
        var module = Compile("""(module (func (export "f") (param i32)))""");
        using var store = Runtime.CreateStore();
        var function = store.Instantiate(module).GetFunction("f")!;
        Assert.Throws<WasmTypeMismatchException>(() => function.Call());
        Assert.Throws<WasmTypeMismatchException>(() => function.Invoke([WasmValue.FromI32(1)], new WasmValue[1]));
    }

    [Fact]
    public void Recursion_TrapsWithStackOverflow()
    {
        var module = Compile("""(module (func $f (export "f") (call $f)))""");
        using var store = Runtime.CreateStore();
        var exception = Assert.Throws<WasmTrapException>(() => store.Instantiate(module).GetFunction("f")!.Call());
        Assert.Equal(WasmTrapKind.StackOverflow, exception.TrapKind);
    }

    [Fact]
    public void Memory_GrowBeyondMaximum_Throws()
    {
        using var store = Runtime.CreateStore();
        var memory = store.CreateMemory(new WasmMemoryType(minimum: 1, maximum: 2));
        Assert.Equal(1UL, memory.Grow(1));
        Assert.Throws<WasmAllocationException>(() => memory.Grow(1));
        Assert.Equal(2UL, memory.GrowTo(1));
        Assert.Null(memory.SharedBlock);
        Assert.Throws<NotSupportedException>(() => memory.AtomicNotify(0, 1));
    }

    [Fact]
    public void Memory_MaxMemorySize_LimitsTheGrowth()
    {
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { MaxMemorySize = 2 * 65536 });
        using var store = runtime.CreateStore();
        var memory = store.CreateMemory(new WasmMemoryType(minimum: 1, maximum: null));
        memory.Grow(1);
        Assert.Throws<WasmAllocationException>(() => memory.Grow(1));
    }

    [Fact]
    public void SharedMemory_UsesTheAllocatorOfTheStore()
    {
        var allocations = new List<(ulong ByteLength, ulong Capacity)>();
        using var store = Runtime.CreateStore(new WasmStoreOptions
        {
            SharedMemoryAllocator = (byteLength, capacity) =>
            {
                allocations.Add((byteLength, capacity));
                return new TestSharedMemoryBlock(byteLength, capacity);
            },
        });

        var memory = store.CreateMemory(new WasmMemoryType(minimum: 1, maximum: 3, shared: true));
        Assert.Equal([(65536UL, 3 * 65536UL)], allocations);
        var block = Assert.IsType<TestSharedMemoryBlock>(memory.SharedBlock);
        Assert.Throws<NotSupportedException>(() => memory.Data);

        memory.GetSpan()[10] = 42;
        Assert.Equal(42, block.GetSpan()[10]);
        Assert.Equal(1UL, memory.GrowTo(2));
        Assert.Equal(2 * 65536UL, block.ByteLength);
        Assert.Equal(WasmWaitResult.NotEqual, memory.AtomicWait(16, expected: 1, size: 4, TimeSpan.Zero));
        Assert.Equal(WasmWaitResult.TimedOut, memory.AtomicWait(16, expected: 0, size: 4, TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void SharedMemory_AllocatorException_IsThrownByTheOperation()
    {
        using var store = Runtime.CreateStore(new WasmStoreOptions
        {
            SharedMemoryAllocator = (_, _) => throw new InvalidOperationException("no shared memory"),
        });

        var exception = Assert.Throws<InvalidOperationException>(() => store.CreateMemory(new WasmMemoryType(minimum: 1, maximum: 1, shared: true)));
        Assert.Equal("no shared memory", exception.Message);
    }

    [Fact]
    public void Table_GetSetGrow()
    {
        using var store = Runtime.CreateStore();
        var table = store.CreateTable(new WasmTableType(WasmValueType.ExternRef, minimum: 1, maximum: 3), WasmValue.Null);
        Assert.Equal(1UL, table.Grow(1, WasmValue.FromHostValue("a")));
        Assert.Equal("a", ((WasmHostReference)table.Get(1).Reference!).Value);
        Assert.Throws<WasmAllocationException>(() => { table.Get(2); });
        Assert.Throws<WasmAllocationException>(() => table.Grow(2, WasmValue.Null));
        Assert.Throws<WasmTypeMismatchException>(() => table.Set(0, WasmValue.FromReference(new object())));
    }

    [Fact]
    public void Global_Immutable_CannotBeSet()
    {
        using var store = Runtime.CreateStore();
        var global = store.CreateGlobal(new WasmGlobalType(WasmValueType.I64, mutable: false), WasmValue.FromI64(5));
        Assert.Equal(5, global.Get().I64);
        Assert.Throws<WasmTypeMismatchException>(() => global.Set(WasmValue.FromI64(6)));
    }

    [Fact]
    public void GcArrays_AreReadAndWritten()
    {
        var module = Compile("""
            (module
              (type $chars (array (mut i16)))
              (import "env" "f" (func (param (ref null $chars))))
              (func (export "new") (result (ref $chars)) (array.new $chars (i32.const 65) (i32.const 3)))
              (func (export "i31") (result i31ref) (ref.i31 (i32.const -5))))
            """);
        var import = Assert.IsType<WasmFunctionType>(module.Imports[0].Type);
        Assert.True(module.IsMutableI16ArrayParameter(import, 0));
        Assert.False(module.IsMutableI16ArrayParameter(import, 1));

        using var store = Runtime.CreateStore();
        var f = store.CreateFunction(import, (_, _) => { });
        var instance = store.Instantiate(module, [f]);
        var array = Assert.IsAssignableTo<WasmGcObject>(instance.GetFunction("new")!.Call()[0].Reference);
        Assert.True(array.IsArray);
        Assert.Equal(3, store.GetArrayLength(array));
        store.SetArrayElement(array, 1, WasmValue.FromI32(66), WasmValueType.I32);
        Assert.Equal(66, store.GetArrayElement(array, 1).I32);
        Assert.Throws<WasmTrapException>(() => { store.GetArrayElement(array, 3); });

        Assert.Equal(-5, Assert.IsType<WasmI31Reference>(instance.GetFunction("i31")!.Call()[0].Reference).Value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void LegacyExceptionHandling_IsSupported(int threshold)
    {
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = threshold });
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (tag $e (param i32))
              (tag $f)
              (func $throw (param i32) (throw $e (local.get 0)))

              ;; catch receives the values of the tag, catch_all the exceptions of the other tags
              (func (export "catch") (param i32) (result i32)
                (try (result i32)
                  (do
                    (if (local.get 0) (then (throw $f)))
                    (call $throw (i32.const 41))
                    (i32.const 0))
                  (catch $e (i32.add (i32.const 1)))
                  (catch_all (i32.const -1))))

              ;; A try without handler is a block, and a handler can branch out of its try
              (func (export "branch") (result i32)
                (block $out (result i32)
                  (try (do (nop)))
                  (try (result i32)
                    (do (throw $e (i32.const 3)))
                    (catch $e (br $out (i32.mul (i32.const 2)))))))

              ;; The rethrow of an enclosing handler throws its exception
              (func (export "rethrow") (result i32)
                (try (result i32)
                  (do
                    (try
                      (do (throw $e (i32.const 5)))
                      (catch $e
                        (drop)
                        (try (do (throw $f)) (catch_all (rethrow 1)))))
                    (i32.const 0))
                  (catch $e)))

              ;; A delegate gives the exception to the handlers of its label: the outer try, or the try that encloses the if
              (func (export "delegate") (param i32) (result i32)
                (try (result i32)
                  (do
                    (try (result i32)
                      (do
                        (if (local.get 0)
                          (then (try (do (throw $e (i32.const 7))) (delegate 2)))
                          (else (try (do (throw $e (i32.const 7))) (delegate 1))))
                        (i32.const 0))
                      (catch $e (i32.add (i32.const 100)))))
                  (catch $e (i32.add (i32.const 1000)))))

              ;; A delegate to the label of the function gives the exception to the caller
              (func $toCaller (try (do (throw $e (i32.const 9))) (delegate 0)))
              (func (export "caller") (result i32)
                (try (result i32) (do (call $toCaller) (i32.const 0)) (catch $e)))

              ;; A delegate in a handler gives the exception to the handlers that enclose the try of the handler
              (func (export "fromHandler") (result i32)
                (try (result i32)
                  (do
                    (try (result i32)
                      (do (throw $f))
                      (catch_all
                        (try (do (throw $e (i32.const 11))) (delegate 0))
                        (i32.const 0))))
                  (catch $e))))
            """));
        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module);
        Assert.Equal(42, instance.GetFunction("catch")!.Call(WasmValue.FromI32(0))[0].I32);
        Assert.Equal(-1, instance.GetFunction("catch")!.Call(WasmValue.FromI32(1))[0].I32);
        Assert.Equal(6, instance.GetFunction("branch")!.Call()[0].I32);
        Assert.Equal(5, instance.GetFunction("rethrow")!.Call()[0].I32);
        Assert.Equal(1007, instance.GetFunction("delegate")!.Call(WasmValue.FromI32(1))[0].I32);
        Assert.Equal(107, instance.GetFunction("delegate")!.Call(WasmValue.FromI32(0))[0].I32);
        Assert.Equal(9, instance.GetFunction("caller")!.Call()[0].I32);
        Assert.Equal(11, instance.GetFunction("fromHandler")!.Call()[0].I32);
    }

    [Theory]
    [InlineData("""(module (func (rethrow 0)))""")]
    [InlineData("""(module (func (block (rethrow 0))))""")]
    [InlineData("""(module (func (try (do (rethrow 0)) (delegate 0))))""")]
    [InlineData("""(module (func (try (do) (delegate 1))))""")]
    [InlineData("""(module (tag) (func (try (do) (catch 0 (i32.const 42)))))""")]
    public void LegacyExceptionHandling_InvalidModulesAreRejected(string text)
    {
        var bytes = WasmRuntime.WatToWasm(text);
        Assert.False(Runtime.Validate(bytes));
        Assert.Throws<WasmCompileException>(() => Runtime.Compile(bytes));
    }

    [Fact]
    public void Runtime_Disposed_Throws()
    {
        var runtime = new WasmRuntime();
        runtime.Dispose();
        Assert.Throws<ObjectDisposedException>(() => runtime.Compile(WasmRuntime.WatToWasm("(module)")));
        Assert.Throws<ObjectDisposedException>(() => runtime.CreateStore());
    }

    [Fact]
    public void Values_AreComparedAndFormatted()
    {
        Assert.Equal(WasmValue.FromI32(1), WasmValue.FromI32(1));
        Assert.NotEqual(WasmValue.FromI32(1), WasmValue.FromI64(1));
        Assert.Equal(WasmValue.FromF64Bits(BitConverter.DoubleToInt64Bits(double.NaN)), WasmValue.FromF64(double.NaN));
        Assert.Equal("i32:42", WasmValue.FromI32(42).ToString());
        Assert.Equal("ref.null", WasmValue.Null.ToString());
        Assert.Equal("funcref", WasmValueType.FuncRef.ToString());
        Assert.Equal("(ref any)", new WasmValueType(WasmValueKind.Ref, WasmHeapKind.Any, Nullable: false).ToString());
        Assert.True(WasmValueType.ExnRef.IsExceptionReference);
        Assert.Equal(WasmValueKind.I64, WasmValue.Default(WasmValueType.I64).Kind);
    }

    [Fact]
    public void Telemetry_RecordsTheMetrics()
    {
        using var meterFactory = new TestMeterFactory();
        using var collector = new MetricCollector(meterFactory);
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { MeterFactory = meterFactory });

        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (memory (export "memory") 1 2)
              (func (export "trap") unreachable)
              (func $f (export "recurse") (call $f))
              (func (export "loop") (loop $l (br $l))))
            """));
        Assert.Throws<WasmCompileException>(() => runtime.Compile([1, 2, 3]));
        Assert.Equal(2, collector.Count(WasmEngineTelemetry.CompileDurationName));
        Assert.Equal(1, collector.Count(WasmEngineTelemetry.CompileDurationName, WasmEngineTelemetry.ErrorTypeTagName, typeof(WasmCompileException).FullName));
        Assert.Equal(1, collector.Count(WasmEngineTelemetry.ModuleSizeName));

        var interrupt = false;
        var store = runtime.CreateStore(new WasmStoreOptions
        {
            CheckInterrupt = () =>
            {
                if (Volatile.Read(ref interrupt))
                    throw new OperationCanceledException();
            },
        });
        Assert.Equal(1, collector.Sum(WasmEngineTelemetry.ActiveStoresName));

        var instance = store.Instantiate(module);
        Assert.Throws<WasmLinkException>(() => store.Instantiate(runtime.Compile(WasmRuntime.WatToWasm("""(module (import "a" "b" (func)))""")), Array.Empty<WasmExtern>()));
        Assert.Equal(2, collector.Count(WasmEngineTelemetry.InstantiateDurationName));
        Assert.Equal(1, collector.Count(WasmEngineTelemetry.InstantiateDurationName, WasmEngineTelemetry.ErrorTypeTagName, typeof(WasmLinkException).FullName));
        Assert.Equal(65536, collector.Sum(WasmEngineTelemetry.MemoryUsageName));

        instance.GetMemory("memory")!.Grow(1);
        Assert.Equal(2 * 65536, collector.Sum(WasmEngineTelemetry.MemoryUsageName));

        Assert.Throws<WasmTrapException>(() => instance.GetFunction("trap")!.Call());
        Assert.Throws<WasmTrapException>(() => instance.GetFunction("recurse")!.Call());
        Assert.Equal(1, collector.Count(WasmEngineTelemetry.TrapsName, WasmEngineTelemetry.TrapKindTagName, "other"));
        Assert.Equal(1, collector.Count(WasmEngineTelemetry.TrapsName, WasmEngineTelemetry.TrapKindTagName, "stack_overflow"));

        Volatile.Write(ref interrupt, true);
        Assert.Throws<OperationCanceledException>(() => instance.GetFunction("loop")!.Call());
        Assert.Equal(1, collector.Sum(WasmEngineTelemetry.InterruptionsName));

        store.Dispose();
        Assert.Equal(0, collector.Sum(WasmEngineTelemetry.ActiveStoresName));
    }

    [Fact]
    public void Telemetry_FailedOperations_HaveAnErrorStatus()
    {
        using var listener = new ScopedActivityListener(new ScopedActivityListenerOptions
        {
            SourceNames = [WasmEngineTelemetry.SourceName],
            SamplingResult = ActivitySamplingResult.AllDataAndRecorded,
        });

        Assert.Throws<WasmCompileException>(() => Runtime.Compile([1, 2, 3]));
        using var store = Runtime.CreateStore();
        Assert.Throws<WasmTrapException>(() => store.Instantiate(Compile("""(module (func unreachable) (start 0))""")));

        var activities = listener.Activities.ToList();
        Assert.Equal(["wasm.compile", "wasm.compile", "wasm.instantiate"], activities.Select(activity => activity.OperationName));
        Assert.Equal(ActivityStatusCode.Error, activities[0].Status);
        Assert.Equal(typeof(WasmCompileException).FullName, activities[0].GetTagItem(WasmEngineTelemetry.ErrorTypeTagName));
        Assert.Equal(ActivityStatusCode.Unset, activities[1].Status);
        Assert.Equal(ActivityStatusCode.Error, activities[2].Status);
        Assert.Equal(typeof(WasmTrapException).FullName, activities[2].GetTagItem(WasmEngineTelemetry.ErrorTypeTagName));
    }

    private sealed class TestSharedMemoryBlock : WasmSharedMemoryBlock
    {
        private readonly byte[] _data;
        private ulong _byteLength;

        public TestSharedMemoryBlock(ulong byteLength, ulong capacity)
            : this(GC.AllocateArray<byte>(checked((int)capacity), pinned: true), byteLength)
        {
        }

        private TestSharedMemoryBlock(byte[] data, ulong byteLength)
            : base(System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(data, 0), (ulong)data.Length)
        {
            _data = data;
            _byteLength = byteLength;
        }

        public override ulong ByteLength => Volatile.Read(ref _byteLength);

        public override bool TryGrow(ulong newByteLength)
        {
            if (newByteLength > (ulong)_data.Length || newByteLength < ByteLength)
                return false;

            Volatile.Write(ref _byteLength, newByteLength);
            return true;
        }
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options.Name, options.Version, options.Tags, scope: this);
            lock (_meters)
            {
                _meters.Add(meter);
            }

            return meter;
        }

        public void Dispose()
        {
            lock (_meters)
            {
                foreach (var meter in _meters)
                {
                    meter.Dispose();
                }
            }
        }
    }

    /// <summary>Collects the measurements of the meters of a factory.</summary>
    private sealed class MetricCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Instrument, double Value, KeyValuePair<string, object?>[] Tags)> _measurements = [];

        public MetricCollector(IMeterFactory meterFactory)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Scope == meterFactory)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));
            _listener.Start();
        }

        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            lock (_measurements)
            {
                _measurements.Add((instrument.Name, value, tags.ToArray()));
            }
        }

        private List<(string Instrument, double Value, KeyValuePair<string, object?>[] Tags)> Get(string instrument, string? tagName, object? tagValue)
        {
            lock (_measurements)
            {
                return [.. _measurements.Where(item => item.Instrument == instrument && (tagName is null || item.Tags.Any(tag => tag.Key == tagName && Equals(tag.Value, tagValue))))];
            }
        }

        public int Count(string instrument, string? tagName = null, object? tagValue = null) => Get(instrument, tagName, tagValue).Count;

        public double Sum(string instrument, string? tagName = null, object? tagValue = null) => Get(instrument, tagName, tagValue).Sum(item => item.Value);

        public void Dispose() => _listener.Dispose();
    }
}
