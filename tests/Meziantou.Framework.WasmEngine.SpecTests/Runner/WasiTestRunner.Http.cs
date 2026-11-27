using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.WasmEngine.Wasi;

namespace Meziantou.Framework.WasmEngine.SpecTests.Runner;

internal static partial class WasiTestRunner
{
    /// <summary>
    /// The HTTP of a test of wasi:http: the servers of the specification (on the loopback address, or in memory through a
    /// <see cref="WasiInMemoryHttpClient"/>), the client of the component, and the service of a component of the world
    /// wasi:http/service (behind a server on the loopback address, or called directly in memory).
    /// </summary>
    private sealed class HttpTestEnvironment : IDisposable
    {
        private readonly List<IDisposable> _disposables = [];
        private readonly Dictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>?> _routes = new(StringComparer.Ordinal);
        private BlockingCollection<(HttpRequestMessage Request, TaskCompletionSource<HttpResponseMessage> Response)>? _requests;
        private Uri? _serviceUri;
        private Task<int>? _service;

        public HttpTestEnvironment(WasiSpecification specification, WasiMode mode)
        {
            var port = 50000;
            foreach (var server in specification.Servers)
            {
                var name = server.GetProperty("name").GetString()!;
                var closed = server.TryGetProperty("kind", out var kind) && kind.GetString() == "closed";
                string authority;
                if (mode == WasiMode.Memory)
                {
                    authority = "127.0.0.1:" + (port++).ToString(CultureInfo.InvariantCulture);
                    _routes[authority] = closed ? null : WasiHttpTestServer.CreateEndpoints(server);
                }
                else if (closed)
                {
                    // A port that was free: the connections are refused
                    using var reserved = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    reserved.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    authority = "127.0.0.1:" + ((IPEndPoint)reserved.LocalEndPoint!).Port.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    var listening = new WasiHttpTestServer.Server(WasiHttpTestServer.CreateEndpoints(server));
                    _disposables.Add(listening);
                    authority = "127.0.0.1:" + listening.Port.ToString(CultureInfo.InvariantCulture);
                }

                ServerVariables.Add(("HTTP_SERVER_" + name.ToUpperInvariant(), authority));
            }

            if (!specification.Proposals.Contains("http"))
                return;

            if (mode == WasiMode.Memory)
            {
                Client = new WasiInMemoryHttpClient((request, cancellationToken) => RouteAsync(request));
            }
            else
            {
                var client = new WasiHostHttpClient(new WasiHostHttpClientOptions { RequestCheck = WasiHostHttpClientOptions.AllowAll });
                _disposables.Add(client);
                Client = client;
            }
        }

        public List<(string Name, string Value)> ServerVariables { get; } = [];

        public WasiHttpClient? Client { get; }

        private Task<HttpResponseMessage> RouteAsync(HttpRequestMessage request)
        {
            if (!_routes.TryGetValue(request.RequestUri!.Authority, out var route) || route is null)
                return Task.FromException<HttpResponseMessage>(new HttpRequestException(HttpRequestError.ConnectionError, "connection refused", new SocketException((int)SocketError.ConnectionRefused)));

            return route(request);
        }

        /// <summary>Runs a component of the world wasi:http/service on a thread of its own: it handles the requests one at a time, until the test kills it.</summary>
        public Task<int> StartService(string path, WasiOptions options, WasiMode mode)
        {
            var requests = new BlockingCollection<(HttpRequestMessage, TaskCompletionSource<HttpResponseMessage>)>();
            _requests = requests;
            var task = Task.Factory.StartNew(() =>
            {
                using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = mode == WasiMode.Compiled ? 0 : 100 });
                using var store = runtime.CreateStore();
                var context = new WasiComponentContext(options);
                var instance = store.InstantiateComponent(runtime.CompileComponent(File.ReadAllBytes(path)), context.DefineImports());
                foreach (var (request, response) in requests.GetConsumingEnumerable())
                {
                    try
                    {
                        response.SetResult(context.HandleHttpRequest(instance, request));
                    }
                    catch (Exception ex)
                    {
                        response.SetException(ex);
                        throw;
                    }
                }

                return 0;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            _service = task;
            if (mode != WasiMode.Memory)
            {
                var server = new WasiHttpTestServer.Server(EnqueueAsync);
                _disposables.Add(server);
                _serviceUri = new Uri("http://127.0.0.1:" + server.Port.ToString(CultureInfo.InvariantCulture));
            }

            return task;
        }

