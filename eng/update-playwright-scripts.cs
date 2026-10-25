#!/usr/bin/env dotnet
#:sdk Meziantou.NET.Sdk
#:project ../src/Meziantou.Framework.FullPath/Meziantou.Framework.FullPath.csproj
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meziantou.Framework;

// Updates Meziantou.Framework.WebPlatform.Playwright to a version of Playwright (the latest stable version of the
// Microsoft.Playwright NuGet package by default):
// - the scripts of Playwright it vendors (the injected script and the utility script, see
//   src/Meziantou.Framework.WebPlatform.Playwright/Scripts/README.md): they are extracted from the bundle of the playwright-core
//   npm package, where they are JavaScript string literals. Nothing of the package is executed;
// - PlaywrightServer.PlaywrightVersion (generated in PlaywrightServer.Version.g.cs), the version of Microsoft.Playwright of
//   the tests and of the samples (the client must have the major and minor versions of the server), and the versions written
//   in the documentation.
//
// The version comes from NuGet: Microsoft.Playwright x.y.z bundles playwright-core x.y.z, which is published to npm first.
// The changes of the protocol (packages/protocol/spec of the Playwright repository) must still be reviewed.
//
// Usage: dotnet run ./eng/update-playwright-scripts.cs [-- <version>]
// Exit code: 0 when everything is up to date, 1 when files were updated (as the other update scripts of the repository), and
// 2 for an error.
if (args.Length > 1 || args is ["--help" or "-h"])
{
    Console.WriteLine("Usage: dotnet run ./eng/update-playwright-scripts.cs [-- <playwright version>]");
    Console.WriteLine("Without version, the latest stable version of the Microsoft.Playwright NuGet package is used.");
    return args.Length == 1 ? 0 : 2;
}

try
{
    return await UpdateAsync(args is [var version] ? version : null);
}
#pragma warning disable CA1031 // Any error is reported with its own exit code: 1 means "updated"
catch (Exception ex)
#pragma warning restore CA1031
{
    Console.Error.WriteLine(ex);
    return 2;
}

static async Task<int> UpdateAsync(string? version)
{
    var root = FullPath.CurrentDirectory().FindRequiredGitRepositoryRoot();
    var projectDirectory = root / "src" / "Meziantou.Framework.WebPlatform.Playwright";
    var scriptsDirectory = projectDirectory / "Scripts";
    var versionPath = projectDirectory / "PlaywrightServer.Version.g.cs";

    using var httpClient = new HttpClient();
    version ??= await GetLatestClientVersionAsync(httpClient);
    var currentVersion = Regex.Match(await File.ReadAllTextAsync(versionPath), """PlaywrightVersion = "(?<version>[^"]+)";""", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)) is { Success: true } match
        ? match.Groups["version"].Value
        : throw new InvalidOperationException("PlaywrightServer.PlaywrightVersion was not found in " + versionPath);
    Console.WriteLine($"Current version: {currentVersion}, target version: {version}");

    var url = $"https://registry.npmjs.org/playwright-core/-/playwright-core-{version}.tgz";
    Console.WriteLine("Downloading " + url);
    await using var tarball = await httpClient.GetStreamAsync(new Uri(url));
    await using var gzip = new GZipStream(tarball, CompressionMode.Decompress);
    using var tar = new TarReader(gzip);

    var files = new Dictionary<string, string>(StringComparer.Ordinal);
    while (tar.GetNextEntry() is { } entry)
    {
        if (entry.Name is "package/lib/coreBundle.js" or "package/LICENSE" or "package/NOTICE" && entry.DataStream is { } dataStream)
        {
            using var reader = new StreamReader(dataStream, Encoding.UTF8);
            files[entry.Name] = await reader.ReadToEndAsync();
        }
    }

    if (!files.TryGetValue("package/lib/coreBundle.js", out var bundle))
        throw new InvalidOperationException("lib/coreBundle.js was not found in the package (the layout of the package changed)");

    var changed = false;
    changed |= await WriteAsync(scriptsDirectory / "injectedScriptSource.js", ExtractScript(bundle, "packages/playwright-core/src/generated/injectedScriptSource.ts\"() {"));
    changed |= await WriteAsync(scriptsDirectory / "utilityScriptSource.js", ExtractScript(bundle, "packages/playwright-core/src/generated/utilityScriptSource.ts\"() {"));
    changed |= await WriteAsync(scriptsDirectory / "LICENSE.txt", files["package/LICENSE"]);
    changed |= await WriteAsync(scriptsDirectory / "NOTICE.txt", files["package/NOTICE"]);
    changed |= await WriteAsync(versionPath, GenerateVersionFile(version));

    if (currentVersion != version)
    {
        // The other files that contain the version of Playwright (the full version, and the major and minor versions in the readme)
        FullPath[] versionFiles =
        [
            scriptsDirectory / "README.md",
            projectDirectory / "THIRD-PARTY-NOTICES.md",
            root / "tests" / "Meziantou.Framework.WebPlatform.Playwright.Tests" / "Meziantou.Framework.WebPlatform.Playwright.Tests.csproj",
            root / "samples" / "Meziantou.Framework.WebPlatform.Playwright.Samples" / "Meziantou.Framework.WebPlatform.Playwright.Samples.csproj",
        ];
        foreach (var path in versionFiles)
        {
            var content = await File.ReadAllTextAsync(path);
            if (!content.Contains(currentVersion, StringComparison.Ordinal))
                throw new InvalidOperationException($"The version {currentVersion} was not found in {path}");

            changed |= await WriteAsync(path, content.Replace(currentVersion, version, StringComparison.Ordinal));
        }

        var readmePath = projectDirectory / "readme.md";
        var readme = await File.ReadAllTextAsync(readmePath);
        var majorMinorPattern = new Regex(@"(?<prefix>`PlaywrightServer\.PlaywrightVersion`\s+\()[0-9]+\.[0-9]+\)", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        if (!majorMinorPattern.IsMatch(readme))
            throw new InvalidOperationException("The version of Playwright was not found in " + readmePath);

        changed |= await WriteAsync(readmePath, majorMinorPattern.Replace(readme, "${prefix}" + GetMajorMinor(version) + ")"));
    }

    if (!changed)
    {
        Console.WriteLine($"Playwright {version} is up to date");
        return 0;
    }

    Console.WriteLine();
    Console.WriteLine($"Updated to Playwright {version}. Then:");
    Console.WriteLine("  - review the changes of the protocol (packages/protocol/spec of the Playwright repository) since the previous version;");
    Console.WriteLine("  - regenerate ref/Meziantou.Framework.WebPlatform.Playwright.cs (build the project), and run the tests.");
    return 1;
}

