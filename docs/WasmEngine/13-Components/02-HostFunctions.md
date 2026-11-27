---
title: Host functions
description: Give .NET functions and interfaces to the imports of a component.
---

[ComponentFunction.FromCallback](cref:Meziantou.Framework.WasmEngine.Components.ComponentFunction.FromCallback) creates a
function whose steps are a .NET callback. Give it to the component with
[ComponentImports](cref:Meziantou.Framework.WasmEngine.Components.ComponentImports), by the name of the import:

<!-- ref:../../../tests/Meziantou.Framework.WasmEngine.Tests/WasmComponentTests.cs;region=Component_HostFunctions;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

The callback receives the values of the parameters, and returns the value of the result, or null for a function without
result. Its [ComponentCallContext](cref:Meziantou.Framework.WasmEngine.Components.ComponentCallContext) gives the
[Type](cref:Meziantou.Framework.WasmEngine.Components.ComponentCallContext.Type) of the function, as the component
imports it, and the [Store](cref:Meziantou.Framework.WasmEngine.Components.ComponentCallContext.Store).

## The types of the functions

A function of the host takes the type of the import it is given to: the canonical ABI converts the values of the
component to this type, and checks the values that the callback returns against it. Give a
[ComponentFunctionType](cref:Meziantou.Framework.WasmEngine.Components.ComponentFunctionType) to `FromCallback` to
check the type of the import when the component is instantiated: an import of another type throws a
[WasmLinkException](cref:Meziantou.Framework.WasmEngine.WasmLinkException).

## Interfaces

The imports of the interfaces, such as `wasi:cli/stdout@0.2.0`, are instances: define their functions and types in a
nested [ComponentImports](cref:Meziantou.Framework.WasmEngine.Components.ComponentImports), and give it the name of the
interface. A version of an interface is given to the imports of the compatible versions: the same major version, the
same minor version for the versions `0.x`, and the same patch version for the versions `0.0.x`. For instance, the
interfaces defined as `@0.2.12` are given to the imports of `@0.2.0`.

An instance of another component can be given too, to link components together: `Define(name, instance)` gives one of
its exported instances, and `DefineAll(instance)` gives all its exports.

## Errors

A .NET exception thrown by a callback stops the call of the component: it is thrown by the call of the exported
function, and the components of the store cannot be called anymore. To report an error to the component, return the
`error` case of a `result`.
