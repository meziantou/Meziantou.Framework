using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Fuzz;

/// <summary>
/// The loader of the static files maps the path of a URL to a file of a folder: whatever the URL (encoded separators, dot
/// segments, other hosts...), it serves only the files of the folder, and the file of the URL path when it exists. The
/// detection of the long-lived requests accepts any request.
/// </summary>
public static class AspNetCoreTestingFuzzTargets
{
    private static readonly Lazy<(string Root, DocumentLoader Loader)> Folder = new(CreateFolder);

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("aspnetcore-testing-static-files", StaticFiles) { Seeds = GetSeeds, Dictionary = ["/", "..", "%2e", "%2f", "%5c", "\\", "?", "#", "//", "%00", "secret.txt", "public.txt", "sub/", "static-secret/", "https://localhost", "https://example.com", ":", "@"] },
        new("aspnetcore-testing-long-lived-requests", LongLivedRequests) { Seeds = GetSeeds, Dictionary = ["?", "&", "id=", "_=", "=", "#", "https://localhost/_blazor"] },
        .. LocatorFuzzTargets.All,
        .. Components.ComponentFuzzTargets.All,
    ];

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        yield return FuzzSeed.FromText("public", "/static/public.txt");
        yield return FuzzSeed.FromText("subfolder", "/static/sub/public.txt");
        yield return FuzzSeed.FromText("dot-segments", "/static/../secret.txt");
        yield return FuzzSeed.FromText("encoded-dot-segments", "/static/%2e%2e/secret.txt");
        yield return FuzzSeed.FromText("encoded-separator", "/static/..%2fsecret.txt");
        yield return FuzzSeed.FromText("backslash", "/static/..\\secret.txt");
        yield return FuzzSeed.FromText("sibling-folder", "/static/..%2fstatic-secret/secret.txt");
        yield return FuzzSeed.FromText("long-polling", "/_blazor?id=abc&_=123");
    }

    private static (string Root, DocumentLoader Loader) CreateFolder()
    {
        // root/secret.txt and root/static-secret/secret.txt are outside of the served folder root/static (the name of the
        // second folder starts with the name of the served folder)
        var root = Path.Combine(Path.GetTempPath(), "mfw-aspnetcore-testing-fuzz-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(Path.Combine(root, "static", "sub"));
        Directory.CreateDirectory(Path.Combine(root, "static-secret"));
        File.WriteAllText(Path.Combine(root, "secret.txt"), "secret");
        File.WriteAllText(Path.Combine(root, "static-secret", "secret.txt"), "secret");
        File.WriteAllText(Path.Combine(root, "static", "public.txt"), "public");
        File.WriteAllText(Path.Combine(root, "static", "sub", "public.txt"), "public");
        var loader = TestDocumentLoaders.FromDirectories(new Uri("https://localhost/"), [new("/static", Path.Combine(root, "static"))]);
        return (root, loader);
    }

    private static void StaticFiles(ReadOnlySpan<byte> data)
    {
        var path = FuzzText.Decode(data);
        var url = path.StartsWith("http", StringComparison.Ordinal) ? path : "https://localhost" + (path.StartsWith('/', StringComparison.Ordinal) ? path : "/" + path);
        var (_, loader) = Folder.Value;
        var resource = loader.LoadAsync(new DocumentRequest(url), CancellationToken.None).AsTask().GetAwaiter().GetResult();
        if (resource is null || resource.StatusCode == 404)
            return;

        FuzzAssert.Equal(200, resource.StatusCode, "The loader returns 200, 404 or a network error");
        var content = resource.ContentBytes is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes.Span) : resource.Content;
        FuzzAssert.Equal("public", content, $"The loader served a file outside of the folder for '{url}'");
    }

    private static void LongLivedRequests(ReadOnlySpan<byte> data)
    {
        var url = FuzzText.Decode(data);
        DocumentRequest request;
        try
        {
            request = new DocumentRequest("https://localhost" + (url.StartsWith('/', StringComparison.Ordinal) ? url : "/" + url));
        }
        catch (ArgumentException)
        {
            return;
        }

        _ = WebAppBrowserOptions.IsDefaultLongLivedRequest(request);
    }
}