static async Task<string> GetLatestClientVersionAsync(HttpClient httpClient)
{
    await using var stream = await httpClient.GetStreamAsync(new Uri("https://api.nuget.org/v3-flatcontainer/microsoft.playwright/index.json"));
    using var document = await JsonDocument.ParseAsync(stream);
    return document.RootElement.GetProperty("versions").EnumerateArray()
        .Select(version => version.GetString()!)
        .Where(version => !version.Contains('-', StringComparison.Ordinal))
        .MaxBy(Version.Parse)
        ?? throw new InvalidOperationException("No stable version of Microsoft.Playwright was found");
}

static string GenerateVersionFile(string version) => $$"""
    // <auto-generated />
    // Generated by eng/update-playwright-scripts.cs
    #nullable enable

    namespace Meziantou.Framework.WebPlatform.Playwright;

    partial class PlaywrightServer
    {
        /// <summary>The version of Playwright whose protocol the server implements (the clients must have the same major and minor versions).</summary>
        public const string PlaywrightVersion = "{{version}}";
    }

    """;

static string GetMajorMinor(string version)
{
    var parsed = Version.Parse(version);
    return parsed.Major.ToString(CultureInfo.InvariantCulture) + "." + parsed.Minor.ToString(CultureInfo.InvariantCulture);
}

// Returns true when the content of the file changed
static async Task<bool> WriteAsync(FullPath path, string content)
{
    if (File.Exists(path) && await File.ReadAllTextAsync(path) == content)
        return false;

    await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine("Wrote " + path);
    return true;
}

// The script is the first string literal assigned after the marker of its module (e.g. "source5 = '...';")
static string ExtractScript(string bundle, string marker)
{
    var markerIndex = bundle.IndexOf(marker, StringComparison.Ordinal);
    if (markerIndex < 0)
        throw new InvalidOperationException("The module was not found in the bundle: " + marker);

    var start = bundle.IndexOf("= '", markerIndex, StringComparison.Ordinal);
    if (start < 0)
        throw new InvalidOperationException("The string literal of the module was not found: " + marker);

    return DecodeJavaScriptString(bundle, start + 2);
}

// Decodes a single-quoted JavaScript string literal that starts at the given index
static string DecodeJavaScriptString(string text, int quoteIndex)
{
    var result = new StringBuilder();
    for (var i = quoteIndex + 1; i < text.Length; i++)
    {
        var c = text[i];
        if (c == '\'')
            return result.ToString();

        if (c != '\\')
        {
            result.Append(c);
            continue;
        }

        var escaped = text[++i];
        switch (escaped)
        {
            case 'n':
                result.Append('\n');
                break;
            case 'r':
                result.Append('\r');
                break;
            case 't':
                result.Append('\t');
                break;
            case 'b':
                result.Append('\b');
                break;
            case 'f':
                result.Append('\f');
                break;
            case 'v':
                result.Append('\v');
                break;
            case '0' when i + 1 >= text.Length || !char.IsAsciiDigit(text[i + 1]):
                result.Append('\0');
                break;
            case 'x':
                result.Append((char)int.Parse(text.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 2;
                break;
            case 'u' when text[i + 1] == '{':
                var end = text.IndexOf("}", i, StringComparison.Ordinal);
                result.Append(char.ConvertFromUtf32(int.Parse(text.AsSpan(i + 2, end - i - 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                i = end;
                break;
            case 'u':
                result.Append((char)int.Parse(text.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 4;
                break;
            case '\r':
                // A line continuation
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                break;
            case '\n':
                break;
            default:
                result.Append(escaped);
                break;
        }
    }

    throw new InvalidOperationException("Unterminated string literal");
}
