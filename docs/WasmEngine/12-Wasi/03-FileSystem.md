---
title: File system
description: Give a WASI program directories on the disk or in memory, and keep it inside them.
---

A WASI program has no access to the files of your machine: it accesses only the preopened directories of
[PreopenedDirectories](cref:Meziantou.Framework.WasmEngine.Wasi.WasiOptions.PreopenedDirectories), the file descriptors 3,
4, ... in their order. A [WasiPreopenedDirectory](cref:Meziantou.Framework.WasmEngine.Wasi.WasiPreopenedDirectory) has the
path the program sees, such as `/` or `/data`, and a [WasiFileSystem](cref:Meziantou.Framework.WasmEngine.Wasi.WasiFileSystem)
that holds its files. The C library of WASI opens the absolute paths of the program in the preopened directory whose
path starts them, so a program that opens `/data/input.txt` reads the file `input.txt` of the directory preopened as
`/data`.

## A directory of the disk

[WasiHostFileSystem](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostFileSystem) gives the program the files of a
directory:

<!-- ref:../../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiContextTests.cs;region=Wasi_HostFileSystem;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

- Pass `isReadOnly: true` to the constructor to let the program only read the files: the changes fail with `rofs`.
- The program can delete or rename a file it has open. Windows does not let it rename a directory whose files are open.
- The hard links are supported on Windows (NTFS and ReFS), Linux, macOS and FreeBSD, and fail with `notsup` elsewhere
  (the browser) and on the file systems without hard links (FAT). A hard link never follows a symbolic link: the program
  cannot link a file outside of the directory through a symbolic link. Two hard links of a file have the same serial
  number (`ino`), and the status of a file gives its number of links (`nlink`).
- Windows lets only the administrators and the developer mode create symbolic links: elsewhere, `path_symlink` fails
  with `perm`.

## Files in memory

[WasiInMemoryFileSystem](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryFileSystem) holds the files in memory: it
works on every platform, including the browser, and keeps the program away from the disk. Prepare the files with
[AddFile](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryFileSystem.AddFile),
[AddDirectory](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryFileSystem.AddDirectory) and
[AddSymbolicLink](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryFileSystem.AddSymbolicLink), and read the results
with [ReadAllBytes](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryFileSystem.ReadAllBytes):

<!-- ref:../../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiContextTests.cs;region=Wasi_InMemoryFileSystem;format=md-fenced-code-block;dedent=true -->
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

- [MaxFileSize](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryFileSystem.MaxFileSize) limits the size of a file:
  a write beyond fails with `fbig`. Lower it when the program is not trusted: a file holds its bytes in memory.
- The hard links and the symbolic links are supported.

## The sandbox

WASI resolves the paths of the program before it calls the file system, so that the program cannot reach the files
outside of its directories:

- The absolute paths fail with `notcapable`: the C library of WASI converts them to paths relative to a preopened
  directory.
- A path is relative to the directory of its file descriptor, and cannot leave it: `../file` fails with `notcapable`,
  even when the directory is inside a preopened directory.
- WASI follows the symbolic links itself, with the same rule: a link that leaves the directory, or whose target is
  absolute, fails with `notcapable`. A chain of more than 32 links fails with `loop`. The program cannot create a link
  whose target is absolute (`perm`), including a drive or a share on Windows (`C:/`, `//server/share`).
- The paths are UTF-8 without null characters (`ilseq`, `inval`), and 4,096 bytes at most (`nametoolong`).
- On Windows, [WasiHostFileSystem](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostFileSystem) also rejects the names
  that Windows would read differently (`\`, `:`, `*`, `?`, `"`, `<`, `>`, `|`, the control characters, the device names
  such as `CON` or `NUL`, and the names that end with a dot or a space), and checks that the full path of each file is in
  its directory.

The checks of a directory of the disk and the operations are not atomic: a process of your machine that replaces a
directory with a link between the check and the operation could let the program access the target of the link. Do not
give the program a directory that untrusted processes can change.

## Your own file system

Derive from [WasiFileSystem](cref:Meziantou.Framework.WasmEngine.Wasi.WasiFileSystem) to give the program other files,
such as the entries of an archive or a database. Its methods receive canonical paths: relative to its root, separated by
`/`, without `.`, `..` or empty segments, and already resolved by WASI. Throw a
[WasiException](cref:Meziantou.Framework.WasmEngine.Wasi.WasiException) to return a precise
[WasiErrno](cref:Meziantou.Framework.WasmEngine.Wasi.WasiErrno) to the program; the other exceptions of I/O are converted
to the closest code.

A rename over an existing file calls [Replace](cref:Meziantou.Framework.WasmEngine.Wasi.WasiFileSystem.Replace), which
deletes the destination and then moves the source by default. Override it to replace the file in one operation, as
[WasiHostFileSystem](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostFileSystem) does, so that a move that fails does
not lose the destination.
