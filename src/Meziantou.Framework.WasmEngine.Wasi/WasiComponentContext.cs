using Meziantou.Framework.WasmEngine.Components;
using Meziantou.Framework.WasmEngine.Wasi.Components;

namespace Meziantou.Framework.WasmEngine.Wasi;

/// <summary>
/// The environment of a WASI component (WASI 0.2 and WASI 0.3, https://github.com/WebAssembly/WASI): its arguments,
/// environment variables, standard streams, clocks, random numbers, and the file system of its preopened directories.
/// Define its imports (<see cref="DefineImports"/>), instantiate the component, and run it (<see cref="Run"/>).
/// </summary>
/// <remarks>
/// <para>
/// The imports are the interfaces of <c>wasi:cli</c>, <c>wasi:io</c>, <c>wasi:clocks</c>, <c>wasi:random</c> and
/// <c>wasi:filesystem</c>, in version 0.2.12 and 0.3.2: a component that imports an older version of the same major
/// version (0.2.x or 0.3.x) uses them too. The sockets and HTTP are not supported.
/// </para>
/// <para>
/// A context is the state of one program: use one context per instance, from the thread of the store. The asynchronous
/// functions of WASI 0.3 and the threads of the components are not supported on the browser.
/// </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiComponentContextTests.cs" ref:region="WasiComponent_GettingStarted" ref:dedent="true">
/// using var runtime = new WasmRuntime();
///
/// // A component that exports wasi:cli/run, compiled from its binary (File.ReadAllBytes("app.wasm")) or its text
/// var component = runtime.CompileComponent(WasmRuntime.WatToWasm(text));
///
/// using var output = new MemoryStream();
/// var wasi = new WasiComponentContext(new WasiOptions
/// {
///     Arguments = ["app.wasm"],
///     StandardOutput = output,
/// });
///
/// using var store = runtime.CreateStore();
/// var instance = store.InstantiateComponent(component, wasi.DefineImports());
/// var exitCode = WasiComponentContext.Run(instance);
///
/// Assert.Equal(0, exitCode);
/// Assert.Equal("Hello, WASI!\n", Encoding.UTF8.GetString(output.ToArray()));
/// </code>
/// </example>
public sealed class WasiComponentContext
{
    private readonly WasiPreview2 _preview2;
    private readonly WasiPreview3 _preview3;

    /// <summary>Creates the environment of a component.</summary>
    /// <param name="options">The options, or null for a program without arguments, environment variables, input, output and files.</param>
    /// <exception cref="ArgumentException">The name of an environment variable is empty or contains <c>=</c>.</exception>
    public WasiComponentContext(WasiOptions? options = null)
    {
        var state = new WasiComponentState(options ?? new WasiOptions());
        _preview2 = new WasiPreview2(state);
        _preview3 = new WasiPreview3(state);
    }

    /// <summary>Defines the interfaces of WASI 0.2 and WASI 0.3.</summary>
    /// <param name="imports">The imports where the interfaces are defined, or null to create them.</param>
    /// <returns>The imports.</returns>
    public ComponentImports DefineImports(ComponentImports? imports = null)
    {
        imports ??= new ComponentImports();
        _preview2.Define(imports);
        _preview3.Define(imports);
        return imports;
    }

    /// <summary>
    /// Runs a command: calls the function <c>run</c> of the export <c>wasi:cli/run</c> of the instance (WASI 0.3 when the
    /// component exports both versions), and returns the exit code of the program: 0 when <c>run</c> returns <c>ok</c>, 1
    /// when it returns <c>error</c>, or the code given to <c>wasi:cli/exit</c>.
    /// </summary>
    /// <param name="instance">The instance of the component.</param>
    /// <returns>The exit code.</returns>
    /// <exception cref="InvalidOperationException">The component does not export <c>wasi:cli/run</c>.</exception>
    /// <exception cref="WasmTrapException">The component trapped.</exception>
    public static int Run(WasmComponentInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var export = FindRunExport(instance, "0.3.") ?? FindRunExport(instance, "0.2.") ?? throw new InvalidOperationException("The component does not export wasi:cli/run");
        var run = instance.GetFunction(export + "#run") ?? throw new InvalidOperationException("The export " + export + " has no function run");
        try
        {
            var result = run.Call();
            return result is { } value && value.IsCase("ok") ? 0 : 1;
        }
        catch (WasiExitException ex)
        {
            return ex.ExitCode;
        }
    }

