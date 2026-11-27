---
title: Resources
description: Give .NET objects to the components as resources, and release them when the components drop them.
---

A resource is an object that a component holds through handles: `own<T>` owns it, `borrow<T>` lends it for the time of
a call. A resource of the host is a .NET object, its representation. Create its type with
[ComponentResourceType](cref:Meziantou.Framework.WasmEngine.Components.ComponentResourceType), give the type to the
import of the resource type, and wrap the objects in a
[ComponentResource](cref:Meziantou.Framework.WasmEngine.Components.ComponentResource):

<!-- ref:../../../tests/Meziantou.Framework.WasmEngine.Tests/WasmComponentTests.cs;region=Component_Resources;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

- The functions of a resource are the imports named `[constructor]T`, `[method]T.name` and `[static]T.name`. The
  first parameter of a method is the `borrow<T>` of the resource.
- [AsResource](cref:Meziantou.Framework.WasmEngine.Components.ComponentValue.AsResource) returns the resource of a
  handle, and [Representation](cref:Meziantou.Framework.WasmEngine.Components.ComponentResource.Representation) its
  object.
- The destructor of the type runs when the component drops the last owning handle of a resource: release its objects
  there, such as files or connections.

The resources that a component defines (`(resource (rep i32))`) are given to the host as their handles: the host
gives them back to the functions of the component, which can drop them.
