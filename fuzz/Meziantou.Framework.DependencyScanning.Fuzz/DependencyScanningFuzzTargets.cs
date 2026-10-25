using System.Buffers;
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
/// the file unchanged: otherwise the reported value is not at the reported location. Updating a location to another value
/// writes this value and nothing else, the in-memory overloads, the dependency type filters, and the directory and file
/// list scans agree with the scan of the file, and an update the file cannot take is refused as documented.
/// </summary>
public static partial class DependencyScanningFuzzTargets
{
    private const string Root = "/repo";

    // Updating a location reads and rewrites the whole file, so only the first ones are checked
    private const int MaxCheckedUpdates = 32;

    // Each update to other values scans the file three times
    private const int MaxCheckedValueChanges = 8;

    // Each type is scanned twice: included, then excluded
    private const int MaxCheckedDependencyTypes = 4;

    // The characters that JSON strings and XML values escape
    private const string EscapedCharacters = "\"'<&> \t\n\\";

    private static readonly IReadOnlyList<DependencyScanner> DefaultScanners = new ScannerOptions().Scanners;

    private static readonly SearchValues<char> AsciiLetters = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");
    private static readonly SearchValues<char> NameCharacters = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._/");

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

        var files = fileSystem.Snapshot();
        var dependencies = ScanFile(fileSystem, filePath, out var hasFailures);
        foreach (var dependency in dependencies)
        {
            FuzzAssert.True(dependency.Name is null || dependency.NameLocation is not null, $"The name of {dependency} has no location");
            FuzzAssert.True(dependency.Version is null || dependency.VersionLocation is not null, $"The version of {dependency} has no location");
            CheckLineInfo(fileSystem, dependency, dependency.NameLocation);
            CheckLineInfo(fileSystem, dependency, dependency.VersionLocation);
            CheckNotUpdatable(dependency, dependency.NameLocation);
            CheckNotUpdatable(dependency, dependency.VersionLocation);
        }

