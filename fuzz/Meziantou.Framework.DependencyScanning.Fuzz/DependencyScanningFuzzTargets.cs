using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.DependencyScanning.Fuzz;

/// <summary>
/// The default scanners scan a file of an in-memory repository. A scanner may only fail on a document it cannot parse
/// (<see cref="XmlException"/>, <see cref="JsonException"/>); any other exception reported to
/// <see cref="ScannerOptions.OnFileScanFailed"/> is a failure. A dependency with a name or a version has a location for it,
/// the line of a location is in the file, and updating an updatable location to the value it holds succeeds and leaves
/// the file unchanged: otherwise the reported value is not at the reported location.
/// </summary>
public static partial class DependencyScanningFuzzTargets
{
    private const string Root = "/repo";

    // Updating a location reads and rewrites the whole file, so only the first ones are checked
    private const int MaxCheckedUpdates = 32;

    // Separates the files of a scenario that reads several files
    private static readonly byte[] FileSeparator = "\0--next-file--\0"u8.ToArray();

    /// <summary>The file the input is written to, and the other files that the scanners of this file read, filled from the rest of the input.</summary>
    private static readonly Scenario[] Scenarios =
    [
        new("Cargo.toml"),
        new("Cargo.lock"),
        new("composer.json"),
        new("composer.lock"),
        new("compose.yaml"),
        new("docker-compose.yml"),
        new("Dockerfile"),
        new("app.Containerfile"),
        new("app.cs"),
        new("global.json"),
        new(".config/dotnet-tools.json"),
        new(".github/workflows/actions.lock"),
        new(".github/workflows/ci.yml"),
        new("actions/build/action.yml"),
        new(".gitmodules", ".git/index", ".git/config"),
        new("go.mod"),
        new("go.sum"),
        new("Chart.yaml"),
        new("Chart.lock"),
        new("v1/requirements.yaml"),
        new("v1/requirements.lock"),
        new("pom.xml"),
        new("build.gradle"),
        new("build.gradle.kts"),
        new("gradle/libs.versions.toml"),
        new("k8s/manifests.yaml"),
        new("test.csproj"),
        new("Directory.Packages.props"),
        new("package.json"),
        new("package-lock.json"),
        new("npm-shrinkwrap.json"),
        new("packages.lock.json"),
        new("test.nuspec"),
        new("packages.config", "test.csproj"),
        new("obj/project.assets.json"),
        new("project.json"),
        new("requirements.txt"),
        new("pyproject.toml"),
        new("Pipfile"),
        new("poetry.lock"),
        new("uv.lock"),
        new("Pipfile.lock"),
        new("renovate.json"),
        new(".github/renovate.json5"),
        new("Gemfile"),
        new("sample.gemspec"),
        new("Gemfile.lock"),
        new("Package.swift"),
        new("Package@swift-5.9.swift"),
        new("Package.resolved"),
        new(".claude/settings.json"),
        new(".claude-plugin/marketplace.json"),
        new("azure-pipelines.yml"),
        new("sample.yml"),
        new("Native.vcxproj"),
        new("packages.Project.config", "Project.csproj"),
        new(".github/workflows/sample.yml"),
    ];

