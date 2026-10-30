---
title: Getting started
description: Install the package, scan a folder and read the dependencies it declares, or use the command-line tool.
---

## Install the package

Add the `Meziantou.Framework.DependencyScanning` package to a project:

```bash
dotnet add package Meziantou.Framework.DependencyScanning
```

The examples of this documentation omit the `using` directives: they need
`using Meziantou.Framework.DependencyScanning;`.

## Scan a folder

`DependencyScanner.ScanDirectoryAsync` scans a folder and its subfolders with all the built-in scanners:

<!-- ref:../../tests/Meziantou.Framework.DependencyScanning.Tests/DependencyScannerTests.cs;region=DependencyScanning_ScanDirectory;format=md-fenced-code-block;dedent=true -->
```csharp
var root = Directory.CreateTempSubdirectory().FullName; // The folder to scan
// A repository with a .NET project and an npm package
File.WriteAllText(Path.Combine(root, "App.csproj"), """
    <Project Sdk="Microsoft.NET.Sdk">
      <ItemGroup>
        <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
      </ItemGroup>
    </Project>
    """);
File.WriteAllText(Path.Combine(root, "package.json"), """
    { "devDependencies": { "typescript": "5.4.5" } }
    """);

var dependencies = await DependencyScanner.ScanDirectoryAsync(root, options: null, cancellationToken);

Assert.Equal(
    ["Npm typescript 5.4.5", "NuGet Newtonsoft.Json 13.0.1"],
    dependencies.Select(dependency => $"{dependency.Type} {dependency.Name} {dependency.Version}").Order(StringComparer.Ordinal));
```
<!-- endref -->

- `options: null` uses the default options: every built-in scanner, every subfolder, and 16 files scanned in parallel.
  See [Configuring the scan](04-ConfiguringTheScan.md).
- The result is a collection of `Dependency`. `Type` says what the dependency is, such as `DependencyType.NuGet`, and
  `Name` and `Version` are the values written in the file.
- The order of the result is not specified, as files are scanned in parallel. Sort the dependencies when the order
  matters.
- The project SDK, `Microsoft.NET.Sdk`, is not reported: it has no version in the file.

## Print the dependencies

`Dependency.ToString` returns the type, the name, the version and the location of the version, which is handy in logs:

<!-- ref:../../samples/DependencyScannerSample/ReadmeExamples.cs;region=DependencyScanner_ScanDirectory;format=md-fenced-code-block;dedent=true -->
```csharp
// Scan with default options
var dependencies = await DependencyScanner.ScanDirectoryAsync(
    "C:\\MyProject",
    options: null,
    cancellationToken);

foreach (var dependency in dependencies)
{
    Console.WriteLine($"{dependency.Type}: {dependency.Name}@{dependency.Version}");
}
```
<!-- endref -->

## Use the command-line tool

To list or update the dependencies of a repository without writing code, install the tool:

```bash
dotnet tool update Meziantou.Framework.DependencyScanning.Tool --global
```

```bash
Meziantou.Framework.DependencyScanning.Tool list --directory .
```

See [Command-line tool](09-CommandLineTool.md).

## Next steps

- [Scanning files](03-ScanningFiles.md): scan files and content, and what a `Dependency` holds.
- [Supported files](05-SupportedFiles/01-Overview.md): the files each scanner reads.
- [Updating dependencies](06-UpdatingDependencies.md): change a version in place.
