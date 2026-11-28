using System.Text;
using Meziantou.Framework.WasmEngine.Wasi.Internal;

namespace Meziantou.Framework.WasmEngine.Wasi.Tests;

public sealed class WasiContextTests
{
    [Fact]
    public void Documentation_GettingStarted()
    {
        #region Wasi_GettingStarted
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
        #endregion
    }

    [Fact]
    public void Documentation_ExitCode()
    {
        #region Wasi_ExitCode
        using var runtime = new WasmRuntime();
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (import "wasi_snapshot_preview1" "proc_exit" (func $proc_exit (param i32)))
              (memory (export "memory") 1)
              (func (export "_start")
                (call $proc_exit (i32.const 3))
                (unreachable)))
            """));

        using var wasi = new WasiContext();
        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module, wasi.DefineImports(store));

        // proc_exit ends the program: Run returns its code
        Assert.Equal(3, wasi.Run(instance));
        #endregion
    }

    [Fact]
    public void Documentation_InMemoryFileSystem()
    {
        #region Wasi_InMemoryFileSystem
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
        #endregion
    }

    [Fact]
    public void Documentation_HostFileSystem()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var directory = temporaryDirectory.FullPath.Value;

        #region Wasi_HostFileSystem
        using var runtime = new WasmRuntime();

        // A command that creates the file "output.txt" in its preopened directory (file descriptor 3) and writes "Done"
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (import "wasi_snapshot_preview1" "path_open" (func $path_open (param i32 i32 i32 i32 i32 i64 i64 i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "fd_write" (func $fd_write (param i32 i32 i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "fd_close" (func $fd_close (param i32) (result i32)))
              (import "wasi_snapshot_preview1" "proc_exit" (func $proc_exit (param i32)))
              (memory (export "memory") 1)
              (data (i32.const 64) "output.txt")
              (data (i32.const 96) "Done")
              (func (export "_start")
                (local $errno i32)
                ;; Create or truncate the file (oflags creat | trunc = 9) with the right to write it (fd_write = 64)
                (local.set $errno (call $path_open (i32.const 3) (i32.const 1) (i32.const 64) (i32.const 10)
                  (i32.const 9) (i64.const 64) (i64.const 0) (i32.const 0) (i32.const 16)))
                (if (local.get $errno) (then (call $proc_exit (local.get $errno))))
                (i32.store (i32.const 0) (i32.const 96))
                (i32.store (i32.const 4) (i32.const 4))
                (drop (call $fd_write (i32.load (i32.const 16)) (i32.const 0) (i32.const 1) (i32.const 20)))
                (drop (call $fd_close (i32.load (i32.const 16))))))
            """));

        // The module sees the directory as "/": it cannot access the files outside of it
        using var wasi = new WasiContext(new WasiOptions
        {
            PreopenedDirectories = [new WasiPreopenedDirectory("/", new WasiHostFileSystem(directory))],
        });

        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module, wasi.DefineImports(store));

        Assert.Equal(0, wasi.Run(instance));
        Assert.Equal("Done", File.ReadAllText(Path.Combine(directory, "output.txt")));
        #endregion
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public void Run_ArgumentsAndEnvironment_AreTheOptions(int threshold)
    {
        // The command writes its arguments and its environment variables, separated by spaces, to its standard output
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = threshold });
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (import "wasi_snapshot_preview1" "args_sizes_get" (func $args_sizes_get (param i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "args_get" (func $args_get (param i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "environ_sizes_get" (func $environ_sizes_get (param i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "environ_get" (func $environ_get (param i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "fd_write" (func $fd_write (param i32 i32 i32 i32) (result i32)))
              (memory (export "memory") 1)
              (func $print (param $pointers i32) (param $count i32)
                (local $i i32) (local $address i32) (local $length i32)
                (block $done
                  (loop $next
                    (br_if $done (i32.ge_u (local.get $i) (local.get $count)))
                    (local.set $address (i32.load (i32.add (local.get $pointers) (i32.shl (local.get $i) (i32.const 2)))))
                    (local.set $length (i32.const 0))
                    (block $end
                      (loop $scan
                        (br_if $end (i32.eqz (i32.load8_u (i32.add (local.get $address) (local.get $length)))))
                        (local.set $length (i32.add (local.get $length) (i32.const 1)))
                        (br $scan)))
                    ;; Replace the null character with a space, and write the string
                    (i32.store8 (i32.add (local.get $address) (local.get $length)) (i32.const 32))
                    (i32.store (i32.const 0) (local.get $address))
                    (i32.store (i32.const 4) (i32.add (local.get $length) (i32.const 1)))
                    (drop (call $fd_write (i32.const 1) (i32.const 0) (i32.const 1) (i32.const 8)))
                    (local.set $i (i32.add (local.get $i) (i32.const 1)))
                    (br $next))))
              (func (export "_start")
                (drop (call $args_sizes_get (i32.const 16) (i32.const 20)))
                (drop (call $args_get (i32.const 1024) (i32.const 2048)))
                (call $print (i32.const 1024) (i32.load (i32.const 16)))
                (drop (call $environ_sizes_get (i32.const 16) (i32.const 20)))
                (drop (call $environ_get (i32.const 4096) (i32.const 8192)))
                (call $print (i32.const 4096) (i32.load (i32.const 16)))))
            """));

        using var output = new MemoryStream();
        using var wasi = new WasiContext(new WasiOptions
        {
            Arguments = ["program", "first", "é"],
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal) { ["A"] = "1", ["B"] = "x=y" },
            StandardOutput = output,
        });

        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module, wasi.DefineImports(store));

        Assert.Equal(0, wasi.Run(instance));
        Assert.Equal("program first é A=1 B=x=y ", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Run_MemoryThatGrows_IsReadAtEachCall(int threshold)
    {
        // The command grows its memory, and writes a text of the new page: the functions read the new bytes of the memory
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = threshold });
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (import "wasi_snapshot_preview1" "fd_write" (func $fd_write (param i32 i32 i32 i32) (result i32)))
              (memory (export "memory") 1)
              (func (export "_start")
                (drop (call $fd_write (i32.const 1) (i32.const 0) (i32.const 0) (i32.const 8)))
                (drop (memory.grow (i32.const 1)))
                (i32.store (i32.const 65536) (i32.const 0x4B4F))
                (i32.store (i32.const 0) (i32.const 65536))
                (i32.store (i32.const 4) (i32.const 2))
                (drop (call $fd_write (i32.const 1) (i32.const 0) (i32.const 1) (i32.const 8)))))
            """));

        using var output = new MemoryStream();
        using var wasi = new WasiContext(new WasiOptions { StandardOutput = output });
        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module, wasi.DefineImports(store));

        Assert.Equal(0, wasi.Run(instance));
        Assert.Equal("OK", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void Run_WithoutStart_Throws()
    {
        using var runtime = new WasmRuntime();
        var module = runtime.Compile(WasmRuntime.WatToWasm("(module (memory (export \"memory\") 1))"));
        using var wasi = new WasiContext();
        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module, wasi.DefineImports(store));

        Assert.Throws<InvalidOperationException>(() => wasi.Run(instance));
    }

    [Fact]
    public void Run_ModuleWithoutMemory_Throws()
    {
        using var runtime = new WasmRuntime();
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (import "wasi_snapshot_preview1" "fd_write" (func $fd_write (param i32 i32 i32 i32) (result i32)))
              (func (export "_start") (drop (call $fd_write (i32.const 1) (i32.const 0) (i32.const 0) (i32.const 0)))))
            """));
        using var wasi = new WasiContext();
        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module, wasi.DefineImports(store));

        Assert.Throws<InvalidOperationException>(() => wasi.Run(instance));
    }

    [Fact]
    public void Functions_CalledByTheHost_Throw()
    {
        using var runtime = new WasmRuntime();
        using var wasi = new WasiContext();
        using var store = runtime.CreateStore();
        var imports = wasi.DefineImports(store);
        var fdWrite = (WasmFunction)imports.Get("wasi_snapshot_preview1", "fd_write")!;

        // The functions need the memory of their caller: a call of the host has none
        Assert.Throws<InvalidOperationException>(() => fdWrite.Call(WasmValue.FromI32(1), WasmValue.FromI32(0), WasmValue.FromI32(0), WasmValue.FromI32(0)));

        // The functions that use no memory can be called by the host
        var fdClose = (WasmFunction)imports.Get("wasi_snapshot_preview1", "fd_close")!;
        Assert.Equal((int)WasiErrno.BadFileDescriptor, fdClose.Call(WasmValue.FromI32(42))[0].I32);
    }

    [Fact]
    public void DefineImports_DefinesEveryFunctionOfPreview1()
    {
        using var runtime = new WasmRuntime();
        using var wasi = new WasiContext();
        using var store = runtime.CreateStore();
        var imports = wasi.DefineImports(store);
        string[] names =
        [
            "args_get", "args_sizes_get", "environ_get", "environ_sizes_get", "clock_res_get", "clock_time_get",
            "fd_advise", "fd_allocate", "fd_close", "fd_datasync", "fd_fdstat_get", "fd_fdstat_set_flags",
            "fd_fdstat_set_rights", "fd_filestat_get", "fd_filestat_set_size", "fd_filestat_set_times", "fd_pread",
            "fd_prestat_get", "fd_prestat_dir_name", "fd_pwrite", "fd_read", "fd_readdir", "fd_renumber", "fd_seek",
            "fd_sync", "fd_tell", "fd_write", "path_create_directory", "path_filestat_get", "path_filestat_set_times",
            "path_link", "path_open", "path_readlink", "path_remove_directory", "path_rename", "path_symlink",
            "path_unlink_file", "poll_oneoff", "proc_exit", "proc_raise", "random_get", "sched_yield", "sock_accept",
            "sock_recv", "sock_send", "sock_shutdown",
        ];

        foreach (var name in names)
        {
            Assert.NotNull(imports.Get("wasi_snapshot_preview1", name));
        }

        // The types of the functions are the ones of the specification
        var pathOpen = (WasmFunction)imports.Get("wasi_snapshot_preview1", "path_open")!;
        Assert.Equal(
            [WasmValueType.I32, WasmValueType.I32, WasmValueType.I32, WasmValueType.I32, WasmValueType.I32, WasmValueType.I64, WasmValueType.I64, WasmValueType.I32, WasmValueType.I32],
            pathOpen.Type.Parameters);
        Assert.Equal([WasmValueType.I32], pathOpen.Type.Results);
        Assert.Empty(((WasmFunction)imports.Get("wasi_snapshot_preview1", "proc_exit")!).Type.Results);
    }

    [Fact]
    public void Options_NotValid_Throw()
    {
        Assert.Throws<ArgumentException>(() => new WasiContext(new WasiOptions { Arguments = ["a\0b"] }));
        Assert.Throws<ArgumentException>(() => new WasiContext(new WasiOptions { EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal) { ["A=B"] = "" } }));
        Assert.Throws<ArgumentException>(() => new WasiContext(new WasiOptions { EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal) { [""] = "" } }));
        Assert.Throws<ArgumentException>(() => new WasiPreopenedDirectory("", new WasiInMemoryFileSystem()));
    }

    [Fact]
    public void Dispose_ClosesTheOpenFiles()
    {
        var files = new WasiInMemoryFileSystem();
        files.AddFile("a.txt", "a"u8);
        var wasi = new WasiContext(new WasiOptions { PreopenedDirectories = [new WasiPreopenedDirectory("/", files)] });
        using (var harness = new WasiHarness(wasi.Implementation))
        {
            _ = harness.Open("a.txt");
            Assert.Equal(5, wasi.Implementation.FileDescriptorCount);
        }

        wasi.Dispose();
        Assert.Equal(0, wasi.Implementation.FileDescriptorCount);
        using var runtime = new WasmRuntime();
        using var store = runtime.CreateStore();
        Assert.Throws<ObjectDisposedException>(() => wasi.DefineImports(store));
    }

    [Fact]
    public void Initialize_CallsInitialize()
    {
        using var runtime = new WasmRuntime();
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (memory (export "memory") 1)
              (global $initialized (export "initialized") (mut i32) (i32.const 0))
              (func (export "_initialize") (global.set $initialized (i32.const 1))))
            """));
        using var wasi = new WasiContext();
        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module, wasi.DefineImports(store));
        wasi.Initialize(instance);

        Assert.Equal(1, instance.GetGlobal("initialized")!.Get().I32);
    }

    [Fact]
    public void WasiExitException_HasTheCode()
    {
        Assert.Equal(7, new WasiExitException(7).ExitCode);
        Assert.Equal(WasiErrno.NoEntry, new WasiException(WasiErrno.NoEntry).Errno);
        Assert.Equal(WasiErrno.IOError, new WasiException("message").Errno);
    }
}
