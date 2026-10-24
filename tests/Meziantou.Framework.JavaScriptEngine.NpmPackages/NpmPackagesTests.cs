using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Wasmtime;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.JavaScriptEngine.NpmPackages;

/// <summary>
/// Runs the workloads of workloads/*.mjs, which import npm packages as ES modules, in a page of
/// Meziantou.Framework.WebPlatform (page.html and runner.mjs), and compares their results with the results of Chromium
/// (expected/*.txt, written by generate.js). See readme.md.
/// </summary>
public sealed class NpmPackagesTests
{
    private const string Origin = "https://npm.test";

    private static readonly FullPath Root = FullPath.FromPath(AppContext.BaseDirectory).FindRequiredGitRepositoryRoot();

    private static readonly FullPath ProjectDirectory = Root / "tests" / "Meziantou.Framework.JavaScriptEngine.NpmPackages";

    /// <summary>The npm packages of package-lock.json and their import map. Run 'dotnet run eng/javascriptengine/fetch-tests.cs' to install them.</summary>
    private static readonly FullPath PackagesDirectory = Root / "tests" / "Meziantou.Framework.JavaScriptEngine.External" / "npm-packages";

    private static readonly WasmtimeRuntime WebAssemblyRuntime = new();

    private static readonly Lazy<string> Page = new(() => File.ReadAllText(ProjectDirectory / "page.html").Replace("{{importmap}}", File.ReadAllText(PackagesDirectory / "importmap.json"), StringComparison.Ordinal));

    public static TheoryData<string> Workloads
    {
        get
        {
            var data = new TheoryData<string>();
            if (!File.Exists(PackagesDirectory / "importmap.json"))
                return data;

            foreach (var path in Directory.GetFiles(ProjectDirectory / "workloads", "*.mjs").Select(FullPath.FromPath).Order())
            {
                data.Add(path.NameWithoutExtension);
            }

            return data;
        }
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Workloads), DisableDiscoveryEnumeration = true)]
    public async Task Run(string workload)
    {
        var expected = File.ReadAllText(ProjectDirectory / "expected" / (workload + ".txt"));
        var (result, errors) = await RunWorkloadAsync(workload);
        if (result != expected)
        {
            var resultPath = FullPath.GetTempPath() / "npm-packages" / (workload + ".txt");
            resultPath.CreateParentDirectory();
            File.WriteAllText(resultPath, result);
            Assert.Fail($"The result of '{workload}' differs from the result of Chromium. The result is in '{resultPath}'.{string.Concat(errors.Select(error => "\n" + error))}");
        }
    }

    private static async Task<(string Result, List<string> Errors)> RunWorkloadAsync(string workload)
    {
        var errors = new List<string>();

        // The timers run in virtual time: the clock advances to the next timer when there is nothing else to do
        var clock = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var engine = new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions
        {
            Timeout = TimeSpan.FromMinutes(5),
            MemoryLimit = null,
            RecursionLimit = 10_000,
            RegexTimeout = TimeSpan.FromMinutes(1),
            TimeProvider = clock,
            ConfigureEngine = options =>
            {
                options.TimeZone = "UTC";
                options.DefaultLocale = "en-US";
            },
        });
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            ScriptEngine = engine,
            WebAssemblyRuntime = WebAssemblyRuntime,
            DocumentLoader = new SiteLoader(errors),
            TimeProvider = clock,
            UnhandledScriptError = (_, error) => errors.Add("unhandled error: " + error),
        });

        context.Navigate(Origin + "/?workload=" + Uri.EscapeDataString(workload));
        var eventLoop = context.EventLoop;
        var cancellationToken = TestContext.Current.CancellationToken;
        while (true)
        {
            await eventLoop.RunUntilIdleAsync(cancellationToken);
            var document = context.Document;
            if (document.DocumentElement?.HasAttribute("data-done") is true)
                return (document.GetElementById("result")?.TextContent ?? "", errors);

            var nextTimer = eventLoop.PromoteDueTimers();
            if (nextTimer is null)
            {
                if (!eventLoop.HasPendingWork && !eventLoop.HasPendingOperations)
                    return ("Error: the workload did not complete", errors);
            }
            else if (nextTimer.Value > TimeSpan.Zero)
            {
                clock.Advance(nextTimer.Value);
            }
        }
    }

    /// <summary>The files of the site, as generate.js serves them to Chromium: the page, the runner and the workloads of this directory, and the installed packages.</summary>
    private sealed class SiteLoader(List<string> errors) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            var url = new Uri(request.Url);
            var path = Uri.UnescapeDataString(url.AbsolutePath);
            if (path is "/")
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource(Page.Value) { Headers = [new("Content-Type", "text/html")] });

            var fullPath = path.StartsWith("/node_modules/", StringComparison.Ordinal) ? PackagesDirectory / path.TrimStart('/')
                : path is "/runner.mjs" || path.StartsWith("/workloads/", StringComparison.Ordinal) ? ProjectDirectory / path.TrimStart('/')
                : FullPath.Empty;
            if (fullPath.IsEmpty || !File.Exists(fullPath))
            {
                lock (errors)
                {
                    errors.Add("404 " + request.Url);
                }

                return ValueTask.FromResult<DocumentResource?>(new DocumentResource("") { StatusCode = 404 });
            }

            var contentType = fullPath.Extension switch
            {
                ".js" or ".mjs" or ".cjs" => "text/javascript",
                ".json" => "application/json",
                ".css" => "text/css",
                ".wasm" => "application/wasm",
                ".html" => "text/html",
                _ => "application/octet-stream",
            };
            return ValueTask.FromResult<DocumentResource?>(new DocumentResource(File.ReadAllBytes(fullPath)) { Headers = [new("Content-Type", contentType)] });
        }
    }
}
