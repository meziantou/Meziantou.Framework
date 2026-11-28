using System.Net;
using System.Net.Sockets;
using System.Text;
using Meziantou.Framework.WasmEngine.Components;

namespace Meziantou.Framework.WasmEngine.Wasi.Tests;

public sealed class WasiComponentContextTests
{
    /// <summary>A component of WASI 0.2 that writes "Hello, WASI!" to its standard output.</summary>
    private const string HelloWorldPreview2 = """
        (component
          (import "wasi:io/error@0.2.12" (instance $error
            (export "error" (type (sub resource)))))
          (alias export $error "error" (type $error-type))
          (import "wasi:io/streams@0.2.12" (instance $streams
            (alias outer 1 $error-type (type $error))
            (export "output-stream" (type $output-stream (sub resource)))
            (type $stream-error (variant (case "last-operation-failed" (own $error)) (case "closed")))
            (export "stream-error" (type $stream-error-export (eq $stream-error)))
            (export "[method]output-stream.blocking-write-and-flush"
              (func (param "self" (borrow $output-stream)) (param "contents" (list u8)) (result (result (error $stream-error-export)))))))
          (alias export $streams "output-stream" (type $output-stream))
          (import "wasi:cli/stdout@0.2.12" (instance $stdout
            (alias outer 1 $output-stream (type $output-stream))
            (export "get-stdout" (func (result (own $output-stream))))))

          (core module $Memory (memory (export "memory") 1))
          (core instance $memory (instantiate $Memory))
          (core func $get-stdout (canon lower (func $stdout "get-stdout")))
          (core func $write (canon lower (func $streams "[method]output-stream.blocking-write-and-flush") (memory (core memory $memory "memory"))))
          (core func $drop (canon resource.drop $output-stream))
          (core module $Main
            (import "memory" "memory" (memory 1))
            (import "stdout" "get-stdout" (func $get-stdout (result i32)))
            (import "streams" "write" (func $write (param i32 i32 i32 i32)))
            (import "streams" "drop" (func $drop (param i32)))
            (data (i32.const 16) "Hello, WASI!\n")
            (func (export "run") (result i32)
              (local $stdout i32)
              (local.set $stdout (call $get-stdout))
              ;; The result of the write is at the address 0: its discriminant is 0 (ok) or 1 (error)
              (call $write (local.get $stdout) (i32.const 16) (i32.const 13) (i32.const 0))
              (call $drop (local.get $stdout))
              (i32.load8_u (i32.const 0))))
          (core instance $main (instantiate $Main
            (with "memory" (instance $memory))
            (with "stdout" (instance (export "get-stdout" (func $get-stdout))))
            (with "streams" (instance (export "write" (func $write)) (export "drop" (func $drop))))))
          (type $run-result (result))
          (func $run (result $run-result) (canon lift (core func $main "run")))
          (instance $run-instance (export "run" (func $run)))
          (export "wasi:cli/run@0.2.12" (instance $run-instance)))
        """;

    [Fact]
    public void GettingStarted()
    {
        var text = HelloWorldPreview2;

        #region WasiComponent_GettingStarted
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
        #endregion
    }

    [Theory]
    [InlineData("0.2.0")]
    [InlineData("0.2.9")]
    public void OlderMinorVersion(string version)
    {
        // A component that imports an older version of WASI 0.2 uses the interfaces of the newest one
        using var runtime = new WasmRuntime();
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm(HelloWorldPreview2.Replace("@0.2.12", "@" + version, StringComparison.Ordinal)));
        using var output = new MemoryStream();
        var wasi = new WasiComponentContext(new WasiOptions { StandardOutput = output });
        using var store = runtime.CreateStore();
        var instance = store.InstantiateComponent(component, wasi.DefineImports());

        Assert.Equal(0, WasiComponentContext.Run(instance));
        Assert.Equal("Hello, WASI!\n", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void StandardOutputDiscarded()
    {
        using var runtime = new WasmRuntime();
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm(HelloWorldPreview2));
        var wasi = new WasiComponentContext();
        using var store = runtime.CreateStore();
        var instance = store.InstantiateComponent(component, wasi.DefineImports());

        Assert.Equal(0, WasiComponentContext.Run(instance));
    }

