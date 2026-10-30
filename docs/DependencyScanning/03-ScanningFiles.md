---
title: Scanning files
description: Scan a folder, a list of files or in-memory content, receive the dependencies as they are found, and read what a dependency holds.
---

## Choose what to scan

`DependencyScanner` has three static methods:

| Method | Scans |
| --- | --- |
| `ScanDirectoryAsync(path, options)` | Every file of a folder, and of its subfolders unless `RecurseSubdirectories` is `false` |
| `ScanFilesAsync(rootDirectory, filePaths, options)` | The given files |
| `ScanFileAsync(rootDirectory, filePath, options)` | One file |
| `ScanFileAsync(rootDirectory, filePath, content)` | Content held in memory, as if it were the given file |

Each scanner decides from the path of a file whether it reads it: `package.json` is read by the npm scanner, a `.csproj`
file by the MSBuild scanner, and a file that no scanner recognizes is skipped without being opened.

## Scan specific files

When you already know the files, for instance the files changed by a commit, scan them directly:

<!-- ref:../../samples/DependencyScannerSample/ReadmeExamples.cs;region=DependencyScanner_ScanFiles;format=md-fenced-code-block;dedent=true -->
```csharp
// Scan a single file
var dependencies = await DependencyScanner.ScanFileAsync(
    rootDirectory: "C:\\MyProject",
    filePath: "C:\\MyProject\\package.json",
    options: null,
    cancellationToken);

// Scan multiple specific files
var filePaths = new[]
{
    "C:\\MyProject\\package.json",
    "C:\\MyProject\\MyProject.csproj",
};

var allDependencies = await DependencyScanner.ScanFilesAsync(
    rootDirectory: "C:\\MyProject",
    filePaths,
    options: null,
    cancellationToken);
```
<!-- endref -->

The root directory matters: some scanners only read files at a given place relative to it, such as the workflows of
`.github/workflows`. The root directory and the file paths can be relative or contain `.` and `..` segments: they are
made absolute before the scanners check them.

## Scan content held in memory

To scan content that is not on the disk, such as a file read from a Git object or an archive, pass its bytes and the path
it would have. The path chooses the scanners:

<!-- ref:../../tests/Meziantou.Framework.DependencyScanning.Tests/DependencyScannerTests.cs;region=DependencyScanning_ScanContent;format=md-fenced-code-block;dedent=true -->
```csharp
var content = Encoding.UTF8.GetBytes("""
    FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
    FROM build AS publish
    FROM nginx:1.27@sha256:0000000000000000000000000000000000000000000000000000000000000000
    """);

// The file path chooses the scanners: this content is scanned as a Dockerfile
var dependencies = await DependencyScanner.ScanFileAsync("/repo", "/repo/Dockerfile", content, cancellationToken);

// "FROM build" is a build stage, not an image
Assert.Equal(["mcr.microsoft.com/dotnet/sdk", "nginx"], dependencies.Select(dependency => dependency.Name).Order(StringComparer.Ordinal));

var sdk = dependencies.Single(dependency => dependency.Name == "mcr.microsoft.com/dotnet/sdk");
Assert.Equal(DependencyType.DockerImage, sdk.Type);
Assert.Equal("10.0", sdk.Version);

// An image pinned by digest has the digest as its version, and the tag in its metadata
var nginx = dependencies.Single(dependency => dependency.Name == "nginx");
Assert.Equal("sha256:0000000000000000000000000000000000000000000000000000000000000000", nginx.Version);
Assert.Equal("1.27", nginx.Metadata["tag"]);

// There is no file to write
Assert.False(sdk.VersionLocation?.IsUpdatable);
```
<!-- endref -->

- The locations of these dependencies are not updatable, as there is no file to write.
- This overload takes no options. Another overload takes the list of scanners to use; `null` uses the built-in ones.
- A scanner failure is skipped silently. See [Handling errors](07-HandlingErrors.md).

## Receive the dependencies as they are found

The overloads of `ScanDirectoryAsync` and `ScanFilesAsync` that take an `onDependencyFound` callback return nothing:
they call the callback for each dependency, as soon as it is found. Files are scanned in parallel, so the callback can be
called concurrently from several threads and must be thread-safe. Use a concurrent collection, a lock, or
`DegreeOfParallelism = 1`:

<!-- ref:../../samples/DependencyScannerSample/ReadmeExamples.cs;region=DependencyScanner_StreamDependencies;format=md-fenced-code-block;dedent=true -->
```csharp
var dependencies = new ConcurrentQueue<Dependency>();
await DependencyScanner.ScanDirectoryAsync(
    "C:\\MyProject",
    options: null,
    onDependencyFound: dependency =>
    {
        // Called concurrently
        dependencies.Enqueue(dependency);
    },
    cancellationToken);
```
<!-- endref -->

An exception thrown by the callback stops the scan, and the scan method throws it.

## Read a dependency

| Member | Contains |
| --- | --- |
| `Type` | The kind of dependency, such as `DependencyType.NuGet` or `DependencyType.DockerImage` |
| `Name` | The name, as written in the file. `null` when the file declares no name, such as the SDK version of `global.json` |
| `Version` | The version, as written in the file. `null` when the file declares no version |
| `NameLocation`, `VersionLocation` | Where the name and the version are written, or `null` |
| `Metadata` | More values found by the scanner, such as the tag of an image pinned by digest. Each scanner documents its keys: see [Supported files](05-SupportedFiles/01-Overview.md) |
| `Tags` | The full name of the type of the scanner that found the dependency, and the tags the scanner added |

A `Location` has the `FilePath` of the file, and `IsUpdatable` says whether `UpdateAsync` can change the value. The
locations in text and XML files also implement `ILocationLineInfo`, which gives the `LineNumber` and the `LinePosition`
of the value; the locations in JSON files do not.

The version is the text of the file, not a normalized version: `^1.2.0` for an npm range, `from: "1.0.0"` for a Swift
requirement, `20-alpine` for an image tag. Compare versions with the rules of each ecosystem.

## Sort the dependencies

`OrderByVersionLocation` and `OrderByVersionLocationDescending` sort dependencies by file, line and position of their
version, or of their name when they have no version. Use them to report the dependencies in the order of the files.