    private static readonly string[] Tokens =
    [
        "\0--next-file--\0", "{", "}", "[", "]", ":", ",", "\"", "'", "=", "==", ">=", "<=", "~=", "!=", "^", "~>", "@", "#", "/", "\\", "\n", "\r\n", "\t", "  ", "- ", "\"version\"", "\"dependencies\"", "\"devDependencies\"", "\"packages\"",
        "\"name\"", "\"source\"", "\"resolved\"", "\"integrity\"", "\"lockfileVersion\"", "\"sdk\"", "\"tools\"", "\"require\"", "\"extends\"", "\"plugins\"", "\"repo\"", "\"ref\"", "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
        "<Project Sdk=\"Microsoft.NET.Sdk\">", "<PackageReference Include=\"", "Version=\"", "<PackageVersion Include=\"", "<package id=\"", "version=\"", "<dependency>", "<groupId>", "<artifactId>", "<version>", "<parent>", "<properties>", "${",
        "<dependency id=\"", "&amp;", "&#x41;", "<!--", "-->", "<![CDATA[", "]]>", "/>", "</", "FROM ", " AS ", "ARG ", "--platform=", "image: ", "uses: ", "@v4", "@sha256:", "services:", "steps:", "jobs:", "container:", "resources:", "repositories:", "template: ",
        "[dependencies]", "[dev-dependencies]", "[workspace.dependencies]", "[[package]]", "[versions]", "[libraries]", "[project]", "[tool.poetry.dependencies]", "[packages]", "version = ", "name = ", "require (", "module ", "go ", "replace ", " => ", "h1:",
        "gem ", "source ", "specs:", "GEM", "DEPENDENCIES", ".package(url: ", "from: ", "exact: ", "implementation ", "implementation(\"", "#:package ", "#:sdk ", "[submodule \"", "path = ", "url = ", "DIRC", "-r ", "--hash=", "; python_version",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("dependency-scan", Scan) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    /// <summary>The first byte selects the scenario, the rest is the content of its files.</summary>
    private static void Scan(ReadOnlySpan<byte> data)
    {
        var scenario = FuzzInput.Pick(Scenarios, FuzzInput.ReadByte(ref data));
        var fileSystem = new InMemoryFileSystem();
        var filePath = Root + "/" + scenario.Path;
        fileSystem.AddFile(filePath, (scenario.ExtraFiles.Length is 0 ? data : ReadFile(ref data)).ToArray());
        foreach (var extraFile in scenario.ExtraFiles)
        {
            fileSystem.AddFile(Root + "/" + extraFile, ReadFile(ref data).ToArray());
        }

        var failures = new List<Exception>();
        var options = new ScannerOptions
        {
            FileSystem = fileSystem,
            DegreeOfParallelism = 1,
            OnFileScanFailed = (path, exception) => failures.Add(exception),
        };

        var dependencies = DependencyScanner.ScanFileAsync(Root, filePath, options).GetAwaiter().GetResult();
        foreach (var failure in failures)
        {
            if (failure is not XmlException and not JsonException)
                throw new FuzzAssertionException($"A scanner failed on {scenario.Path}", failure);
        }

        foreach (var dependency in dependencies)
        {
            FuzzAssert.True(dependency.Name is null || dependency.NameLocation is not null, $"The name of {dependency} has no location");
            FuzzAssert.True(dependency.Version is null || dependency.VersionLocation is not null, $"The version of {dependency} has no location");
            CheckLineInfo(fileSystem, dependency, dependency.NameLocation);
            CheckLineInfo(fileSystem, dependency, dependency.VersionLocation);
        }

        var checkedUpdates = 0;
        foreach (var dependency in dependencies)
        {
            foreach (var (location, value) in (ReadOnlySpan<(Location?, string?)>)[(dependency.NameLocation, dependency.Name), (dependency.VersionLocation, dependency.Version)])
            {
                // A git submodule commit is updated by running git
                if (location is null || value is null || !location.IsUpdatable || location.GetType().Name is "GitSubmoduleVersionLocation")
                    continue;

                if (checkedUpdates++ >= MaxCheckedUpdates)
                    return;

                var original = fileSystem.GetContent(location.FilePath);
                FuzzAssert.True(original is not null, $"The location {location} is not in a file of the input");
                try
                {
                    location.UpdateAsync(value, value).GetAwaiter().GetResult();
                }
                catch (DependencyScannerException ex) when (IsUnsupportedUpdate(ex))
                {
                    continue;
                }
                catch (Exception ex)
                {
                    throw new FuzzAssertionException($"Updating {dependency} at {location} to the value it holds ({FuzzAssert.Format(value)}) failed", ex);
                }

                var updated = fileSystem.GetContent(location.FilePath)!;
                if (!original.AsSpan().SequenceEqual(updated))
                {
                    // The value may be written with another escaping than the original text, such as '>' as "&gt;" in an
                    // XML attribute: the file must still hold the same dependencies
                    var rescanned = DependencyScanner.ScanFileAsync(Root, filePath, options).GetAwaiter().GetResult();
                    FuzzAssert.Equal(Describe(dependencies), Describe(rescanned), $"Updating {dependency} at {location} to the value it holds ({FuzzAssert.Format(value)}) changed the dependencies of the file: {FuzzAssert.Format(Encoding.UTF8.GetString(updated))}");

                    // The other locations were recorded on the original text
                    fileSystem.AddFile(location.FilePath, original);
                }
            }
        }
    }

    private static string Describe(IEnumerable<Dependency> dependencies)
    {
        return string.Join('\n', dependencies.Select(dependency => $"{dependency.Type} {FuzzAssert.Format(dependency.Name)} {FuzzAssert.Format(dependency.Version)}").Order(StringComparer.Ordinal));
    }

    /// <summary>The update refuses to write, as documented, rather than finding another value than the reported one.</summary>
    private static bool IsUnsupportedUpdate(DependencyScannerException exception)
    {
        // The file is not valid in its encoding: rewriting it would change the bytes that cannot be decoded
        if (exception.InnerException is DecoderFallbackException or EncoderFallbackException)
            return true;

        // An XML value is only updated when it is a single run of text whose references can be mapped back onto the file
        var message = exception.Message;
        return message.StartsWith("Cannot update value of a self-closing XML element", StringComparison.Ordinal)
            || message.StartsWith("Cannot update the value of the XML element", StringComparison.Ordinal)
            || message.Contains("contains an entity reference that cannot be resolved", StringComparison.Ordinal)
            || message.StartsWith("The recorded location splits a character reference", StringComparison.Ordinal);
    }

    private static void CheckLineInfo(InMemoryFileSystem fileSystem, Dependency dependency, Location? location)
    {
        if (location is not ILocationLineInfo lineInfo)
            return;

        // 0,0 means that the location has no line information
        if (lineInfo is { LineNumber: 0, LinePosition: 0 })
            return;

        FuzzAssert.True(lineInfo.LineNumber >= 1 && lineInfo.LinePosition >= 1, $"Invalid line information {lineInfo.LineNumber},{lineInfo.LinePosition} for {dependency}");
        var content = fileSystem.GetContent(location.FilePath);
        FuzzAssert.True(content is not null, $"The location {location} is not in a file of the input");
        var lineCount = CountLines(content);
        FuzzAssert.True(lineInfo.LineNumber <= lineCount, $"The location {location} of {dependency} is after the last line ({lineCount})");
    }

    /// <summary>Counts the lines of the text the way the most lenient of the parsers does: \r\n, \r, \n, NEL, LS and PS each end a line.</summary>
    private static int CountLines(byte[] content)
    {
        string text;
        using (var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            text = reader.ReadToEnd();
        }

        var count = 1;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n':
                    break;
                case '\r' or '\n' or '\u0085' or '\u2028' or '\u2029':
                    count++;
                    break;
            }
        }