    [Fact]
    public void Preview3_ArgumentsAndExit()
    {
        // run is an asynchronous function of WASI 0.3, lifted with the synchronous ABI: it exits with the number of arguments
        using var runtime = new WasmRuntime();
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm("""
            (component
              (import "wasi:cli/environment@0.3.2" (instance $environment
                (export "get-arguments" (func (result (list string))))))
              (import "wasi:cli/exit@0.3.2" (instance $exit
                (export "exit-with-code" (func (param "status-code" u8)))))
              (core module $Memory
                (memory (export "memory") 1)
                (global $heap (mut i32) (i32.const 1024))
                (func (export "realloc") (param i32 i32 i32 i32) (result i32)
                  (local $pointer i32)
                  (local.set $pointer (i32.and (i32.add (global.get $heap) (i32.const 7)) (i32.const -8)))
                  (global.set $heap (i32.add (local.get $pointer) (local.get 3)))
                  (local.get $pointer)))
              (core instance $memory (instantiate $Memory))
              (core func $get-arguments (canon lower (func $environment "get-arguments") (memory (core memory $memory "memory")) (realloc (core func $memory "realloc"))))
              (core func $exit (canon lower (func $exit "exit-with-code")))
              (core module $Main
                (import "memory" "memory" (memory 1))
                (import "host" "get-arguments" (func $get-arguments (param i32)))
                (import "host" "exit" (func $exit (param i32)))
                (func (export "run") (result i32)
                  (call $get-arguments (i32.const 0))
                  (call $exit (i32.load (i32.const 4)))
                  unreachable))
              (core instance $main (instantiate $Main
                (with "memory" (instance $memory))
                (with "host" (instance (export "get-arguments" (func $get-arguments)) (export "exit" (func $exit))))))
              (type $run-result (result))
              (func $run async (result $run-result) (canon lift (core func $main "run")))
              (instance $run-instance (export "run" (func $run)))
              (export "wasi:cli/run@0.3.2" (instance $run-instance)))
            """));

        var wasi = new WasiComponentContext(new WasiOptions { Arguments = ["app.wasm", "a", "b"] });
        using var store = runtime.CreateStore();
        var instance = store.InstantiateComponent(component, wasi.DefineImports());

