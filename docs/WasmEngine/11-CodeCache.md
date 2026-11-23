---
title: Code cache
description: Save the compiled code of a module, and load it in the next runs of the application instead of compiling the module again.
---

[Compile](cref:Meziantou.Framework.WasmEngine.WasmRuntime.Compile) validates the code of every function of a module
and converts it to the instructions of the interpreter. For a large module, this is most of the startup time. Save the
result with [CreateCodeCache](cref:Meziantou.Framework.WasmEngine.WasmModule.CreateCodeCache), and give it to
[Compile](cref:Meziantou.Framework.WasmEngine.WasmRuntime.Compile) with the same binary in the next runs:

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmRuntimeTests.cs;region=WasmEngine_CodeCache;format=md-fenced-code-block;dedent=true -->
```csharp
var bytes = WasmRuntime.WatToWasm("""
    (module
      (func (export "add") (param i32 i32) (result i32)
        (i32.add (local.get 0) (local.get 1))))
    """);

// The first run compiles the module from its binary, and saves its code cache (e.g. in a file)
byte[] codeCache;
using (var runtime = new WasmRuntime())
{
    var module = runtime.Compile(bytes);
    Assert.False(module.IsLoadedFromCodeCache);
    codeCache = module.CreateCodeCache();
}

// A later run gives the cache with the same binary: the code is not validated and compiled again. A cache that
// does not match the binary or the build of the engine is ignored: the module is compiled from its binary.
using (var runtime = new WasmRuntime())
{
    var module = runtime.Compile(bytes, codeCache);
    Assert.True(module.IsLoadedFromCodeCache);

    using var store = runtime.CreateStore();
    var add = store.Instantiate(module).GetFunction("add")!;
    Assert.Equal(42, add.Call(WasmValue.FromI32(40), WasmValue.FromI32(2))[0].I32);
}
```
<!-- endref -->

- The cache only holds the code of the functions: [Compile](cref:Meziantou.Framework.WasmEngine.WasmRuntime.Compile)
  still needs the binary, and decodes its other sections (the types, the imports, the exports, the data...).
- A cache that does not match is ignored, and the module is compiled from its binary: a cache of another binary, of
  another build of the engine, of a platform of another kind (with or without dynamic code), or a truncated or
  corrupted cache. [IsLoadedFromCodeCache](cref:Meziantou.Framework.WasmEngine.WasmModule.IsLoadedFromCodeCache) tells
  whether the cache was used: when it is false, create the cache again and replace the saved one, for instance after
  an update of the package.
- The module behaves the same: the functions are interpreted, then compiled to .NET code when they run often (see
  [compilation to .NET code](06-LimitsAndInterruption.md#compilation-to-net-code)). The cache does not hold this .NET
  code: the JIT of .NET compiles it again in each run.
- The cache is larger than the binary of the module.

## Trust

The code of a cache is not validated again: the engine runs it as it is. A cache is trusted like a compiled assembly:
store it where only the application can write, such as the local application data folder, not next to a binary that a
user or a web page provides. The cache detects the changes and the corruptions, but not a cache written on purpose to
run other code.