    /// <summary>
    /// Handles an HTTP request with a component of the world <c>wasi:http/service</c>: calls the function
    /// <c>handle</c> of its export <c>wasi:http/handler</c> (WASI 0.3), and returns its response once the component wrote
    /// its body. The component sends its own requests with <see cref="WasiOptions.HttpClient"/>.
    /// </summary>
    /// <remarks>
    /// The call runs on the current thread, as the thread of the store: handle the requests of a store one at a time. The
    /// body of the response is read before the method returns. An error of the handler is a response with the status 500.
    /// </remarks>
    /// <param name="instance">The instance of the component, whose imports are the imports of this context.</param>
    /// <param name="request">The request: its URI is absolute, or its header Host gives the authority.</param>
    /// <param name="cancellationToken">The token to stop reading the body of the response.</param>
    /// <returns>The response.</returns>
    /// <exception cref="InvalidOperationException">The component does not export <c>wasi:http/handler</c>.</exception>
    /// <exception cref="WasmTrapException">The component trapped.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiComponentContextTests.cs" ref:region="WasiHttp_Service" ref:dedent="true">
    /// using var runtime = new WasmRuntime();
    ///
    /// // A component of the world wasi:http/service: it exports wasi:http/handler
    /// var component = runtime.CompileComponent(WasmRuntime.WatToWasm(text));
    /// var wasi = new WasiComponentContext();
    /// using var store = runtime.CreateStore();
    /// var instance = store.InstantiateComponent(component, wasi.DefineImports());
    ///
    /// // Each call handles a request on the thread of the store: a server of the host calls it for each request
    /// using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/items");
    /// using var response = wasi.HandleHttpRequest(instance, request);
    /// Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    /// </code>
    /// </example>
    public HttpResponseMessage HandleHttpRequest(WasmComponentInstance instance, HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(request);
        var export = FindExport(instance, "wasi:http/handler@", "0.3.") ?? throw new InvalidOperationException("The component does not export wasi:http/handler");
        var handle = instance.GetFunction(export + "#handle") ?? throw new InvalidOperationException("The export " + export + " has no function handle");
        return instance.Run(() => _preview3.Http.HandleAsync(handle, request, cancellationToken));
    }

    private static string? FindRunExport(WasmComponentInstance instance, string versionPrefix) => FindExport(instance, "wasi:cli/run@", versionPrefix);

    private static string? FindExport(WasmComponentInstance instance, string prefix, string versionPrefix)
    {
        return instance.Exports
            .Select(export => export.Name)
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.AsSpan(prefix.Length).StartsWith(versionPrefix, StringComparison.Ordinal))
            .Select(name => (Name: name, Version: ParseVersion(name[prefix.Length..])))
            // The latest version: 0.3.10 is after 0.3.9, and a release is after its pre-releases (0.3.0-rc-...)
            .OrderByDescending(export => export.Version.Number)
            .ThenBy(export => export.Version.PreRelease is not null)
            .ThenByDescending(export => export.Version.PreRelease, StringComparer.Ordinal)
            .Select(export => export.Name)
            .FirstOrDefault();
    }

    private static (Version? Number, string? PreRelease) ParseVersion(string version)
    {
        var separator = version.IndexOf('-', StringComparison.Ordinal);
        var number = separator < 0 ? version : version[..separator];
        return (Version.TryParse(number, out var parsed) ? parsed : null, separator < 0 ? null : version[(separator + 1)..]);
    }
}
