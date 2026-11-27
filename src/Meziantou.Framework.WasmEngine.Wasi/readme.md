# Meziantou.Framework.WasmEngine.Wasi

WASI for [Meziantou.Framework.WasmEngine](../Meziantou.Framework.WasmEngine/readme.md): run the WebAssembly programs
compiled for WASI Preview 1 (`wasm32-wasip1`: C, C++, Rust, Go, TinyGo, Zig, AssemblyScript) and the components of
WASI 0.2 and WASI 0.3 (`wasm32-wasip2`, `wasm32-wasip3`) with their arguments, environment variables, standard
streams, clocks, random numbers and exit codes, and give them directories on the disk or in memory that they cannot
leave. The components of WASI 0.3 also get sockets (`wasi:sockets`) and HTTP (`wasi:http`), on the network of the
machine with rules of what they can access, or on a network inside the process.

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiContextTests.cs;region=Wasi_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();

// A command that writes "Hello, WASI!" to its standard output (file descriptor 1) with fd_write
var module = runtime.Compile(WasmRuntime.WatToWasm("""
    (module
      (import "wasi_snapshot_preview1" "fd_write" (func $fd_write (param i32 i32 i32 i32) (result i32)))
      (memory (export "memory") 1)
      (data (i32.const 8) "Hello, WASI!\n")
      (func (export "_start")
        ;; An iovec at 0: the address and the length of the text
        (i32.store (i32.const 0) (i32.const 8))
        (i32.store (i32.const 4) (i32.const 13))
        (drop (call $fd_write (i32.const 1) (i32.const 0) (i32.const 1) (i32.const 32)))))
    """));

using var output = new MemoryStream();
using var wasi = new WasiContext(new WasiOptions
{
    Arguments = ["hello.wasm"],
    StandardOutput = output,
});

using var store = runtime.CreateStore();
var instance = store.Instantiate(module, wasi.DefineImports(store));
var exitCode = wasi.Run(instance);

Assert.Equal(0, exitCode);
Assert.Equal("Hello, WASI!\n", Encoding.UTF8.GetString(output.ToArray()));
```
<!-- endref -->

## File system

The program accesses only its preopened directories: a directory of the disk (`WasiHostFileSystem`) or files in memory
(`WasiInMemoryFileSystem`). The paths and the symbolic links cannot leave the directories.

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiContextTests.cs;region=Wasi_InMemoryFileSystem;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();

// A command that copies the file "input.txt" of its preopened directory (file descriptor 3) to its standard output
var module = runtime.Compile(WasmRuntime.WatToWasm("""
    (module
      (import "wasi_snapshot_preview1" "path_open" (func $path_open (param i32 i32 i32 i32 i32 i64 i64 i32 i32) (result i32)))
      (import "wasi_snapshot_preview1" "fd_read" (func $fd_read (param i32 i32 i32 i32) (result i32)))
      (import "wasi_snapshot_preview1" "fd_write" (func $fd_write (param i32 i32 i32 i32) (result i32)))
      (import "wasi_snapshot_preview1" "proc_exit" (func $proc_exit (param i32)))
      (memory (export "memory") 1)
      (data (i32.const 64) "input.txt")
      (func (export "_start")
        (local $errno i32)
        ;; Open the file with the right to read it (fd_read = 2): the new file descriptor is at 16
        (local.set $errno (call $path_open (i32.const 3) (i32.const 1) (i32.const 64) (i32.const 9)
          (i32.const 0) (i64.const 2) (i64.const 0) (i32.const 0) (i32.const 16)))
        (if (local.get $errno) (then (call $proc_exit (local.get $errno))))
        ;; Read up to 256 bytes at 1024, and write them to the standard output
        (i32.store (i32.const 0) (i32.const 1024))
        (i32.store (i32.const 4) (i32.const 256))
        (drop (call $fd_read (i32.load (i32.const 16)) (i32.const 0) (i32.const 1) (i32.const 20)))
        (i32.store (i32.const 4) (i32.load (i32.const 20)))
        (drop (call $fd_write (i32.const 1) (i32.const 0) (i32.const 1) (i32.const 24)))))
    """));

// The files of the module are in memory: the host prepares them, and reads them after the run
var files = new WasiInMemoryFileSystem();
files.AddFile("input.txt", "Hello from a file"u8);

using var output = new MemoryStream();
using var wasi = new WasiContext(new WasiOptions
{
    StandardOutput = output,
    PreopenedDirectories = [new WasiPreopenedDirectory("/", files)],
});

using var store = runtime.CreateStore();
var instance = store.Instantiate(module, wasi.DefineImports(store));

Assert.Equal(0, wasi.Run(instance));
Assert.Equal("Hello from a file", Encoding.UTF8.GetString(output.ToArray()));
```
<!-- endref -->

## WASI 0.2 and 0.3

`WasiComponentContext` defines the interfaces of WASI 0.2 and WASI 0.3 for the components:

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiComponentContextTests.cs;region=WasiComponent_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();

// A component that exports wasi:cli/run, compiled from its binary (File.ReadAllBytes("app.wasm")) or its text
var component = runtime.CompileComponent(WasmRuntime.WatToWasm(text));

using var output = new MemoryStream();
var wasi = new WasiComponentContext(new WasiOptions
{
    Arguments = ["app.wasm"],
    StandardOutput = output,
});

using var store = runtime.CreateStore();
var instance = store.InstantiateComponent(component, wasi.DefineImports());
var exitCode = WasiComponentContext.Run(instance);

Assert.Equal(0, exitCode);
Assert.Equal("Hello, WASI!\n", Encoding.UTF8.GetString(output.ToArray()));
```
<!-- endref -->

## Documentation

See [the documentation](../../docs/WasmEngine/12-Wasi/01-Overview.md): the options, the exit code, the file systems, the
sandbox, WASI 0.2 and 0.3, and the pass rate of the official WASI test suite.
