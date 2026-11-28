using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.WasmEngine.Wasi;

namespace Meziantou.Framework.WasmEngine.SpecTests.Runner;

/// <summary>
/// The tests whose operations interact with the running program, as the runner of the test suite does
/// (test-runner/wasi_test_runner/test_suite_runner.py): the program runs on another thread while the operations read
/// its outputs, connect to the address it prints, and wait for its end.
/// </summary>
internal static partial class WasiTestRunner
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The connections are disposed at the end of the test")]
    private static string? RunInteractive(FullPath directory, string name, WasiSpecification specification, WasiMode mode, List<WasiPreopenedDirectory> preopened, WasiNetwork? network)
    {
        using var standardInput = new MemoryStream(Encoding.UTF8.GetBytes(specification.StandardInput.ToString()));
        using var standardOutput = new RunnerPipe();
        using var standardError = new RunnerPipe();
        using var http = new HttpTestEnvironment(specification, mode);
        var environment = new Dictionary<string, string>(specification.Environment, StringComparer.Ordinal);
        foreach (var (variable, value) in http.ServerVariables)
        {
            environment[variable] = value;
        }

        var options = new WasiOptions
        {
            Arguments = [name + ".wasm", .. specification.Arguments],
            EnvironmentVariables = environment,
            StandardInput = standardInput,
            StandardOutput = standardOutput,
            StandardError = standardError,
            PreopenedDirectories = preopened,
            Network = network,
            HttpClient = http.Client,
        };

        var connections = new Dictionary<string, Stream>(StringComparer.Ordinal);
        Task<int>? program = null;
        try
        {
            foreach (var operation in specification.Operations)
            {
                var failure = operation.GetProperty("type").GetString() switch
                {
                    "run" => Start(),
                    "read" => Read(operation),
                    "connect" => Connect(operation),
                    "send" => Send(operation),
                    "recv" => Receive(operation),
                    "wait" => Wait(operation),
                    "request" => http.Request(operation, OperationTimeout),
                    "kill" => http.Kill(),
                    _ => null,
                };

                if (failure is not null)
                    return failure + GetOutputs(standardOutput.ToArray(), standardError.ToArray());
            }

            if (program is { IsCompleted: false })
                return "the program did not end" + GetOutputs(standardOutput.ToArray(), standardError.ToArray());

            return null;
        }
        finally
        {
            foreach (var connection in connections.Values)
            {
                connection.Dispose();
            }

            standardOutput.Complete();
            standardError.Complete();
        }

        string? Start()
        {
            program = specification.World == "wasi:http/service"
                ? http.StartService(directory / (name + ".wasm"), options, mode)
                : Task.Factory.StartNew(() => RunProgram(directory, name, options, mode), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            return null;
        }

        string? Read(JsonElement operation)
        {
            var payload = GetPayload(operation);
            var pipe = operation.TryGetProperty("id", out var id) && id.GetString() == "stderr" ? standardError : standardOutput;
            var bytes = pipe.ReadExactly(Encoding.UTF8.GetByteCount(payload), OperationTimeout);
            var actual = Encoding.UTF8.GetString(bytes);
            return string.Equals(actual, payload, StringComparison.Ordinal) ? null : "read: expected '" + payload + "', got '" + actual + "'";
        }

        string? Connect(JsonElement operation)
        {
            // The program prints the address it listens on: <host>:<port>
            var line = standardOutput.ReadLine(OperationTimeout)?.Trim();
            var separator = line?.LastIndexOf(":", StringComparison.Ordinal) ?? -1;
            if (line is null || separator < 0 || !IPAddress.TryParse(line[..separator], out var address) || !int.TryParse(line[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port))
                return "connect: expected the address as <host>:<port>, got '" + line + "'";

            var endPoint = new IPEndPoint(address, port);
            Stream stream;
            try
            {
                if (network is WasiInMemoryNetwork memory)
                {
                    stream = memory.ConnectAsync(endPoint).AsTask().WaitAsync(OperationTimeout).GetAwaiter().GetResult();
                }
                else
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        socket.Connect(endPoint);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }

                    stream = new NetworkStream(socket, ownsSocket: true) { ReadTimeout = (int)OperationTimeout.TotalMilliseconds };
                }
            }
            catch (Exception ex) when (ex is SocketException or TimeoutException)
            {
                return "connect: " + ex.Message;
            }

            connections[operation.GetProperty("id").GetString()!] = stream;
            return null;
        }

        string? Send(JsonElement operation)
        {
            connections[operation.GetProperty("id").GetString()!].Write(Encoding.UTF8.GetBytes(GetPayload(operation)));
            return null;
        }

        string? Receive(JsonElement operation)
        {
            var payload = GetPayload(operation);
            var stream = connections[operation.GetProperty("id").GetString()!];
            var expected = Encoding.UTF8.GetBytes(payload);
            var buffer = new byte[expected.Length];
            var read = 0;
            try
            {
                while (read < buffer.Length)
                {
                    var task = stream.ReadAsync(buffer.AsMemory(read)).AsTask();
                    var count = task.WaitAsync(OperationTimeout).GetAwaiter().GetResult();
                    if (count == 0)
                        break;

                    read += count;
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or TimeoutException)
            {
                return "recv: " + ex.Message;
            }

            var actual = Encoding.UTF8.GetString(buffer, 0, read);
            return string.Equals(actual, payload, StringComparison.Ordinal) ? null : "recv: expected '" + payload + "', got '" + actual + "'";
        }

        string? Wait(JsonElement operation)
        {
            if (program is null)
                return "wait: the program is not running";

            if (!((IAsyncResult)program).AsyncWaitHandle.WaitOne(OperationTimeout))
                return "wait: the program did not end after " + OperationTimeout.ToString();

            if (program.Exception?.InnerException is { } exception)
                return exception.GetType().Name + ": " + exception.Message;

            var expected = operation.TryGetProperty("exit_code", out var code) ? code.GetInt32() : 0;
            return program.Result == expected ? null : "exit code " + program.Result.ToString(CultureInfo.InvariantCulture) + " (expected " + expected.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }

    private static string GetPayload(JsonElement operation) => operation.TryGetProperty("payload", out var payload) ? payload.GetString() ?? "" : "";

    /// <summary>Runs a component of a test on the current thread, and returns its exit code.</summary>
    private static int RunProgram(FullPath directory, string name, WasiOptions options, WasiMode mode)
    {
        using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = mode == WasiMode.Compiled ? 0 : 100 });
        using var store = runtime.CreateStore();
        try
        {
            var context = new WasiComponentContext(options);
            var component = runtime.CompileComponent(File.ReadAllBytes(directory / (name + ".wasm")));
            var instance = store.InstantiateComponent(component, context.DefineImports());
            return WasiComponentContext.Run(instance);
        }
        finally
        {
            ((RunnerPipe)options.StandardOutput!).Complete();
            ((RunnerPipe)options.StandardError!).Complete();
        }
    }

    /// <summary>A stream that the program writes and the runner reads, from two threads.</summary>
    private sealed class RunnerPipe : Stream
    {
        private readonly object _lock = new();
        private readonly List<byte> _all = [];
        private int _position;
        private bool _completed;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public byte[] ToArray()
        {
            lock (_lock)
            {
                return [.. _all];
            }
        }

        public void Complete()
        {
            lock (_lock)
            {
                _completed = true;
                Monitor.PulseAll(_lock);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                _all.AddRange(buffer.AsSpan(offset, count));
                Monitor.PulseAll(_lock);
            }
        }

        /// <summary>Reads the next line, or null when the program ends or the time is out.</summary>
        public string? ReadLine(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            lock (_lock)
            {
                while (true)
                {
                    var end = _all.IndexOf((byte)'\n', _position);
                    if (end >= 0)
                    {
                        var line = Encoding.UTF8.GetString([.. _all.GetRange(_position, end - _position)]);
                        _position = end + 1;
                        return line;
                    }

                    var remaining = deadline - DateTime.UtcNow;
                    if (_completed || remaining <= TimeSpan.Zero)
                        return null;

                    Monitor.Wait(_lock, remaining);
                }
            }
        }

        /// <summary>Reads a number of bytes, or fewer when the program ends or the time is out.</summary>
        public byte[] ReadExactly(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            lock (_lock)
            {
                while (_all.Count - _position < count)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (_completed || remaining <= TimeSpan.Zero)
                        break;

                    Monitor.Wait(_lock, remaining);
                }

                var available = Math.Min(count, _all.Count - _position);
                var bytes = _all.GetRange(_position, available).ToArray();
                _position += available;
                return bytes;
            }
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
