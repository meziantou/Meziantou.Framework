---
title: Updating dependencies
description: Write a new version or name at the location found by the scan, and understand the checks that prevent a wrong write.
---

## Update a version

`Dependency.UpdateVersionAsync` writes a new version where the scan found the current one. Only that text changes: the
rest of the file keeps its formatting:

<!-- ref:../../tests/Meziantou.Framework.DependencyScanning.Tests/DependencyScannerTests.cs;region=DependencyScanning_UpdateVersion;format=md-fenced-code-block;dedent=true -->
```csharp
var root = Directory.CreateTempSubdirectory().FullName; // The folder to scan
var projectPath = Path.Combine(root, "App.csproj");
File.WriteAllText(projectPath, """
    <Project Sdk="Microsoft.NET.Sdk">
      <ItemGroup>
        <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
      </ItemGroup>
    </Project>
    """);

var dependencies = await DependencyScanner.ScanDirectoryAsync(root, options: null, cancellationToken);
var dependency = dependencies.Single(dependency => dependency.Name == "Newtonsoft.Json");

Assert.True(dependency.VersionLocation?.IsUpdatable);
await dependency.UpdateVersionAsync("13.0.3", cancellationToken);

// Only the version is changed: the rest of the file keeps its formatting
Assert.Equal("""
    <Project Sdk="Microsoft.NET.Sdk">
      <ItemGroup>
        <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
      </ItemGroup>
    </Project>
    """, File.ReadAllText(projectPath));

// The dependency keeps the values found by the scan
Assert.Equal("13.0.1", dependency.Version);
```
<!-- endref -->

- Check `VersionLocation?.IsUpdatable` first. `UpdateVersionAsync` throws an `InvalidOperationException` when the
  dependency has no version location or when the location is not updatable, such as an entry of a lock file. See
  [Updatable or not](05-SupportedFiles/01-Overview.md#rules-shared-by-the-scanners).
- `Dependency.Name` and `Dependency.Version` keep the values found by the scan. Scan again to read the file as it is now.
- The new value is written as text: pass a value that has the format of the file, such as `^2.0.0` to keep an npm
  range. A value that the file cannot hold, such as a line break in a line of text or a control character in an XML
  file, throws an `ArgumentException`.

To update many dependencies, scan once and update them one after the other:

<!-- ref:../../samples/DependencyScannerSample/ReadmeExamples.cs;region=DependencyScanner_UpdateVersions;format=md-fenced-code-block;dedent=true -->
```csharp
var dependencies = await DependencyScanner.ScanDirectoryAsync(
    "C:\\MyProject",
    options: null,
    cancellationToken);

// Update all NuGet packages to version 2.0.0
foreach (var dependency in dependencies.Where(d => d.Type == DependencyType.NuGet))
{
    if (dependency.VersionLocation?.IsUpdatable == true)
    {
        await dependency.UpdateVersionAsync("2.0.0", cancellationToken);
    }
}
```
<!-- endref -->

The library writes the value it is given: choosing the new version, for instance from a registry, is up to you. The
[command-line tool](09-CommandLineTool.md) does it for NuGet, npm, Docker, GitHub Actions and the .NET SDK.

## Update a name

`Dependency.UpdateNameAsync` replaces the name, for instance to move to a renamed package or to another registry. It
follows the same rules as `UpdateVersionAsync`, with `NameLocation`.

The name and the version can be updated in any order, even when they share a line or a value, such as `FROM node:18`:
each location follows the changes made by the other one.

## An update never writes blindly

Before it writes, an update checks that the file still holds the expected value at the location, and throws a
`DependencyScannerException` without changing the file otherwise:

<!-- ref:../../tests/Meziantou.Framework.DependencyScanning.Tests/DependencyScannerTests.cs;region=DependencyScanning_UpdateModifiedFile;format=md-fenced-code-block;dedent=true -->
```csharp
var root = Directory.CreateTempSubdirectory().FullName; // The folder to scan
var dockerfilePath = Path.Combine(root, "Dockerfile");
File.WriteAllText(dockerfilePath, "FROM node:20\n");

var dependencies = await DependencyScanner.ScanDirectoryAsync(root, options: null, cancellationToken);
var node = Assert.Single(dependencies);

// The file changes after the scan
File.WriteAllText(dockerfilePath, "FROM node:22\n");

// The update checks that the file still holds the scanned value, and does not write anything
await Assert.ThrowsAsync<DependencyScannerException>(() => node.UpdateVersionAsync("24", cancellationToken));
Assert.Equal("FROM node:22\n", File.ReadAllText(dockerfilePath));
```
<!-- endref -->

The expected value is the value the location holds: the value found by the scan, then the value written by the last
update through this location. So a dependency can be updated several times in a row. `Location.UpdateAsync(oldValue,
newValue)` takes the expected value explicitly, for instance when another process may have changed the file since the
scan.

When a file changed after the scan, scan it again and update the new dependencies.

## Encoding

The file is rewritten in place. The encoding of the file is kept: a byte order mark stays, and an XML file is written in
the encoding of its declaration. A text file that is not valid UTF-8 is not modified: the update throws.
