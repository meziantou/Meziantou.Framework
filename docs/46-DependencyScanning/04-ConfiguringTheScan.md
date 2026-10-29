---
title: Configuring the scan
description: Choose the dependency types, the files, the folders, the scanners and the parallelism of a scan, or read files from another file system.
---

Pass a `ScannerOptions` to the scan methods. `null` uses the default options.

<!-- ref:../../samples/DependencyScannerSample/ReadmeExamples.cs;region=DependencyScanner_CustomOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ScannerOptions
{
    // Number of parallel scanning tasks (default: 16)
    DegreeOfParallelism = 8,

    // Recurse into subdirectories (default: true)
    RecurseSubdirectories = true,

    // Filter files to scan (FileSystemEntry is in System.IO.Enumeration)
    ShouldScanFilePredicate = (ref FileSystemEntry entry) => !entry.FileName.StartsWith('.'),

    // Filter directories to recurse into
    ShouldRecursePredicate = (ref FileSystemEntry entry) => entry.FileName is not ("node_modules" or "bin"),

    // Called when a file cannot be read or parsed. The file is skipped and the scan continues.
    OnFileScanFailed = (filePath, exception) => Console.Error.WriteLine($"Cannot scan {filePath}: {exception.Message}"),
};

var dependencies = await DependencyScanner.ScanDirectoryAsync(
    @"C:\MyProject",
    options,
    cancellationToken);
```
<!-- endref -->

The options are frozen when a scan uses them: create new options to change the scanners or the dependency types.

## Choose the dependency types

`IncludedDependencyTypes` keeps only some types, and `ExcludedDependencyTypes` removes some:

<!-- ref:../../samples/DependencyScannerSample/ReadmeExamples.cs;region=DependencyScanner_FilterByDependencyType;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ScannerOptions
{
    // Only scan for specific dependency types
    IncludedDependencyTypes = [DependencyType.NuGet, DependencyType.Npm],
};

// Or exclude specific types
var options2 = new ScannerOptions
{
    ExcludedDependencyTypes = [DependencyType.DockerImage],
};
```
<!-- endref -->

The filter makes the scan faster too: a scanner that cannot report any of the requested types does not run.

## Choose the files and the folders

- `ShouldScanFilePredicate` decides whether a file is scanned, and `ShouldRecursePredicate` whether a folder is entered.
  Both receive a `FileSystemEntry` (from `System.IO.Enumeration`) by reference, as in the example above.
- `RecurseSubdirectories` set to `false` only scans the files of the folder itself.
- Symbolic links and other reparse points to folders are never followed, so a link cannot make the scan loop or leave
  the root folder. A symbolic link to a file is scanned only when its target is inside the root folder, because updating
  its dependencies would write to the target.

The predicates run for every file and folder, before any scanner: skipping large folders such as `node_modules`, `bin`
or `.git` saves the time of listing them.

## Parallelism

`DegreeOfParallelism` is the number of files scanned at the same time. It is 16 by default; `-1` uses the number of
processors, and `0` or less than `-1` throws an `ArgumentOutOfRangeException`. Set it to `1` to scan one file at a time,
for instance when the `onDependencyFound` callback is not thread-safe.

## Choose the scanners

`Scanners` holds the scanners to run. It contains all the built-in scanners by default. Replace it to run fewer
scanners, or add your own:

<!-- ref:../../samples/DependencyScannerSample/ReadmeExamples.cs;region=DependencyScanning_CustomScannerUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ScannerOptions();

// Keep the built-in scanners, and add the custom one
options.Scanners = options.Scanners.Add(new VersionsFileScanner());

var dependencies = await DependencyScanner.ScanDirectoryAsync("C:\\MyProject", options, cancellationToken);
```
<!-- endref -->

Each lock file format has its own scanner, separate from the scanner of the manifest it is generated from. Leave the
lock file scanners out to get the declared dependencies only: `CargoLockFileDependencyScanner`,
`ComposerLockFileDependencyScanner`, `GitHubActionsLockFileDependencyScanner`, `GoSumDependencyScanner`,
`HelmChartLockFileDependencyScanner`, `NpmLockFileDependencyScanner`, `NuGetLockFileDependencyScanner`,
`ProjectAssetsDependencyScanner`, `PythonLockFileDependencyScanner`, `RubyGemLockFileDependencyScanner` and
`SwiftPackageLockFileDependencyScanner`. See [Supported files](05-SupportedFiles/01-Overview.md) for the files of each
scanner.

## Use another file system

`FileSystem` reads and writes the files through an `IFileSystem`, for instance to scan an in-memory copy of a
repository:

- `GetFiles(path, "*", searchOption)` lists the files of a folder. It should throw a `DirectoryNotFoundException` when
  the folder does not exist.
- `OpenRead` can return a stream that cannot seek: it is read into memory.
- `OpenReadWrite` must return a seekable stream that supports `SetLength`, as an update rewrites the file in place.

`ShouldScanFilePredicate` and `ShouldRecursePredicate` take a `FileSystemEntry`, which only an enumeration of the
physical disk provides: a directory scan with a custom file system throws a `NotSupportedException` when they are set.
Filter the files in `GetFiles` instead.
