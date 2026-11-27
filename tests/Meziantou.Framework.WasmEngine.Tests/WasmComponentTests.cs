using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework.WasmEngine.Components;

namespace Meziantou.Framework.WasmEngine.Tests;

public sealed class WasmComponentTests
{
    [Fact]
    public void GettingStarted()
    {
        #region Component_GettingStarted
        using var runtime = new WasmRuntime();

        // A component that exports greet(name: string) -> string: the canonical ABI copies the strings between the
        // memory of the component and .NET, the component allocates its memory with its realloc function
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm("""
            (component
              (core module $M
                (memory (export "memory") 1)
                (global $heap (mut i32) (i32.const 1024))
                (func $realloc (export "realloc") (param i32 i32 i32 i32) (result i32)
                  (local $pointer i32)
                  (local.set $pointer (global.get $heap))
                  (global.set $heap (i32.add (global.get $heap) (local.get 3)))
                  (local.get $pointer))
                (data (i32.const 16) "Hello, ")
                (func (export "greet") (param $name i32) (param $length i32) (result i32)
                  (local $result i32)
                  (local.set $result (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.add (local.get $length) (i32.const 7))))
                  (memory.copy (local.get $result) (i32.const 16) (i32.const 7))
                  (memory.copy (i32.add (local.get $result) (i32.const 7)) (local.get $name) (local.get $length))
                  ;; The result is the address and the length of the string, at the address 0
                  (i32.store (i32.const 0) (local.get $result))
                  (i32.store (i32.const 4) (i32.add (local.get $length) (i32.const 7)))
                  (i32.const 0)))
              (core instance $m (instantiate $M))
              (func (export "greet") (param "name" string) (result string)
                (canon lift (core func $m "greet") (memory (core memory $m "memory")) (realloc (core func $m "realloc")))))
            """));

        using var store = runtime.CreateStore();
        var instance = store.InstantiateComponent(component);
        var greet = instance.GetFunction("greet")!;
        var result = greet.Call(ComponentValue.FromString("World"));

