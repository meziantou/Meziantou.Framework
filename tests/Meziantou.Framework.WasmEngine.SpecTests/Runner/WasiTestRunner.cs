using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.WasmEngine.Wasi;

namespace Meziantou.Framework.WasmEngine.SpecTests.Runner;

/// <summary>How a test of the WASI test suite runs: the file system of its root directory, and when its functions are compiled to .NET code.</summary>
internal enum WasiMode
{
    /// <summary>A copy of the root directory on the disk (<see cref="WasiHostFileSystem"/>).</summary>
    Host,

    /// <summary>A copy of the root directory in memory (<see cref="WasiInMemoryFileSystem"/>).</summary>
    Memory,

    /// <summary>A copy of the root directory on the disk, the functions compiled to .NET code on their first call.</summary>
    Compiled,
}

/// <summary>
/// Runs a test of the WASI test suite (https://github.com/WebAssembly/wasi-testsuite): a command (a module of WASI
/// Preview 1, or a component of WASI 0.3 for the tests of wasm32-wasip3) whose specification
/// (the JSON file next to it) gives its arguments, environment variables, root directory (preopened as <c>/</c>),
/// expected exit code and outputs (see doc/specification.md of the test suite).
/// </summary>
internal static partial class WasiTestRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>The fetched test suite. Run 'dotnet run eng/wasmengine/fetch-tests.cs' to download it.</summary>
    public static FullPath TestSuite { get; } = SpecTestPaths.Root / "tests" / "Meziantou.Framework.WasmEngine.External" / "wasi-testsuite";

    public static bool IsAvailable => Directory.Exists(TestSuite / "tests");

    /// <summary>Whether the process can create symbolic links (Windows allows them to the administrators and in the developer mode).</summary>
    public static bool CanCreateSymbolicLinks { get; } = ProbeSymbolicLinks();

    private static bool ProbeSymbolicLinks()
    {
        using var directory = TemporaryDirectory.Create();
        try
        {
            File.WriteAllText(directory.GetFullPath("target"), "");
            File.CreateSymbolicLink(directory.GetFullPath("link"), "target");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The suffix of the language of the tests of WASI 0.3 (wasm32-wasip3), which are components.</summary>
    private const string Preview3Suffix = "-wasip3";

    /// <summary>
    /// The tests, as <c>&lt;language&gt;/&lt;name&gt;</c> (e.g. <c>rust/fd_readdir</c>), the language of the tests of
    /// wasm32-wasip3 ending with <c>-wasip3</c> (e.g. <c>rust-wasip3/cli-env</c>).
    /// </summary>
    public static IReadOnlyList<string> GetTests()
    {
        if (!IsAvailable)
            return [];

        var tests = new List<string>();
        foreach (var language in Directory.EnumerateDirectories(TestSuite / "tests"))
        {
            foreach (var (target, suffix) in new[] { ("wasm32-wasip1", ""), ("wasm32-wasip3", Preview3Suffix) })
            {
                var directory = Path.Combine(language, "testsuite", target);
                if (!Directory.Exists(directory))
                    continue;

                foreach (var file in Directory.EnumerateFiles(directory, "*.wasm"))
                {
                    tests.Add(Path.GetFileName(language) + suffix + "/" + Path.GetFileNameWithoutExtension(file));
                }
            }
        }

        return tests.Order(StringComparer.Ordinal).ToArray();
    }

    public static string GetLanguage(string test) => test[..test.IndexOf('/', StringComparison.Ordinal)];

    private static bool IsPreview3(string test) => GetLanguage(test).EndsWith(Preview3Suffix, StringComparison.Ordinal);

    private static FullPath GetDirectory(string test) => IsPreview3(test)
        ? TestSuite / "tests" / GetLanguage(test)[..^Preview3Suffix.Length] / "testsuite" / "wasm32-wasip3"
        : TestSuite / "tests" / GetLanguage(test) / "testsuite" / "wasm32-wasip1";

    private static string GetName(string test) => test[(test.IndexOf('/', StringComparison.Ordinal) + 1)..];

    /// <summary>Runs a test, and returns the reason of its failure, or null when it passes.</summary>
    public static string? Run(string test, WasiMode mode)
    {
        var directory = GetDirectory(test);
        var name = GetName(test);
        var specification = ReadSpecification(directory / (name + ".json"));

        using var temporaryDirectory = TemporaryDirectory.Create();
        var preopened = new List<WasiPreopenedDirectory>();
        if (specification.Root is { } root)
        {
            WasiFileSystem fileSystem;
            if (mode == WasiMode.Memory)
            {
                var memory = new WasiInMemoryFileSystem();
                CopyToMemory(directory / root, memory, "");
                fileSystem = memory;
            }
            else
            {
                CopyDirectory(directory / root, temporaryDirectory.FullPath);
                fileSystem = new WasiHostFileSystem(temporaryDirectory.FullPath);
            }

            preopened.Add(new WasiPreopenedDirectory("/", fileSystem));
        }

        var network = CreateNetwork(specification, mode);
        if (specification.IsInteractive)
            return RunInteractive(directory, name, specification, mode, preopened, network);

        using var standardInput = new MemoryStream(Encoding.UTF8.GetBytes(specification.StandardInput.ToString()));
        using var standardOutput = new MemoryStream();
        using var standardError = new MemoryStream();
        var options = new WasiOptions
        {
            Arguments = [name + ".wasm", .. specification.Arguments],
            EnvironmentVariables = specification.Environment,
            StandardInput = standardInput,
            StandardOutput = standardOutput,
            StandardError = standardError,
            PreopenedDirectories = preopened,
            Network = network,
        };

        int exitCode;
        using (var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = mode == WasiMode.Compiled ? 0 : 100 }))
        {
            var deadline = DateTime.UtcNow + Timeout;
            using var store = runtime.CreateStore(new WasmStoreOptions
            {
                CheckInterrupt = () =>
                {
                    if (DateTime.UtcNow > deadline)
                        throw new TimeoutException("The test did not end after " + Timeout.ToString());
                },
            });

            try
            {
                var bytes = File.ReadAllBytes(directory / (name + ".wasm"));
                if (WasmRuntime.IsComponent(bytes))
                {
                    var context = new WasiComponentContext(options);
                    var component = runtime.CompileComponent(bytes);
                    var instance = store.InstantiateComponent(component, context.DefineImports());
                    exitCode = WasiComponentContext.Run(instance);
                }
                else
                {
                    using var context = new WasiContext(options);
                    var module = runtime.Compile(bytes);
                    var instance = store.Instantiate(module, context.DefineImports(store));
                    exitCode = context.Run(instance);
                }
            }
            catch (Exception ex) when (ex is WasmException or TimeoutException or InvalidOperationException)
            {
                return ex.GetType().Name + ": " + ex.Message + GetOutputs(standardOutput, standardError);
            }
        }

        if (exitCode != specification.ExitCode)
            return "exit code " + exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + " (expected " + specification.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" + GetOutputs(standardOutput, standardError);

        if (specification.StandardOutput is { } expectedOutput && !string.Equals(expectedOutput, Encoding.UTF8.GetString(standardOutput.ToArray()), StringComparison.Ordinal))
            return "unexpected standard output" + GetOutputs(standardOutput, standardError);

        if (specification.StandardError is { } expectedError && !string.Equals(expectedError, Encoding.UTF8.GetString(standardError.ToArray()), StringComparison.Ordinal))
            return "unexpected standard error" + GetOutputs(standardOutput, standardError);

        return null;
    }

    /// <summary>The network of the tests of the sockets: in memory, or the network of the machine (as wasmtime with inherit-network).</summary>
    private static WasiNetwork? CreateNetwork(WasiSpecification specification, WasiMode mode)
    {
        if (!specification.Proposals.Contains("sockets"))
            return null;

        return mode == WasiMode.Memory
            ? new WasiInMemoryNetwork()
            : new WasiHostNetwork(new WasiHostNetworkOptions { AddressCheck = WasiHostNetworkOptions.AllowAll, AllowNameLookup = true });
    }

    private static string GetOutputs(MemoryStream standardOutput, MemoryStream standardError) => GetOutputs(standardOutput.ToArray(), standardError.ToArray());

    private static string GetOutputs(byte[] standardOutput, byte[] standardError)
    {
        var output = Encoding.UTF8.GetString(standardOutput).Trim();
        var error = Encoding.UTF8.GetString(standardError).Trim();
        var result = new StringBuilder();
        if (output.Length > 0)
        {
            result.Append(" | stdout: ").Append(Truncate(output));
        }

        if (error.Length > 0)
        {
            result.Append(" | stderr: ").Append(Truncate(error));
        }

        return result.ToString();

        static string Truncate(string value) => value.Length > 500 ? value[..500] + "..." : value;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void CopyToMemory(string source, WasiInMemoryFileSystem fileSystem, string path)
    {
        fileSystem.AddDirectory(path);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            fileSystem.AddFile(Combine(path, Path.GetFileName(file)), File.ReadAllBytes(file));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyToMemory(directory, fileSystem, Combine(path, Path.GetFileName(directory)));
        }

        static string Combine(string path, string name) => path.Length == 0 ? name : path + "/" + name;
    }

    private static WasiSpecification ReadSpecification(string path)
    {
        var specification = new WasiSpecification();
        if (!File.Exists(path))
            return specification;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var json = document.RootElement;
        if (json.TryGetProperty("proposals", out var proposals))
        {
            specification.Proposals.UnionWith(proposals.EnumerateArray().Select(proposal => proposal.GetString()!));
        }

        if (json.TryGetProperty("world", out var world))
        {
            specification.World = world.GetString();
            specification.IsInteractive |= specification.World == "wasi:http/service";
        }

        if (json.TryGetProperty("servers", out var servers))
        {
            specification.Servers.AddRange(servers.EnumerateArray().Select(server => server.Clone()));
            specification.IsInteractive = true;
        }

        if (json.TryGetProperty("operations", out var operations))
        {
            foreach (var operation in operations.EnumerateArray())
            {
                var type = operation.GetProperty("type").GetString()!;
                specification.Operations.Add(operation.Clone());
                if (type is "connect" or "send" or "recv" or "request" or "kill")
                {
                    specification.IsInteractive = true;
                }

                switch (type)
                {
                    case "run":
                        ReadRun(operation, specification);
                        break;

                    case "wait" when operation.TryGetProperty("exit_code", out var exitCode):
                        specification.ExitCode = exitCode.GetInt32();
                        break;

                    case "read":
                        // The reads of a stream are the successive parts of its content
                        var payload = operation.TryGetProperty("payload", out var value) ? value.GetString() ?? "" : "";
                        if (operation.TryGetProperty("id", out var id) && id.GetString() == "stderr")
                        {
                            specification.StandardError += payload;
                        }
                        else
                        {
                            specification.StandardOutput += payload;
                        }

                        break;

                    case "write":
                        // The input is written before the program runs
                        specification.StandardInput.Append(operation.TryGetProperty("payload", out var input) ? input.GetString() : "");
                        break;
                }
            }

            return specification;
        }

        ReadRun(json, specification);
        if (json.TryGetProperty("exit_code", out var code))
        {
            specification.ExitCode = code.GetInt32();
        }

        if (json.TryGetProperty("stdout", out var stdout))
        {
            specification.StandardOutput = stdout.GetString();
        }

        if (json.TryGetProperty("stderr", out var stderr))
        {
            specification.StandardError = stderr.GetString();
        }

        return specification;
    }

    private static void ReadRun(JsonElement json, WasiSpecification specification)
    {
        if (json.TryGetProperty("args", out var arguments))
        {
            specification.Arguments.AddRange(arguments.EnumerateArray().Select(argument => argument.GetString()!));
        }

        if (json.TryGetProperty("env", out var environment))
        {
            foreach (var variable in environment.EnumerateObject())
            {
                specification.Environment[variable.Name] = variable.Value.GetString()!;
            }
        }

        if (json.TryGetProperty("root", out var root) && root.ValueKind == JsonValueKind.String)
        {
            specification.Root = root.GetString();
        }
    }

    private sealed class WasiSpecification
    {
        public List<string> Arguments { get; } = [];

        public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);

        public string? Root { get; set; }

        public int ExitCode { get; set; }

        public string? StandardOutput { get; set; }

        public string? StandardError { get; set; }

        public StringBuilder StandardInput { get; } = new();

        public HashSet<string> Proposals { get; } = new(StringComparer.Ordinal);

        /// <summary>The operations, in order: the interactive tests run them while the program runs.</summary>
        public List<JsonElement> Operations { get; } = [];

        /// <summary>Whether the operations interact with the running program (the connections, the requests).</summary>
        public bool IsInteractive { get; set; }

        /// <summary>The world of the component, such as <c>wasi:http/service</c> for a component that handles requests.</summary>
        public string? World { get; set; }

        /// <summary>The HTTP servers that the component sends requests to.</summary>
        public List<JsonElement> Servers { get; } = [];
    }
}