        return count;
    }

    private static ReadOnlySpan<byte> ReadFile(ref ReadOnlySpan<byte> data)
    {
        var index = data.IndexOf(FileSeparator);
        if (index < 0)
        {
            var all = data;
            data = default;
            return all;
        }

        var result = data[..index];
        data = data[(index + FileSeparator.Length)..];
        return result;
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        foreach (var (path, content) in TestFiles.Read(FuzzResources.ReadText(typeof(DependencyScanningFuzzTargets).Assembly, "ScannerTests.cs")))
        {
            var index = FindScenario(path);
            if (index < 0)
                continue;

            yield return new FuzzSeed("test-" + path, CreateInput(index, Encoding.UTF8.GetBytes(content)));
        }

        yield return new FuzzSeed("gitmodules-with-index", CreateInput(
            FindScenario(".gitmodules"),
            """
            [submodule "lib"]
                path = lib
                url = https://github.com/meziantou/lib.git
                branch = main
            [submodule "relative"]
                path = deps/relative
                url = ../relative.git
            """u8.ToArray(),
            CreateGitIndex(("lib", 0xAB), ("deps/relative", 0xCD), ("readme.md", 0x00)),
            """
            [core]
                repositoryformatversion = 0
            [remote "origin"]
                url = https://github.com/meziantou/repo.git
            """u8.ToArray()));

        yield return new FuzzSeed("packages-config-with-project", CreateInput(
            FindScenario("packages.config"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="Newtonsoft.Json" version="12.0.1" targetFramework="net452" />
              <package id="NUnit" version="3.11.0" targetFramework="net452" />
            </packages>
            """u8.ToArray(),
            """
            <Project ToolsVersion="15.0">
              <ItemGroup>
                <Reference Include="Newtonsoft.Json, Version=12.0.0.0, Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed">
                  <HintPath>..\packages\Newtonsoft.Json.12.0.1\lib\net45\Newtonsoft.Json.dll</HintPath>
                </Reference>
              </ItemGroup>
              <Import Project="..\packages\NUnit.3.11.0\build\NUnit.props" Condition="Exists('..\packages\NUnit.3.11.0\build\NUnit.props')" />
            </Project>
            """u8.ToArray()));

        yield return new FuzzSeed("project-packages-config-with-project", CreateInput(
            FindScenario("packages.Project.config"),
            "<packages>\n  <package id=\"Microsoft.Net.Compilers\" version=\"2.10.0\" developmentDependency=\"true\" />\n</packages>\n"u8.ToArray(),
            "<Project>\n  <Import Project=\"..\\packages\\Microsoft.Net.Compilers.2.10.0\\build\\Microsoft.Net.Compilers.props\" />\n  <Error Condition=\"!Exists('..\\packages\\Microsoft.Net.Compilers.2.10.0\\build\\Microsoft.Net.Compilers.props')\" Text=\"Missing\" />\n</Project>\n"u8.ToArray()));

        yield return new FuzzSeed("utf16-package-json", CreateInput(FindScenario("package.json"), [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("""{ "dependencies": { "react": "^18.2.0" } }""")]));
        yield return new FuzzSeed("renovate", CreateInput(FindScenario("renovate.json"), """{ "extends": ["config:recommended", "github>meziantou/renovate-config#v1.0.0"] }"""u8.ToArray()));
        yield return new FuzzSeed("compose", CreateInput(FindScenario("compose.yaml"), "services:\n  web:\n    image: nginx:1.25\n  db:\n    image: \"postgres:16@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"\n"u8.ToArray()));
        yield return new FuzzSeed("directory-packages-props", CreateInput(FindScenario("Directory.Packages.props"), "<Project>\n  <ItemGroup>\n    <PackageVersion Include=\"xunit\" Version=\"2.9.0\" />\n    <GlobalPackageReference Include=\"Meziantou.Analyzer\" Version=\"2.0.1\" />\n  </ItemGroup>\n</Project>\n"u8.ToArray()));
    }

    private static int FindScenario(string path)
    {
        var index = Array.FindIndex(Scenarios, scenario => scenario.Path == path);
        if (index >= 0)
            return index;

        // A file with the same name is scanned by the same scanners, unless they filter on the directory
        var fileName = Path.GetFileName(path);
        index = Array.FindIndex(Scenarios, scenario => Path.GetFileName(scenario.Path) == fileName);
        if (index >= 0)
            return index;

        var extension = Path.GetExtension(path);
        return extension is ".yml" or ".yaml" or ".csproj" or ".cs" ? Array.FindIndex(Scenarios, scenario => Path.GetExtension(scenario.Path) == extension) : -1;
    }

    private static byte[] CreateInput(int scenario, params byte[][] files)
    {
        var result = new List<byte> { (byte)scenario };
        for (var i = 0; i < files.Length; i++)
        {
            if (i > 0)
            {
                result.AddRange(FileSeparator);
            }

            result.AddRange(files[i]);
        }

        return [.. result];
    }

    /// <summary>A version 2 git index: entries sorted by path, gitlinks (mode 160000) for the submodules, then the checksum.</summary>
    private static byte[] CreateGitIndex(params (string Path, byte HashByte)[] entries)
    {
        var result = new List<byte>();
        result.AddRange("DIRC"u8);
        AddUInt32(result, 2);
        AddUInt32(result, (uint)entries.Length);
        foreach (var (path, hashByte) in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            var entryStart = result.Count;
            result.AddRange(new byte[24]); // ctime, mtime, dev, ino
            AddUInt32(result, hashByte is 0 ? 0x81A4u : 0xE000u); // regular file or gitlink
            result.AddRange(new byte[12]); // uid, gid, size
            result.AddRange(Enumerable.Repeat(hashByte, 20));
            var name = Encoding.UTF8.GetBytes(path);
            result.Add((byte)(name.Length >> 8));
            result.Add((byte)name.Length);
            result.AddRange(name);
            do
            {
                result.Add(0);
            }
            while ((result.Count - entryStart) % 8 != 0);
        }

        result.AddRange(new byte[20]);
        return [.. result];

        static void AddUInt32(List<byte> list, uint value)
        {
            list.Add((byte)(value >> 24));
            list.Add((byte)(value >> 16));
            list.Add((byte)(value >> 8));
            list.Add((byte)value);
        }
    }

    private sealed record Scenario(string Path, params string[] ExtraFiles);
}