        Assert.Equal("Hello, World", result?.AsString());
        #endregion
    }

    [Fact]
    public void HostFunctions()
    {
        #region Component_HostFunctions
        using var runtime = new WasmRuntime();

        // A component that imports add(a: u32, b: u32) -> u32 and calls it from its export run
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm("""
            (component
              (import "add" (func $add (param "a" u32) (param "b" u32) (result u32)))
              (core func $add-core (canon lower (func $add)))
              (core module $M
                (import "host" "add" (func $add (param i32 i32) (result i32)))
                (func (export "run") (result i32)
                  (call $add (i32.const 40) (i32.const 2))))
              (core instance $m (instantiate $M (with "host" (instance (export "add" (func $add-core))))))
              (func (export "run") (result u32) (canon lift (core func $m "run"))))
            """));

        // The functions of the host take the values of the parameters, and return the value of the result (or null)
        var imports = new ComponentImports()
            .Define("add", ComponentFunction.FromCallback((context, arguments) => ComponentValue.FromU32(arguments[0].AsU32() + arguments[1].AsU32())));

        using var store = runtime.CreateStore();
        var instance = store.InstantiateComponent(component, imports);
        var result = instance.GetFunction("run")!.Call();

        Assert.Equal(42u, result?.AsU32());
        #endregion
    }

    [Fact]
    public void Resources()
    {
        #region Component_Resources
        using var runtime = new WasmRuntime();

        // A component that creates a counter of the host, increments it twice, and drops it
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm("""
            (component
              (import "counter" (type $counter (sub resource)))
              (import "[constructor]counter" (func $new (result (own $counter))))
              (import "[method]counter.increment" (func $increment (param "self" (borrow $counter)) (result u32)))
              (core func $new-core (canon lower (func $new)))
              (core func $increment-core (canon lower (func $increment)))
              (core func $drop (canon resource.drop $counter))
              (core module $M
                (import "host" "new" (func $new (result i32)))
                (import "host" "increment" (func $increment (param i32) (result i32)))
                (import "host" "drop" (func $drop (param i32)))
                (func (export "run") (result i32)
                  (local $counter i32)
                  (local $value i32)
                  (local.set $counter (call $new))
                  (drop (call $increment (local.get $counter)))
                  (local.set $value (call $increment (local.get $counter)))
                  (call $drop (local.get $counter))
                  (local.get $value)))
              (core instance $m (instantiate $M (with "host" (instance
                (export "new" (func $new-core))
                (export "increment" (func $increment-core))
                (export "drop" (func $drop))))))
              (func (export "run") (result u32) (canon lift (core func $m "run"))))
            """));

        // A resource of the host is a .NET object (its representation): the destructor runs when the component drops it
        var dropped = 0;
        var counterType = new ComponentResourceType("counter", resource => dropped++);
        var imports = new ComponentImports()
            .Define("counter", counterType)
            .Define("[constructor]counter", ComponentFunction.FromCallback((context, arguments) => ComponentValue.FromResource(new ComponentResource(counterType, new StrongBox<uint>()))))
            .Define("[method]counter.increment", ComponentFunction.FromCallback((context, arguments) =>
            {
                var counter = (StrongBox<uint>)arguments[0].AsResource().Representation!;
                counter.Value++;
                return ComponentValue.FromU32(counter.Value);
            }));

        using var store = runtime.CreateStore();
        var instance = store.InstantiateComponent(component, imports);
        var result = instance.GetFunction("run")!.Call();

        Assert.Equal(2u, result?.AsU32());
        Assert.Equal(1, dropped);
        #endregion
    }

    [Fact]
    public void AsyncHostFunctions()
    {
        #region Component_AsyncHostFunctions
        using var runtime = new WasmRuntime();

        // A component that calls the asynchronous function double of the host: the call blocks the asynchronous task of
        // the component until the function completes, while the store runs the other tasks and the continuations of the host
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm("""
            (component
              (import "double" (func $double async (param "value" u32) (result u32)))
              (core func $double-core (canon lower (func $double)))
              (core module $M
                (import "host" "double" (func $double (param i32) (result i32)))
                (func (export "run") (result i32)
                  (call $double (i32.const 21))))
              (core instance $m (instantiate $M (with "host" (instance (export "double" (func $double-core))))))
              (func (export "run") async (result u32) (canon lift (core func $m "run"))))
            """));

        var imports = new ComponentImports()
            .Define("double", ComponentFunction.FromAsyncCallback(async (context, arguments) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), context.CancellationToken);
                return ComponentValue.FromU32(arguments[0].AsU32() * 2);
            }));

        using var store = runtime.CreateStore();
        var instance = store.InstantiateComponent(component, imports);
        var result = instance.GetFunction("run")!.Call();

        Assert.Equal(42u, result?.AsU32());
        #endregion
    }

    /// <summary>
    /// A component whose export echo calls the import echo of the host with its arguments: the values go from .NET to the
    /// memory of the component (lift), back to .NET (lower), and back to the component and to .NET with the result.
    /// </summary>
    private static ComponentValue? Echo(string type, string coreParameters, ComponentValue value, string definitions = "")
    {
        using var runtime = new WasmRuntime();
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm($$"""
            (component
              (core module $Memory
                (memory (export "memory") 1)
                (global $heap (mut i32) (i32.const 1024))
                (func (export "realloc") (param i32 i32 i32 i32) (result i32)
                  (local $pointer i32)
                  ;; Aligned on 8 bytes, the content of the previous allocation copied
                  (local.set $pointer (i32.and (i32.add (global.get $heap) (i32.const 7)) (i32.const -8)))
                  (global.set $heap (i32.add (local.get $pointer) (local.get 3)))
                  (memory.copy (local.get $pointer) (local.get 0) (select (local.get 1) (local.get 3) (i32.lt_u (local.get 1) (local.get 3))))
                  (local.get $pointer)))
              (core instance $memory (instantiate $Memory))
              {{definitions}}
              (type $t-definition {{type}})
              (import "t" (type $t (eq $t-definition)))
              (import "echo" (func $echo (param "value" $t) (result $t)))
              (core func $echo-core (canon lower (func $echo) (memory (core memory $memory "memory")) (realloc (core func $memory "realloc"))))
              (core module $M
                (import "host" "echo" (func $echo (param {{coreParameters}} i32)))
                (func (export "echo") (param {{coreParameters}}) (result i32)
                  {{string.Join(' ', Enumerable.Range(0, coreParameters.Split(' ').Length).Select(i => "(local.get " + i + ")"))}}
                  (call $echo (i32.const 8))
                  (i32.const 8)))
              (core instance $m (instantiate $M (with "host" (instance (export "echo" (func $echo-core))))))
              (func (export "echo") (param "value" $t) (result $t)
                (canon lift (core func $m "echo") (memory (core memory $memory "memory")) (realloc (core func $memory "realloc")))))
            """));

        var received = new List<ComponentValue>();
        var imports = new ComponentImports().Define("echo", ComponentFunction.FromCallback((context, arguments) =>
        {
            received.Add(arguments[0]);
            return arguments[0];
        }));

        using var store = runtime.CreateStore();
        var instance = store.InstantiateComponent(component, imports);
        var result = instance.GetFunction("echo")!.Call(value);
        Assert.Equal(value, Assert.Single(received));
        return result;
    }

    [Fact]
    public void Values_Record()
    {
        var value = ComponentValue.FromRecord(ComponentValue.FromU32(42), ComponentValue.FromString("héllo ☃"), ComponentValue.FromBytes([1, 2, 3]));
        Assert.Equal(value, Echo("""(record (field "a" u32) (field "b" string) (field "c" (list u8)))""", "i32 i32 i32 i32 i32", value));
    }

    [Fact]
    public void Values_VariantsAndOptions()
    {
        const string Type = "(result (option f64) (error string))";
        const string CoreParameters = "i32 i32 i64";
        foreach (var value in new[]
        {
            ComponentValue.Ok(ComponentValue.Some(ComponentValue.FromF64(1.5))),
            ComponentValue.Ok(ComponentValue.NoneValue),
            ComponentValue.Error(ComponentValue.FromString("failure")),
        })
        {
            Assert.Equal(value, Echo(Type, CoreParameters, value));
        }
    }

    [Fact]
    public void Values_Tuple()
    {
        var value = ComponentValue.FromTuple(
            ComponentValue.FromChar(new Rune('☃')),
            ComponentValue.FromFlags(["a", "c"]),
            ComponentValue.FromEnum("green"),
            ComponentValue.FromList([ComponentValue.FromString("x"), ComponentValue.FromString("yz")]),
            ComponentValue.FromS64(-5),
            ComponentValue.FromBool(true));
        Assert.Equal(value, Echo("(tuple char $flags $color (list string) s64 bool)", "i32 i32 i32 i32 i32 i64 i32", value, definitions: """
            (type $flags-definition (flags "a" "b" "c"))
            (import "flags" (type $flags (eq $flags-definition)))
            (type $color-definition (enum "red" "green"))
            (import "color" (type $color (eq $color-definition)))
            """));
    }

    [Fact]
    public void Values_HostValueOfTheWrongType()
    {
        Assert.Throws<WasmTypeMismatchException>(() => Echo("(list u8)", "i32 i32", ComponentValue.FromString("not a list")));
    }

    [Fact]
    public void Trap_LocksDownTheStore()
    {
        using var runtime = new WasmRuntime();
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm("""
            (component
              (core module $M
                (func (export "fail") unreachable)
                (func (export "ok")))
              (core instance $m (instantiate $M))
              (func (export "fail") (canon lift (core func $m "fail")))
              (func (export "ok") (canon lift (core func $m "ok"))))
            """));

        using var store = runtime.CreateStore();
        var instance = store.InstantiateComponent(component);
        instance.GetFunction("ok")!.Call();
        Assert.Throws<WasmTrapException>(() => instance.GetFunction("fail")!.Call());
        Assert.Throws<WasmTrapException>(() => instance.GetFunction("ok")!.Call());
    }

    [Fact]
    public void MissingImport()
    {
        using var runtime = new WasmRuntime();
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm("""
            (component
              (import "f" (func)))
            """));

        Assert.Equal([new WasmComponentImport("f", ComponentExternKind.Function)], component.Imports);
        using var store = runtime.CreateStore();
        Assert.Throws<WasmLinkException>(() => store.InstantiateComponent(component));
    }

    [Fact]
    public void ImportOfTheWrongType()
    {
        using var runtime = new WasmRuntime();
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm("""
            (component
              (import "f" (func (param "x" u32))))
            """));

        var imports = new ComponentImports().Define("f", ComponentFunction.FromCallback((context, arguments) => null, new ComponentFunctionType([], result: null, isAsync: false)));
        using var store = runtime.CreateStore();
        Assert.Throws<WasmLinkException>(() => store.InstantiateComponent(component, imports));
    }

    [Fact]
    public void IsComponent()
    {
        Assert.True(WasmRuntime.IsComponent(WasmRuntime.WatToWasm("(component)")));
        Assert.False(WasmRuntime.IsComponent(WasmRuntime.WatToWasm("(module)")));
        Assert.False(WasmRuntime.IsComponent([]));
    }

    [Fact]
    public void InvalidComponent()
    {
        using var runtime = new WasmRuntime();
        Assert.Throws<WasmCompileException>(() => runtime.CompileComponent([0x00, 0x61, 0x73, 0x6D, 0x0D, 0x00, 0x01, 0x00, 0xFF]));
    }

    [Fact]
    public void Validation()
    {
        #region Component_Validation
        using var runtime = new WasmRuntime();

        // The exported function returns a record type that the component does not export: its users cannot name it
        var invalid = WasmRuntime.WatToWasm("""
            (component
              (core module $M (func (export "f") (result i32) (i32.const 0)))
              (core instance $m (instantiate $M))
              (type $point (record (field "x" u32)))
              (func $f (result $point) (canon lift (core func $m "f")))
              (export "f" (func $f)))
            """);

        var exception = Assert.Throws<WasmCompileException>(() => runtime.CompileComponent(invalid));
        Assert.Equal("func not valid to be used as export: `f`", exception.Message);

        // Exporting the type gives it a name: the function uses the exported type
        var valid = WasmRuntime.WatToWasm("""
            (component
              (core module $M (func (export "f") (result i32) (i32.const 0)))
              (core instance $m (instantiate $M))
              (type $point (record (field "x" u32)))
              (export $point-export "point" (type $point))
              (func $f (result $point-export) (canon lift (core func $m "f")))
              (export "f" (func $f)))
            """);

        var component = runtime.CompileComponent(valid);
        Assert.Equal(["point", "f"], component.Exports.Select(export => export.Name));
        #endregion
    }

    [Theory]
    [InlineData("""(component (import "Foo" (func)))""", "is not in kebab case")]
    [InlineData("""(component (import "foo-bar" (func)) (import "FOOBAR" (func)))""", "conflicts with previous name")]
    [InlineData("""(component (import "a:b/c@1" (instance)))""", "is not valid")]
    [InlineData("""(component (import "a:b/c@1.2.3" (version-suffix "+sha") (instance)))""", "is not the canonical version")]
    [InlineData("""(component (import "r" (type $r (sub resource))) (import "[constructor]r" (func (result u32))))""", "should return `(own $T)`")]
    [InlineData("""(component (import "r" (type $r (sub resource))) (import "[method]r.f" (func (param "x" u32))))""", "should have a first argument called `self`")]
    [InlineData("""(component (import "r" (type $r (sub resource))) (core func (canon resource.new $r)))""", "is not a local resource")]
    [InlineData("""(component (type $r (resource (rep i32))) (type (func (result (borrow $r)))))""", "function result cannot contain a `borrow` type")]
    [InlineData("""(component (type (map f32 u8)))""", "invalid map key type")]
    [InlineData("""(component (type (list u8 268435456)))""", "exceeds maximum byte size")]
    [InlineData("""(component (component $c (import "f" (func))) (instance (instantiate $c)))""", "missing import named `f`")]
    [InlineData("""
        (component
          (import "t1" (type $t1 (sub resource)))
          (import "t2" (type $t2 (sub resource)))
          (component $c (import "a" (type $a (sub resource))) (import "b" (type (eq $a))))
          (instance (instantiate $c (with "a" (type $t1)) (with "b" (type $t2)))))
        """, "resource types are not the same")]
    [InlineData("""
        (component
          (import "f" (func $f (param "x" u32)))
          (component $c (import "f" (func (param "x" s32))))
          (instance (instantiate $c (with "f" (func $f)))))
        """, "type mismatch in function parameter `x`")]
    [InlineData("""(component (import "f" (func $f (param "s" string))) (core func (canon lower (func $f))))""", "canonical option `memory` is required")]
    [InlineData("""
        (component
          (core module $M (func (export "f") (result i32) (i32.const 0)))
          (core instance $m (instantiate $M))
          (func (canon lift (core func $m "f"))))
        """, "do not match result types")]
    [InlineData("""
        (component
          (core module $M (func (export "f")))
          (core instance $m (instantiate $M))
          (func (canon lift (core func $m "f") async)))
        """, "the `async` canonical option requires an async function type")]
    [InlineData("""(component (core module $M (import "" "g" (global i32))) (core instance (instantiate $M)))""", "missing module instantiation argument")]
    [InlineData("""(component (type $t (record (field "x" u32))) (component (alias outer 1 0 (type $u))) (type $r (resource (rep i32))) (component (alias outer 1 1 (type $s))))""", "transitively refers to resources")]
    public void Validation_InvalidComponent(string text, string message)
    {
        using var runtime = new WasmRuntime();
        var bytes = WasmRuntime.WatToWasm(text);

        var exception = Assert.Throws<WasmCompileException>(() => runtime.CompileComponent(bytes));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_ValidNames()
    {
        // Methods of different resources may have the same name, and an interface may be imported at several versions
        using var runtime = new WasmRuntime();
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm("""
            (component
              (import "wasi:io/streams@0.2.0" (instance
                (export "input-stream" (type $input (sub resource)))
                (export "output-stream" (type $output (sub resource)))
                (export "[method]input-stream.subscribe" (func (param "self" (borrow $input))))
                (export "[method]output-stream.subscribe" (func (param "self" (borrow $output))))
                (export "[static]output-stream.create" (func (result (own $output))))))
              (import "wasi:io/streams@0.3.0" (instance))
              (import "a:b/c@1" (version-suffix ".2.3") (instance))
              (import "my-store" (implements "wasi:keyvalue/store@0.2.0") (external-id "id") (instance)))
            """));

        Assert.Equal(4, component.Imports.Count);
    }

    [Fact]
    public void InterfaceExports()
    {
        using var runtime = new WasmRuntime();
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm("""
            (component
              (core module $M (func (export "f") (result i32) (i32.const 7)))
              (core instance $m (instantiate $M))
              (func $f (result u32) (canon lift (core func $m "f")))
              (instance $i (export "f" (func $f)))
              (export "test:demo/api@1.0.0" (instance $i)))
            """));

        using var store = runtime.CreateStore();
        var instance = store.InstantiateComponent(component);
        Assert.Equal(7u, instance.GetFunction("test:demo/api@1.0.0#f")?.Call()?.AsU32());
        Assert.Equal(7u, instance.GetInstance("test:demo/api@1.0.0")?.GetFunction("f")?.Call()?.AsU32());
    }
}