        CheckInMemoryScan(scenario, fileSystem, filePath, dependencies);
        CheckDependencyTypeFilters(fileSystem, filePath, dependencies, hasFailures);
        CheckMultipleFileScans(fileSystem, filePath, dependencies);
        CheckUpdatesToSameValue(fileSystem, filePath, dependencies);
        CheckUpdatesToNewValues(fileSystem, filePath, files);
    }

    /// <summary>Scans a file with the default scanners, and checks that the scanners only failed on a document they cannot parse.</summary>
    private static IReadOnlyCollection<Dependency> ScanFile(InMemoryFileSystem fileSystem, string filePath, out bool hasFailures, Action<ScannerOptions>? configure = null)
    {
        var failures = new List<(string Path, Exception Exception)>();
        var options = CreateOptions(fileSystem, failures);
        configure?.Invoke(options);
        var dependencies = DependencyScanner.ScanFileAsync(Root, filePath, options).GetAwaiter().GetResult();
        CheckFailures(failures);
        hasFailures = failures.Count > 0;
        return dependencies;
    }

    private static ScannerOptions CreateOptions(InMemoryFileSystem fileSystem, List<(string Path, Exception Exception)> failures)
    {
        return new ScannerOptions
        {
            FileSystem = fileSystem,
            DegreeOfParallelism = 1,
            OnFileScanFailed = (path, exception) =>
            {
                lock (failures)
                {
                    failures.Add((path, exception));
                }
            },
        };
    }

    private static void CheckFailures(List<(string Path, Exception Exception)> failures)
    {
        foreach (var (path, exception) in failures)
        {
            if (exception is not XmlException and not JsonException)
                throw new FuzzAssertionException($"A scanner failed on {path}", exception);
        }
    }

    /// <summary>A location that is not updatable refuses to write, as documented, rather than failing in another way.</summary>
    private static void CheckNotUpdatable(Dependency dependency, Location? location)
    {
        if (location is null || location.IsUpdatable)
            return;

        try
        {
            location.UpdateAsync("1.0.0").GetAwaiter().GetResult();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new FuzzAssertionException($"Updating the location {location} of {dependency}, which is not updatable, did not throw an {nameof(InvalidOperationException)}", ex);
        }

        FuzzAssert.Fail($"Updating the location {location} of {dependency}, which is not updatable, succeeded");
    }

    /// <summary>Scanning the content of the file from memory finds the same dependencies as scanning the file, and none of their locations is updatable.</summary>
    private static void CheckInMemoryScan(Scenario scenario, InMemoryFileSystem fileSystem, string filePath, IReadOnlyCollection<Dependency> dependencies)
    {
        var content = fileSystem.GetContent(filePath)!;
        var expected = dependencies;
        if (scenario.ExtraFiles.Length > 0)
        {
            // The in-memory content is the only file the scanners can read
            var singleFileSystem = new InMemoryFileSystem();
            singleFileSystem.AddFile(filePath, content);
            expected = ScanFile(singleFileSystem, filePath, out _);
        }

        var actual = content.Length % 2 is 0
            ? DependencyScanner.ScanFileAsync(Root, filePath, content).GetAwaiter().GetResult()
            : DependencyScanner.ScanFileAsync(Root, filePath, content, DefaultScanners).GetAwaiter().GetResult();
        FuzzAssert.Equal(Describe(expected), Describe(actual), "Scanning the content from memory found other dependencies than scanning the file");
        foreach (var dependency in actual)
        {
            foreach (var location in (ReadOnlySpan<Location?>)[dependency.NameLocation, dependency.VersionLocation])
            {
                FuzzAssert.True(location is null || !location.IsUpdatable, $"The location {location} of {dependency} scanned from memory is updatable");
                CheckNotUpdatable(dependency, location);
            }
        }
    }

    /// <summary>Including or excluding a dependency type finds the dependencies of the full scan that have, or do not have, this type.</summary>
    private static void CheckDependencyTypeFilters(InMemoryFileSystem fileSystem, string filePath, IReadOnlyCollection<Dependency> dependencies, bool hasFailures)
    {
        // A failure skips the scanners after the one that failed, which a filter may not even run
        if (hasFailures)
            return;

        foreach (var type in dependencies.Select(dependency => dependency.Type).Distinct().Order().Take(MaxCheckedDependencyTypes))
        {
            var included = ScanFile(fileSystem, filePath, out _, options => options.IncludedDependencyTypes = [type]);
            FuzzAssert.Equal(Describe(dependencies.Where(dependency => dependency.Type == type)), Describe(included), $"Including the {type} dependencies found other dependencies than the full scan");

            var excluded = ScanFile(fileSystem, filePath, out _, options => options.ExcludedDependencyTypes = [type]);
            FuzzAssert.Equal(Describe(dependencies.Where(dependency => dependency.Type != type)), Describe(excluded), $"Excluding the {type} dependencies found other dependencies than the full scan");
        }
    }

    /// <summary>Scanning the directory or the list of its files finds the dependencies of each file scanned on its own.</summary>
    private static void CheckMultipleFileScans(InMemoryFileSystem fileSystem, string filePath, IReadOnlyCollection<Dependency> dependencies)
    {
        var paths = fileSystem.Paths;
        var expected = new List<Dependency>();
        foreach (var path in paths)
        {
            expected.AddRange(path == filePath ? dependencies : ScanFile(fileSystem, path, out _));
        }

        var failures = new List<(string Path, Exception Exception)>();
        var directory = DependencyScanner.ScanDirectoryAsync(Root, CreateOptions(fileSystem, failures)).GetAwaiter().GetResult();
        var list = DependencyScanner.ScanFilesAsync(Root, paths, CreateOptions(fileSystem, failures)).GetAwaiter().GetResult();
        CheckFailures(failures);
        FuzzAssert.Equal(Describe(expected), Describe(directory), "Scanning the directory found other dependencies than scanning its files one at a time");
        FuzzAssert.Equal(Describe(expected), Describe(list), "Scanning the list of files found other dependencies than scanning them one at a time");
    }

    /// <summary>Updating a location to the value it holds succeeds, and leaves the file unchanged or holding the same dependencies.</summary>
    private static void CheckUpdatesToSameValue(InMemoryFileSystem fileSystem, string filePath, IReadOnlyCollection<Dependency> dependencies)
    {
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
                    var rescanned = ScanFile(fileSystem, filePath, out _);
                    FuzzAssert.Equal(Describe(dependencies), Describe(rescanned), $"Updating {dependency} at {location} to the value it holds ({FuzzAssert.Format(value)}) changed the dependencies of the file: {FuzzAssert.Format(Encoding.UTF8.GetString(updated))}");

                    // The other locations were recorded on the original text
                    fileSystem.AddFile(location.FilePath, original);
                }
            }
        }
    }

    /// <summary>
    /// Updating a location to another value writes this value: scanning the file again finds it in place of the old
    /// one, and the other dependencies of the file are unchanged. A second update without an expected value replaces the
    /// value written by the first one, and an update that expects a value the file does not hold, or that writes a value
    /// the file cannot hold (an unpaired surrogate, a line break in a line of text, a control character in XML), is refused.
    /// </summary>
    private static void CheckUpdatesToNewValues(InMemoryFileSystem fileSystem, string filePath, Dictionary<string, byte[]> files)
    {
        for (var index = 0; index < MaxCheckedValueChanges; index++)
        {
            // An update moves the locations that follow it in the same value, so each one starts from the original files
            // and their locations
            fileSystem.Restore(files);
            var dependencies = ScanFile(fileSystem, filePath, out _);
            var change = GetValueChanges(dependencies).ElementAtOrDefault(index);
            if (change is null)
                break;

            CheckUpdateToNewValue(fileSystem, filePath, dependencies, change);
        }

        fileSystem.Restore(files);
    }

    private static IEnumerable<ValueChange> GetValueChanges(IReadOnlyCollection<Dependency> dependencies)
    {
        var values = GetValues(dependencies);
        foreach (var dependency in dependencies)
        {
            foreach (var (location, value) in new[] { (dependency.NameLocation, dependency.Name), (dependency.VersionLocation, dependency.Version) })
            {
                // A git submodule commit is updated by running git, and an assembly version is normalized when it is written
                if (location is null || value is null || !location.IsUpdatable || location.GetType().Name is "GitSubmoduleVersionLocation" or "AssemblyVersionXmlLocation")
                    continue;

                var newValue = ChangeValue(value, values);
                if (newValue is not null)
                    yield return new ValueChange(dependency, location, value, newValue);
            }
        }
    }

    private static void CheckUpdateToNewValue(InMemoryFileSystem fileSystem, string filePath, IReadOnlyCollection<Dependency> dependencies, ValueChange change)
    {
        var (dependency, location, value, newValue) = change;

        // An update moves the locations that follow it in the same value, so they are identified by where they were scanned
        var keys = new Dictionary<Location, string>(ReferenceEqualityComparer.Instance);
        foreach (var other in dependencies)
        {
            foreach (var otherLocation in (ReadOnlySpan<Location?>)[other.NameLocation, other.VersionLocation])
            {
                if (otherLocation is not null)
                {
                    keys.TryAdd(otherLocation, GetLocationKey(otherLocation));
                }
            }
        }

        if (!TryUpdate(dependency, location, value, newValue))
            return;

        CheckUpdatedDependencies(fileSystem, filePath, dependencies, keys, change, newValue);

        // Without an expected value, the update expects the value written by the previous one
        var secondValue = GetValueToEscape(location, newValue) ?? ChangeValue(newValue, GetValues(dependencies));
        if (secondValue is null || !TryUpdate(dependency, location, oldValue: null, secondValue))
            return;

        CheckUpdatedDependencies(fileSystem, filePath, dependencies, keys, change, secondValue);

        // An update that expects a value the file does not hold, or that writes a value the file cannot hold, is refused
        CheckRefusedUpdate(fileSystem, dependency, location, "\uFFFF" + secondValue, value, isMismatch: true);
        CheckRefusedUpdate(fileSystem, dependency, location, oldValue: null, secondValue + "\uD800", isMismatch: false);
        switch (location.GetType().Name)
        {
            case "TextLocation":
                CheckRefusedUpdate(fileSystem, dependency, location, oldValue: null, secondValue + "\n", isMismatch: false);
                break;
            case "XmlLocation":
                CheckRefusedUpdate(fileSystem, dependency, location, oldValue: null, secondValue + "\u0001", isMismatch: false);
                break;
        }
    }

    /// <summary>Checks that the update is refused as documented, and leaves the file as it was.</summary>
    private static void CheckRefusedUpdate(InMemoryFileSystem fileSystem, Dependency dependency, Location location, string? oldValue, string newValue, bool isMismatch)
    {
        var content = fileSystem.GetContent(location.FilePath)!;
        var description = $"Updating {dependency} at {location} from {FuzzAssert.Format(oldValue)} to {FuzzAssert.Format(newValue)}";
        try
        {
            location.UpdateAsync(oldValue, newValue).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (isMismatch ? ex is DependencyScannerException : ex is ArgumentException or DependencyScannerException { InnerException: EncoderFallbackException })
        {
            FuzzAssert.True(content.AsSpan().SequenceEqual(fileSystem.GetContent(location.FilePath)), $"{description} was refused, but changed the file");
            return;
        }
        catch (Exception ex)
        {
            throw new FuzzAssertionException($"{description} was not refused as documented", ex);
        }

        FuzzAssert.Fail(isMismatch ? $"{description} succeeded, but the file does not hold the old value" : $"{description} succeeded, but the file cannot hold the new value");
    }

    /// <summary>Updates the location, and returns <see langword="false"/> when it refuses to write the value, as documented.</summary>
    private static bool TryUpdate(Dependency dependency, Location location, string? oldValue, string newValue)
    {
        try
        {
            location.UpdateAsync(oldValue, newValue).GetAwaiter().GetResult();
            return true;
        }
        catch (DependencyScannerException ex) when (IsUnsupportedUpdate(ex))
        {
            return false;
        }
        catch (Exception ex)
        {
            throw new FuzzAssertionException($"Updating {dependency} at {location} from {FuzzAssert.Format(oldValue)} to {FuzzAssert.Format(newValue)} failed", ex);
        }
    }

    /// <summary>Checks that the file holds the dependencies it held, with the new value in place of the old one at the updated location.</summary>
    private static void CheckUpdatedDependencies(InMemoryFileSystem fileSystem, string filePath, IReadOnlyCollection<Dependency> dependencies, Dictionary<Location, string> keys, ValueChange change, string newValue)
    {
        var updatedKey = keys[change.Location];
        var updatedFilePath = change.Location.FilePath;

        // The dependencies found in another file, such as a project that references the packages of a packages.config,
        // may depend on the updated value
        var originalDependencies = dependencies.Where(dependency => IsInFile(dependency, updatedFilePath)).ToArray();
        var expected = originalDependencies.Select(dependency => (dependency.Type, Name: Replace(dependency.Name, dependency.NameLocation), Version: Replace(dependency.Version, dependency.VersionLocation))).ToArray();
        var actual = ScanFile(fileSystem, filePath, out _)
            .Where(dependency => IsInFile(dependency, updatedFilePath))
            .Select(dependency => (dependency.Type, dependency.Name, dependency.Version))
            .ToArray();

        // A value that contains the updated text may follow it, such as the coordinates "group:name:version" of which
        // the version is also reported on its own
        var remaining = actual.Select(Describe).ToList();
        var unmatched = expected.Where(dependency => !remaining.Remove(Describe(dependency))).ToArray();
        var mismatch = unmatched.Any(dependency => !remaining.Remove(Describe((dependency.Type, dependency.Name?.Replace(change.Value, newValue, StringComparison.Ordinal), dependency.Version?.Replace(change.Value, newValue, StringComparison.Ordinal)))));
        if (mismatch || remaining.Count > 0)
        {
            var content = fileSystem.GetContent(updatedFilePath)!;
            FuzzAssert.Equal(Describe(expected), Describe(actual), $"Updating {change.Dependency} at {change.Location} from {FuzzAssert.Format(change.Value)} to {FuzzAssert.Format(newValue)} did not only change this value: {FuzzAssert.Format(Encoding.UTF8.GetString(content))}");
        }

        // Several dependencies can share the updated location, such as two scanners reporting the same value
        string? Replace(string? value, Location? location)
        {
            return location is not null && value == change.Value && keys.TryGetValue(location, out var key) && key == updatedKey ? newValue : value;
        }

        static bool IsInFile(Dependency dependency, string path)
        {
            return (dependency.NameLocation is null || dependency.NameLocation.FilePath == path)
                && (dependency.VersionLocation is null || dependency.VersionLocation.FilePath == path);
        }
    }

    /// <summary>Identifies a location by its file, its value and the part of the value it was scanned at.</summary>
    private static string GetLocationKey(Location location)
    {
        // Several locations can share a JSON or an XML value, such as the name and the version of "name#version"
        return $"{location.GetType().FullName} {location} {GetStartPosition(location)}";
    }

    private static HashSet<string> GetValues(IReadOnlyCollection<Dependency> dependencies)
    {
        return [.. dependencies.SelectMany(dependency => (string?[])[dependency.Name, dependency.Version]).OfType<string>()];
    }

    private static object? GetStartPosition(Location location) => location.GetType().GetProperty("StartPosition")?.GetValue(location);

    /// <summary>
    /// Changes the last digit of the value, or its last letter when it has none, to another character of the same class
    /// (a non-zero digit, a hexadecimal letter, another letter of the same case), so that the scanners still read it as a
    /// value of the same kind. Returns <see langword="null"/> when the value has no such character, or when the new value
    /// is the name or the version of a dependency of the file: two dependencies with the same name may be merged.
    /// </summary>
    private static string? ChangeValue(string value, HashSet<string> values)
    {
        var index = value.AsSpan().LastIndexOfAnyInRange('0', '9');
        if (index < 0)
        {
            // A letter of a value such as 'from: ""' may be part of a keyword rather than of a name
            index = value.AsSpan().LastIndexOfAny(AsciiLetters);
            if (index < 0 || value.AsSpan().ContainsAnyExcept(NameCharacters))
                return null;
        }

        var replacement = value[index] switch
        {
            '9' => '1',
            'f' => 'a',
            'z' => 'g',
            'F' => 'A',
            'Z' => 'G',
            var c => (char)(c + 1),
        };

        var result = string.Concat(value.AsSpan(0, index), [replacement], value.AsSpan(index + 1));
        return values.Contains(result) ? null : result;
    }

    /// <summary>
    /// Inserts the characters that a JSON string or an XML value escapes in a value that is a whole JSON string or XML
    /// value, which the scanners do not split. Returns <see langword="null"/> for any other location.
    /// </summary>
    private static string? GetValueToEscape(Location location, string value)
    {
        if (location.GetType().Name is not ("XmlLocation" or "JsonLocation") || GetStartPosition(location) is not < 0)
            return null;

        return string.Concat(value.AsSpan(0, 1), EscapedCharacters, value.AsSpan(1));
    }

    private static string Describe(IEnumerable<Dependency> dependencies)
    {
        return Describe(dependencies.Select(dependency => (dependency.Type, dependency.Name, dependency.Version)));
    }

    private static string Describe(IEnumerable<(DependencyType Type, string? Name, string? Version)> dependencies)
    {
        return string.Join('\n', dependencies.Select(Describe).Order(StringComparer.Ordinal));
    }

    private static string Describe((DependencyType Type, string? Name, string? Version) dependency)
    {
        return $"{dependency.Type} {FuzzAssert.Format(dependency.Name)} {FuzzAssert.Format(dependency.Version)}";
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

    private sealed record ValueChange(Dependency Dependency, Location Location, string Value, string NewValue);
}
