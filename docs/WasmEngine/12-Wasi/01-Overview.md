---
title: Overview
description: What WASI gives to the WebAssembly programs, and what Meziantou.Framework.WasmEngine.Wasi supports.
---

The WebAssembly System Interface (WASI) is the set of functions that a WebAssembly program uses to talk to its
environment: its arguments, its environment variables, its standard streams, the clocks, the random numbers and the
files. `Meziantou.Framework.WasmEngine.Wasi` implements, on top of [the engine](../01-Introduction.md):

- WASI Preview 1, the imports of the module `wasi_snapshot_preview1`. The toolchains that target `wasm32-wasip1`
  produce such programs: the WASI SDK (C and C++), Rust, Go (`GOOS=wasip1`), TinyGo, Zig and AssemblyScript.
- WASI 0.2 and WASI 0.3, the interfaces of the [components](../13-Components/01-Overview.md) compiled for
  `wasm32-wasip2` and `wasm32-wasip3`. See [WASI 0.2 and 0.3](04-Components.md).

<InstallNuGetPackage Id="Meziantou.Framework.WasmEngine.Wasi" />

- [Getting started](02-GettingStarted.md): run a command, give it arguments, environment variables and standard
  streams, and read its exit code.
- [File system](03-FileSystem.md): give the program directories on the disk or in memory, which it cannot leave.
- [WASI 0.2 and 0.3](04-Components.md): run the components of WASI 0.2 and WASI 0.3.
- [Network](05-Network.md): give the components of WASI 0.3 sockets, on the network of the machine with rules, or on a
  network inside the process.
- [HTTP](06-Http.md): let the components send HTTP requests, and handle HTTP requests with a component.
- [Conformance](07-Conformance.md): the pass rate of the official WASI test suite.

## What is supported by WASI Preview 1

- The arguments and the environment variables (`args_*`, `environ_*`).
- The realtime and monotonic clocks (`clock_*`), from a [TimeProvider](https://learn.microsoft.com/dotnet/api/system.timeprovider),
  and the random numbers (`random_get`).
- The standard input, output and error, as .NET streams.
- The files and the directories of the preopened directories (`fd_*`, `path_*`): read, write, seek, truncate, list,
  create, rename, delete, the times, the symbolic links, the hard links, and the rights of
  the file descriptors.
- `poll_oneoff`: the clocks, and the files, which are always ready.
- The exit of the program (`proc_exit`).

## What is not supported

- The sockets: `sock_*` return `notsock`, as the program cannot open sockets.
- The signals: `proc_raise` returns `nosys`.
- The threads of `wasi-threads`.
- The sockets and HTTP of WASI 0.2 (`wasi:sockets`, `wasi:http`): those of WASI 0.3 are supported.