        Assert.Equal(3, WasiComponentContext.Run(instance));
    }

    [Fact]
    public void Preview3_WriteViaStream()
    {
        // The component writes to its standard output through a stream<u8>, which the host copies to the .NET stream
        using var runtime = new WasmRuntime();
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm("""
            (component
              (import "wasi:cli/stdout@0.3.2" (instance $stdout
                (type $error-code (enum "io" "illegal-byte-sequence" "pipe"))
                (export "error-code" (type $error-code-export (eq $error-code)))
                (export "write-via-stream" (func (param "data" (stream u8)) (result (future (result (error $error-code-export))))))))
              (alias export $stdout "error-code" (type $error-code))
              (type $status (result (error $error-code)))
              (type $bytes (stream u8))
              (type $done (future $status))
              (core module $Memory (memory (export "memory") 1))
              (core instance $memory (instantiate $Memory))
              (core func $write-via-stream (canon lower (func $stdout "write-via-stream")))
              (core func $stream-new (canon stream.new $bytes))
              (core func $stream-write (canon stream.write $bytes (memory (core memory $memory "memory"))))
              (core func $stream-drop (canon stream.drop-writable $bytes))
              (core func $future-drop (canon future.drop-readable $done))
              (core module $Main
                (import "memory" "memory" (memory 1))
                (import "host" "write-via-stream" (func $write-via-stream (param i32) (result i32)))
                (import "host" "stream.new" (func $stream-new (result i64)))
                (import "host" "stream.write" (func $stream-write (param i32 i32 i32) (result i32)))
                (import "host" "stream.drop-writable" (func $stream-drop (param i32)))
                (import "host" "future.drop-readable" (func $future-drop (param i32)))
                (data (i32.const 16) "Hello, WASI 0.3!\n")
                (func (export "run") (result i32)
                  (local $ends i64)
                  (local $writable i32)
                  (local $future i32)
                  (local.set $ends (call $stream-new))
                  (local.set $writable (i32.wrap_i64 (i64.shr_u (local.get $ends) (i64.const 32))))
                  (local.set $future (call $write-via-stream (i32.wrap_i64 (local.get $ends))))
                  ;; The write blocks until the host reads the 17 bytes: the result is COMPLETED (0) and the count
                  (if (i32.ne (call $stream-write (local.get $writable) (i32.const 16) (i32.const 17)) (i32.const 0x110))
                    (then unreachable))
                  (call $stream-drop (local.get $writable))
                  (call $future-drop (local.get $future))
                  (i32.const 0)))
              (core instance $main (instantiate $Main
                (with "memory" (instance $memory))
                (with "host" (instance
                  (export "write-via-stream" (func $write-via-stream))
                  (export "stream.new" (func $stream-new))
                  (export "stream.write" (func $stream-write))
                  (export "stream.drop-writable" (func $stream-drop))
                  (export "future.drop-readable" (func $future-drop))))))
              (type $run-result (result))
              (func $run async (result $run-result) (canon lift (core func $main "run")))
              (instance $run-instance (export "run" (func $run)))
              (export "wasi:cli/run@0.3.2" (instance $run-instance)))
            """));

        using var output = new MemoryStream();
        var wasi = new WasiComponentContext(new WasiOptions { StandardOutput = output });
        using var store = runtime.CreateStore();
        var instance = store.InstantiateComponent(component, wasi.DefineImports());

        Assert.Equal(0, WasiComponentContext.Run(instance));
        Assert.Equal("Hello, WASI 0.3!\n", Encoding.UTF8.GetString(output.ToArray()));
    }

    /// <summary>A component that stats "a.txt" in its preopened directory, and exits with the size of the file.</summary>
    private static int RunStatComponent(string version, bool isAsync, string secondsType)
    {
        var asyncKeyword = isAsync ? "async" : "";
        using var runtime = new WasmRuntime();
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm($$"""
            (component
              (import "wasi:filesystem/types@{{version}}" (instance $types
                (export "descriptor" (type $descriptor (sub resource)))
                (type $instant (record (field "seconds" {{secondsType}}) (field "nanoseconds" u32)))
                (export "instant" (type $instant-export (eq $instant)))
                (type $descriptor-type (variant (case "block-device") (case "character-device") (case "directory") (case "fifo") (case "symbolic-link") (case "regular-file") (case "socket") (case "other" (option string))))
                (export "descriptor-type" (type $descriptor-type-export (eq $descriptor-type)))
                (type $stat (record (field "type" $descriptor-type-export) (field "link-count" u64) (field "size" u64)
                  (field "data-access-timestamp" (option $instant-export)) (field "data-modification-timestamp" (option $instant-export)) (field "status-change-timestamp" (option $instant-export))))
                (export "descriptor-stat" (type $stat-export (eq $stat)))
                (type $path-flags (flags "symlink-follow"))
                (export "path-flags" (type $path-flags-export (eq $path-flags)))
                (type $error-code (variant (case "access") (case "no-entry") (case "other" (option string))))
                (export "error-code" (type $error-code-export (eq $error-code)))
                (export "[method]descriptor.stat-at" (func {{asyncKeyword}} (param "self" (borrow $descriptor)) (param "path-flags" $path-flags-export) (param "path" string) (result (result $stat-export (error $error-code-export)))))))
              (alias export $types "descriptor" (type $descriptor))
              (import "wasi:filesystem/preopens@{{version}}" (instance $preopens
                (alias outer 1 $descriptor (type $descriptor))
                (export "get-directories" (func (result (list (tuple (own $descriptor) string)))))))
              (import "wasi:cli/exit@{{version}}" (instance $exit
                (export "exit-with-code" (func (param "status-code" u8)))))
              (core module $Memory
                (memory (export "memory") 1)
                (global $heap (mut i32) (i32.const 1024))
                (func (export "realloc") (param i32 i32 i32 i32) (result i32)
                  (local $pointer i32)
                  (local.set $pointer (i32.and (i32.add (global.get $heap) (i32.const 7)) (i32.const -8)))
                  (global.set $heap (i32.add (local.get $pointer) (local.get 3)))
                  (local.get $pointer)))
              (core instance $memory (instantiate $Memory))
              (core func $get-directories (canon lower (func $preopens "get-directories") (memory (core memory $memory "memory")) (realloc (core func $memory "realloc"))))
              (core func $stat-at (canon lower (func $types "[method]descriptor.stat-at") (memory (core memory $memory "memory")) (realloc (core func $memory "realloc"))))
              (core func $exit (canon lower (func $exit "exit-with-code")))
              (core module $Main
                (import "memory" "memory" (memory 1))
                (import "host" "get-directories" (func $get-directories (param i32)))
                (import "host" "stat-at" (func $stat-at (param i32 i32 i32 i32 i32)))
                (import "host" "exit" (func $exit (param i32)))
                (data (i32.const 100) "a.txt")
                (func (export "run") (result i32)
                  ;; The list of the directories is at 0: its first element is the handle of the descriptor and the name
                  (call $get-directories (i32.const 0))
                  ;; The result of stat-at is at 200: the discriminant, then the record (the size is at offset 24 of the record)
                  (call $stat-at (i32.load (i32.load (i32.const 0))) (i32.const 0) (i32.const 100) (i32.const 5) (i32.const 200))
                  (if (i32.load8_u (i32.const 200)) (then unreachable))
                  (call $exit (i32.wrap_i64 (i64.load (i32.const 232))))
                  unreachable))
              (core instance $main (instantiate $Main
                (with "memory" (instance $memory))
                (with "host" (instance (export "get-directories" (func $get-directories)) (export "stat-at" (func $stat-at)) (export "exit" (func $exit))))))
              (type $run-result (result))
              (func $run {{asyncKeyword}} (result $run-result) (canon lift (core func $main "run")))
              (instance $run-instance (export "run" (func $run)))
              (export "wasi:cli/run@{{version}}" (instance $run-instance)))
            """));

        var fileSystem = new WasiInMemoryFileSystem();
        fileSystem.AddFile("a.txt", "hello"u8.ToArray());
        var wasi = new WasiComponentContext(new WasiOptions { PreopenedDirectories = [new WasiPreopenedDirectory("/", fileSystem)] });
        using var store = runtime.CreateStore();
        var instance = store.InstantiateComponent(component, wasi.DefineImports());
        return WasiComponentContext.Run(instance);
    }

    [Fact]
    public void Preview3_FileSystem() => Assert.Equal(5, RunStatComponent("0.3.2", isAsync: true, secondsType: "s64"));

    [Fact]
    public void Preview2_FileSystem() => Assert.Equal(5, RunStatComponent("0.2.12", isAsync: false, secondsType: "u64"));

    [Fact]
    public void Run_WithoutRunExport()
    {
        using var runtime = new WasmRuntime();
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm("(component)"));
        using var store = runtime.CreateStore();
        var instance = store.InstantiateComponent(component, new WasiComponentContext().DefineImports());

        Assert.Throws<InvalidOperationException>(() => WasiComponentContext.Run(instance));
    }

    [Fact]
    public void DefineImports_Interfaces()
    {
        var imports = new WasiComponentContext().DefineImports(new ComponentImports());

        Assert.Contains("wasi:cli/stdout@0.2.12", imports.Names);
        Assert.Contains("wasi:cli/stdout@0.3.2", imports.Names);
        Assert.Contains("wasi:filesystem/types@0.3.2", imports.Names);
        Assert.Contains("wasi:io/streams@0.2.12", imports.Names);
    }

    [Fact]
    public void InvalidEnvironmentVariable()
    {
        Assert.Throws<ArgumentException>(() => new WasiComponentContext(new WasiOptions { EnvironmentVariables = { ["a=b"] = "c" } }));
    }

    // ------------------------------------------------------------------------------------------------------------
    // wasi:sockets
    // ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task HostNetwork_ChecksTheAddresses()
    {
        #region WasiNetwork_HostNetwork
        // The component can only use the loopback addresses (the services of this machine), and cannot look up names
        var network = new WasiHostNetwork(new WasiHostNetworkOptions
        {
            AddressCheck = WasiHostNetworkOptions.AllowLoopback,
            AllowNameLookup = false,
            AllowUdp = false,
        });

        var wasi = new WasiComponentContext(new WasiOptions { Network = network });
        #endregion

        Assert.NotNull(wasi);
        using var socket = network.CreateTcpSocket(AddressFamily.InterNetwork);
        Assert.Equal(SocketError.AccessDenied, Assert.Throws<SocketException>(() => socket.Bind(new IPEndPoint(IPAddress.Any, 0))).SocketErrorCode);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        Assert.Equal(IPAddress.Loopback, socket.LocalEndPoint!.Address);
        Assert.NotEqual(0, socket.LocalEndPoint.Port);

        using var client = network.CreateTcpSocket(AddressFamily.InterNetwork);
        var outside = await Assert.ThrowsAsync<SocketException>(async () => await client.ConnectAsync(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 80), CancellationToken.None));
        Assert.Equal(SocketError.AccessDenied, outside.SocketErrorCode);
        Assert.Equal(SocketError.AccessDenied, Assert.Throws<SocketException>(() => network.CreateUdpSocket(AddressFamily.InterNetwork)).SocketErrorCode);
        var lookup = await Assert.ThrowsAsync<SocketException>(async () => await network.ResolveAddressesAsync("localhost", CancellationToken.None));
        Assert.Equal(SocketError.AccessDenied, lookup.SocketErrorCode);

        // Without a check, every address is denied
        var denied = new WasiHostNetwork(new WasiHostNetworkOptions());
        using var deniedSocket = denied.CreateTcpSocket(AddressFamily.InterNetwork);
        Assert.Equal(SocketError.AccessDenied, Assert.Throws<SocketException>(() => deniedSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0))).SocketErrorCode);
    }

    [Fact]
    public async Task InMemoryNetwork_ConnectsTheHostAndTheComponents()
    {
        #region WasiNetwork_InMemoryNetwork
        // A network inside the process: the host listens and connects next to the components, without the network of the machine
        var network = new WasiInMemoryNetwork();
        using var listener = network.Listen(new IPEndPoint(IPAddress.Loopback, 8080));

        await using var client = await network.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 8080));
        await using var server = await listener.AcceptAsync();
        await client.WriteAsync("ping"u8.ToArray());

        var buffer = new byte[4];
        await server.ReadExactlyAsync(buffer);
        Assert.Equal("ping", Encoding.ASCII.GetString(buffer));

        var wasi = new WasiComponentContext(new WasiOptions { Network = network });
        #endregion

        Assert.NotNull(wasi);

        // The port is in use, the other addresses are not addresses of the network, and no socket listens on 8081
        Assert.Equal(SocketError.AddressAlreadyInUse, Assert.Throws<SocketException>(() => network.Listen(new IPEndPoint(IPAddress.Any, 8080))).SocketErrorCode);
        Assert.Equal(SocketError.AddressNotAvailable, Assert.Throws<SocketException>(() => network.Listen(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 80))).SocketErrorCode);
        Assert.Equal(SocketError.ConnectionRefused, (await Assert.ThrowsAsync<SocketException>(async () => await network.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 8081)))).SocketErrorCode);

        // The end of the data, after the peer shuts down its side
        client.Dispose();
        Assert.Equal(0, await server.ReadAsync(buffer));
    }

    [Fact]
    public async Task InMemoryNetwork_Datagrams()
    {
        var network = new WasiInMemoryNetwork();
        using var receiver = network.CreateUdpSocket(AddressFamily.InterNetwork);
        receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        using var sender = network.CreateUdpSocket(AddressFamily.InterNetwork);
        await sender.SendToAsync("hello"u8.ToArray(), receiver.LocalEndPoint!, CancellationToken.None);

        var buffer = new byte[65536];
        var (length, source) = await receiver.ReceiveFromAsync(buffer, CancellationToken.None);
        Assert.Equal("hello", Encoding.ASCII.GetString(buffer, 0, length));
        Assert.Equal(sender.LocalEndPoint!.Port, source.Port);
        Assert.Equal(IPAddress.Loopback, source.Address);

        // A connected socket receives only the datagrams of its peer
        using var other = network.CreateUdpSocket(AddressFamily.InterNetwork);
        receiver.Connect(new IPEndPoint(IPAddress.Loopback, sender.LocalEndPoint.Port));
        Assert.Equal(IPAddress.Loopback, receiver.LocalEndPoint!.Address);
        await other.SendToAsync("ignored"u8.ToArray(), receiver.LocalEndPoint, CancellationToken.None);
        await sender.SendToAsync("again"u8.ToArray(), receiver.LocalEndPoint, CancellationToken.None);
        (length, _) = await receiver.ReceiveFromAsync(buffer, CancellationToken.None);
        Assert.Equal("again", Encoding.ASCII.GetString(buffer, 0, length));

        Assert.Equal([IPAddress.Loopback, IPAddress.IPv6Loopback], await network.ResolveAddressesAsync("LOCALHOST", CancellationToken.None));
        Assert.Equal(SocketError.HostNotFound, (await Assert.ThrowsAsync<SocketException>(async () => await network.ResolveAddressesAsync("example.com", CancellationToken.None))).SocketErrorCode);
    }

    // ------------------------------------------------------------------------------------------------------------
    // wasi:http
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>A component of the world wasi:http/service: its handler responds 201 (Created), without body.</summary>
    private const string HttpServiceComponent = """
        (component
          (import "wasi:http/types@0.3.2" (instance $types
            (export "fields" (type $fields (sub resource)))
            (export "request" (type $request (sub resource)))
            (export "response" (type $response (sub resource)))
            (type $error-code (variant (case "internal-error" (option string))))
            (export "error-code" (type $error-code-export (eq $error-code)))
            (export "[constructor]fields" (func (result (own $fields))))
            (export "[static]response.new" (func (param "headers" (own $fields)) (param "contents" (option (stream u8)))
              (param "trailers" (future (result (option (own $fields)) (error $error-code-export))))
              (result (tuple (own $response) (future (result (error $error-code-export)))))))
            (export "[method]response.set-status-code" (func (param "self" (borrow $response)) (param "status-code" u16) (result (result))))))
          (alias export $types "fields" (type $fields))
          (alias export $types "request" (type $request))
          (alias export $types "response" (type $response))
          (alias export $types "error-code" (type $error-code))
          (type $trailers (future (result (option (own $fields)) (error $error-code))))
          (type $handle-result (result (own $response) (error $error-code)))
          (core module $Memory (memory (export "memory") 1))
          (core instance $memory (instantiate $Memory))
          (core func $fields-new (canon lower (func $types "[constructor]fields")))
          (core func $response-new (canon lower (func $types "[static]response.new") (memory (core memory $memory "memory"))))
          (core func $set-status (canon lower (func $types "[method]response.set-status-code")))
          (core func $request-drop (canon resource.drop $request))
          (core func $trailers-new (canon future.new $trailers))
          (core func $trailers-write (canon future.write $trailers async (memory (core memory $memory "memory"))))
          (core func $task-return (canon task.return (result $handle-result) (memory (core memory $memory "memory"))))
          (core module $M
            (import "" "memory" (memory 1))
            (import "" "fields-new" (func $fields-new (result i32)))
            (import "" "response-new" (func $response-new (param i32 i32 i32 i32 i32)))
            (import "" "set-status" (func $set-status (param i32 i32) (result i32)))
            (import "" "request-drop" (func $request-drop (param i32)))
            (import "" "trailers-new" (func $trailers-new (result i64)))
            (import "" "trailers-write" (func $trailers-write (param i32 i32) (result i32)))
            (import "" "task-return" (func $task-return (param i32 i32 i32 i32 i32)))
            (func (export "handle") (param $request i32)
              (local $ends i64)
              (local $response i32)
              (call $request-drop (local.get $request))
              (local.set $ends (call $trailers-new))
              ;; The response without body: its handle and the future of its transmission are at 16
              (call $response-new (call $fields-new) (i32.const 0) (i32.const 0) (i32.wrap_i64 (local.get $ends)) (i32.const 16))
              (local.set $response (i32.load (i32.const 16)))
              (drop (call $set-status (local.get $response) (i32.const 201)))
              ;; The trailers are ok(none) (zeros at 0): the write completes when the host reads them
              (drop (call $trailers-write (i32.wrap_i64 (i64.shr_u (local.get $ends) (i64.const 32))) (i32.const 0)))
              (call $task-return (i32.const 0) (local.get $response) (i32.const 0) (i32.const 0) (i32.const 0))))
          (core instance $m (instantiate $M (with "" (instance
            (export "memory" (memory $memory "memory"))
            (export "fields-new" (func $fields-new))
            (export "response-new" (func $response-new))
            (export "set-status" (func $set-status))
            (export "request-drop" (func $request-drop))
            (export "trailers-new" (func $trailers-new))
            (export "trailers-write" (func $trailers-write))
            (export "task-return" (func $task-return))))))
          (func $handle async (param "request" (own $request)) (result $handle-result) (canon lift (core func $m "handle") async (memory (core memory $memory "memory"))))
          (instance $handler (export "handle" (func $handle)))
          (export "wasi:http/handler@0.3.2" (instance $handler)))
        """;

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The response owns the request")]
    public async Task HttpService()
    {
        var text = HttpServiceComponent;

        #region WasiHttp_Service
        using var runtime = new WasmRuntime();

        // A component of the world wasi:http/service: it exports wasi:http/handler
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm(text));
        var wasi = new WasiComponentContext();
        using var store = runtime.CreateStore();
        var instance = store.InstantiateComponent(component, wasi.DefineImports());

        // Each call handles a request on the thread of the store: a server of the host calls it for each request
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/items");
        using var response = wasi.HandleHttpRequest(instance, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        #endregion

        using var second = wasi.HandleHttpRequest(instance, new HttpRequestMessage(HttpMethod.Get, "http://localhost/"));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public void HttpService_WithoutHandler()
    {
        using var runtime = new WasmRuntime();
        var component = runtime.CompileComponent(WasmRuntime.WatToWasm("(component)"));
        var wasi = new WasiComponentContext();
        using var store = runtime.CreateStore();
        var instance = store.InstantiateComponent(component, wasi.DefineImports());
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/");
        Assert.Throws<InvalidOperationException>(() => wasi.HandleHttpRequest(instance, request));
    }

    [Theory]
    [InlineData("0.3.9", "0.3.10", "0.3.10")]
    [InlineData("0.3.0-rc.9", "0.3.0-rc.10", "0.3.0-rc.10")]
    [InlineData("0.3.0-rc-2025-09-16", "0.3.0", "0.3.0")]
    [InlineData("0.3.1", "0.3.2+build.1", "0.3.2+build.1")]
    [InlineData("0.3.1", "0.3.2+build-1", "0.3.2+build-1")]
    [InlineData("0.3.0-alpha", "0.3.0-1", "0.3.0-alpha")]
    public void Exports_TheLatestVersionIsCalled(string first, string second, string expected)
    {
        // The versions are ordered by the precedence of semantic versioning, in any order of the exports
        string[] names = ["wasi:cli/run@" + first, "wasi:cli/run@" + second, "wasi:cli/run@0.2.6", "wasi:http/handler@0.3.99"];
        Assert.Equal("wasi:cli/run@" + expected, WasiComponentContext.FindLatestVersion(names, "wasi:cli/run@", "0.3."));
        Assert.Equal("wasi:cli/run@" + expected, WasiComponentContext.FindLatestVersion(names.Reverse(), "wasi:cli/run@", "0.3."));
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The clients dispose the requests")]
    public async Task HttpClients()
    {
        #region WasiHttp_InMemoryClient
        // The requests of the component go to a function of the process, without the network
        var client = new WasiInMemoryHttpClient((request, cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Hello from " + request.RequestUri!.Host),
        }));

        var wasi = new WasiComponentContext(new WasiOptions { HttpClient = client });
        #endregion

        Assert.NotNull(wasi);
        using (var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://api.example.com/"), CancellationToken.None))
        {
            Assert.Equal("Hello from api.example.com", await response.Content.ReadAsStringAsync());
        }

        // The client of the host denies every request by default, and the requests to the hosts that are not allowed
        using var denied = new WasiHostHttpClient(new WasiHostHttpClientOptions());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost/"), CancellationToken.None));
        using var restricted = new WasiHostHttpClient(new WasiHostHttpClientOptions { RequestCheck = WasiHostHttpClientOptions.AllowHosts("api.example.com") });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restricted.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://other.example.com/"), CancellationToken.None));
        Assert.True(WasiHostHttpClientOptions.AllowHosts("API.example.com")(new HttpRequestMessage(HttpMethod.Get, "https://api.example.com:8443/a")));
        Assert.True(WasiHostHttpClientOptions.AllowHosts("::1")(new HttpRequestMessage(HttpMethod.Get, "http://[::1]:8080/")));
        Assert.True(WasiHostHttpClientOptions.AllowHosts("[::1]")(new HttpRequestMessage(HttpMethod.Get, "http://[::1]/")));

        // Without address check, the connections go to the address of the host
        using var allowed = new WasiHostHttpClient(new WasiHostHttpClientOptions { RequestCheck = WasiHostHttpClientOptions.AllowAll, ConnectTimeout = TimeSpan.FromSeconds(10) });
        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => allowed.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:1/"), CancellationToken.None));
        Assert.Equal(HttpRequestError.ConnectionError, refused.HttpRequestError);

        // The address check runs on the addresses of the connections: a name of a denied address fails
        using var loopbackOnly = new WasiHostHttpClient(new WasiHostHttpClientOptions { RequestCheck = WasiHostHttpClientOptions.AllowAll, AddressCheck = (endPoint, use) => false });
        await Assert.ThrowsAsync<HttpRequestException>(() => loopbackOnly.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:1/"), CancellationToken.None));
    }

    [Theory]
    [InlineData("content-type", true)]
    [InlineData("X-Custom_1!", true)]
    [InlineData("", false)]
    [InlineData("bad name", false)]
    [InlineData("bad:name", false)]
    public void HttpSyntax_FieldNames(string name, bool valid) => Assert.Equal(valid, Components.WasiHttpSyntax.IsValidFieldName(name));

    [Fact]
    public void HttpSyntax()
    {
        Assert.True(Components.WasiHttpSyntax.IsValidFieldValue("text/plain; charset=utf-8\t"u8));
        Assert.False(Components.WasiHttpSyntax.IsValidFieldValue("a\r\nb"u8));
        Assert.False(Components.WasiHttpSyntax.IsValidFieldValue([0x7F]));
        Assert.True(Components.WasiHttpSyntax.IsForbiddenHeader("Transfer-Encoding"));
        Assert.False(Components.WasiHttpSyntax.IsForbiddenHeader("content-length"));

        Assert.True(Components.WasiHttpSyntax.IsValidPathWithQuery("/a/b?c=d&e"));
        Assert.True(Components.WasiHttpSyntax.IsValidPathWithQuery("?foo"));
        Assert.True(Components.WasiHttpSyntax.IsValidPathWithQuery("/café"));
        Assert.False(Components.WasiHttpSyntax.IsValidPathWithQuery("a/b"));
        Assert.False(Components.WasiHttpSyntax.IsValidPathWithQuery("/a b"));
        Assert.False(Components.WasiHttpSyntax.IsValidPathWithQuery("/<"));
        Assert.Equal("/a", Components.WasiHttpSyntax.RemoveFragment("/a#b"));

        Assert.True(Components.WasiHttpSyntax.IsValidAuthority("example.com:8080"));
        Assert.True(Components.WasiHttpSyntax.IsValidAuthority("user@[::1]:443"));
        Assert.False(Components.WasiHttpSyntax.IsValidAuthority("example.com/path"));
        Assert.False(Components.WasiHttpSyntax.IsValidAuthority("example.com:99999"));
        Assert.False(Components.WasiHttpSyntax.IsValidAuthority("[not-ipv6]"));

        Assert.True(Components.WasiHttpSyntax.IsValidScheme("coap+tcp"));
        Assert.False(Components.WasiHttpSyntax.IsValidScheme("1http"));
        Assert.True(Components.WasiHttpSyntax.IsValidStatusCode(599));
        Assert.False(Components.WasiHttpSyntax.IsValidStatusCode(600));
    }
}
