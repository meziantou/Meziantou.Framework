#pragma warning disable CA1812 // Avoid uninstantiated internal classes
#pragma warning disable MA0004 // Use Task.ConfigureAwait
#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
#pragma warning disable CA1849 // Call async methods when in an async method
#pragma warning disable MA0042 // Do not use blocking calls in an async method
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Meziantou.Framework;

// Refreshes the URL parser conformance corpus of the web-platform-tests project that UrlPatternWebPlatformTests
// runs. Like the other generators, it returns 1 when it updated the files and 0 when they were up to date.
const string CorpusPath = "url/resources/urltestdata.json";

if (!FullPath.CurrentDirectory().TryFindGitRepositoryRoot(out var root))
    throw new InvalidOperationException("Cannot find git root from " + FullPath.CurrentDirectory());

var filesPath = root / "tests" / "Meziantou.Framework.Uri.Tests" / "files";
var corpusFilePath = filesPath / "urltestdata.json";
var licenseFilePath = filesPath / "urltestdata.LICENSE.md";

// The corpus is pinned to the last commit that changed it, so that the pin only moves when the content does
var commitSha = await GetLastCommitSha(CorpusPath);
var corpusUrl = $"https://raw.githubusercontent.com/web-platform-tests/wpt/{commitSha}/{CorpusPath}";
var licenseUrl = $"https://raw.githubusercontent.com/web-platform-tests/wpt/{commitSha}/LICENSE.md";

Console.WriteLine("Downloading " + corpusUrl);
var corpus = await SharedHttpClient.Instance.GetByteArrayAsync(corpusUrl);
Console.WriteLine("Downloading " + licenseUrl);
var license = await SharedHttpClient.Instance.GetStringAsync(licenseUrl);

// The tests only read the objects of the array; the strings in it are comments
using (var document = JsonDocument.Parse(corpus))
{
    var caseCount = document.RootElement.EnumerateArray().Count(element => element.ValueKind is JsonValueKind.Object);
    if (caseCount is 0)
        throw new InvalidOperationException("The URL parser corpus has no test case");

    Console.WriteLine($"{caseCount.ToString(CultureInfo.InvariantCulture)} test cases");
}

var licenseFile = $"""
    The file `urltestdata.json` in this directory is the URL parser conformance corpus of the
    web-platform-tests project, vendored unmodified from:

      https://github.com/web-platform-tests/wpt/blob/{commitSha}/{CorpusPath}

    To refresh it, run `dotnet run --project tools/Meziantou.Framework.Uri.UrlTestData.Generator`, which the
    update-url-test-data workflow does every week. It is covered by the following license, reproduced from
    https://github.com/web-platform-tests/wpt/blob/{commitSha}/LICENSE.md

    ---


    """.ReplaceLineEndings("\n") + license.ReplaceLineEndings("\n");

var updated = false;
updated |= WriteBytesIfChanged(corpusFilePath, corpus);
updated |= WriteBytesIfChanged(licenseFilePath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(licenseFile));

if (!updated)
{
    Console.WriteLine("The URL parser corpus is up to date");
    return 0;
}

Console.WriteLine("The files have been updated");
RunGitDiff(root);
return 1;

static async Task<string> GetLastCommitSha(string path)
{
    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (string.IsNullOrEmpty(token))
    {
        // gh auth token
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "gh",
            Arguments = "auth token",
            RedirectStandardOutput = true,
            UseShellExecute = false,
        });
        process!.WaitForExit();
        token = process.StandardOutput.ReadToEnd().Trim();
    }

    using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/web-platform-tests/wpt/commits?path={path}&per_page=1");
    request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Meziantou.Framework.Uri.UrlTestData.Generator", "1.0"));
    if (!string.IsNullOrEmpty(token))
    {
        request.Headers.Add("Authorization", "Bearer " + token);
    }

    using var response = await SharedHttpClient.Instance.SendAsync(request);
    response.EnsureSuccessStatusCode();
    using var commits = await response.Content.ReadFromJsonAsync<JsonDocument>();
    return commits!.RootElement.EnumerateArray().First().GetProperty("sha").GetString()!;
}

static bool WriteBytesIfChanged(FullPath path, byte[] content)
{
    // The corpus is vendored unmodified, so it is compared and written byte for byte
    if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
        return false;

    File.WriteAllBytes(path, content);
    return true;
}

static void RunGitDiff(FullPath root)
{
    var process = Process.Start(new ProcessStartInfo
    {
        FileName = "git",
        Arguments = "--no-pager diff --stat",
        WorkingDirectory = root,
        UseShellExecute = false,
    });

    process?.WaitForExit();
}