        private Task<HttpResponseMessage> EnqueueAsync(HttpRequestMessage request)
        {
            var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_requests is not { IsAddingCompleted: false } requests)
                return Task.FromException<HttpResponseMessage>(new InvalidOperationException("The service is stopped"));

            requests.Add((request, response));
            return response.Task;
        }

        /// <summary>Stops the service: the component ends with the exit code 0.</summary>
        public string? Kill()
        {
            if (_requests is null)
                return "kill: only the services can be killed";

            _requests.CompleteAdding();
            return null;
        }

        /// <summary>Sends a request to the service, and compares its response: the status, the expected headers (a subset), and the body.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed", Justification = "The request is awaited before it is disposed")]
        public string? Request(JsonElement operation, TimeSpan timeout)
        {
            if (_requests is null)
                return "request: the service is not running";

            var method = operation.TryGetProperty("method", out var m) ? m.GetString()! : "GET";
            var path = operation.TryGetProperty("path", out var p) ? p.GetString()! : "/";
            using var request = new HttpRequestMessage(new HttpMethod(method), new Uri((_serviceUri ?? new Uri("http://127.0.0.1")) + path.TrimStart('/')));
            if (operation.TryGetProperty("body", out var body) && body.GetString() is { Length: > 0 } text)
            {
                request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
            }

            if (operation.TryGetProperty("headers", out var headers))
            {
                foreach (var header in headers.EnumerateObject())
                {
                    request.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString());
                }
            }

            HttpResponseMessage response;
            try
            {
                if (_serviceUri is null)
                {
                    var pending = EnqueueAsync(request);
                    _ = Task.WaitAny([pending, _service!], timeout);
                    if (_service!.IsFaulted)
                        return "request " + method + " " + path + ": the service failed: " + _service.Exception!.InnerException!.GetType().Name + ": " + _service.Exception.InnerException.Message;

                    response = pending.WaitAsync(TimeSpan.Zero).GetAwaiter().GetResult();
                }
                else
                {
                    using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
                    using var client = new HttpClient(handler, disposeHandler: false) { Timeout = timeout };
                    response = client.Send(request);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException or WasmException or InvalidOperationException)
            {
                return "request " + method + " " + path + ": " + ex.GetType().Name + ": " + ex.Message;
            }

            using (response)
            {
                var expected = operation.TryGetProperty("response", out var e) ? e : default;
                var status = expected.ValueKind == JsonValueKind.Object && expected.TryGetProperty("status", out var s) ? s.GetInt32() : 200;
                if ((int)response.StatusCode != status)
                    return "request " + method + " " + path + ": status " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + " (expected " + status.ToString(CultureInfo.InvariantCulture) + ")";

                if (expected.ValueKind == JsonValueKind.Object && expected.TryGetProperty("headers", out var expectedHeaders))
                {
                    foreach (var header in expectedHeaders.EnumerateObject())
                    {
                        var actual = response.Headers.TryGetValues(header.Name, out var values) || response.Content.Headers.TryGetValues(header.Name, out values) ? string.Join(", ", values) : null;
                        if (!string.Equals(actual, header.Value.GetString(), StringComparison.Ordinal))
                            return "request " + method + " " + path + ": header " + header.Name + " is '" + actual + "' (expected '" + header.Value.GetString() + "')";
                    }
                }

                var expectedBody = expected.ValueKind == JsonValueKind.Object && expected.TryGetProperty("body", out var b) ? b.GetString() : "";
                var actualBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!string.Equals(actualBody, expectedBody, StringComparison.Ordinal))
                    return "request " + method + " " + path + ": body '" + actualBody + "' (expected '" + expectedBody + "')";
            }

            return null;
        }

        public void Dispose()
        {
            _requests?.CompleteAdding();
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }

            _requests?.Dispose();
        }
    }
}
