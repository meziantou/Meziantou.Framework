---
title: Handling errors
description: Keep scanning when a file cannot be read or parsed, get notified of the failures, or stop at the first one.
---

## A broken file does not stop the scan

A repository often contains files that a scanner cannot read: a malformed JSON file, a template, a file locked by
another process. When a file cannot be opened or read, or a scanner throws while scanning it, the remaining scanners
are skipped for that file and the scan continues with the next file. The dependencies reported from the file before
the failure are kept.

## Get notified of the failures

Set `ScannerOptions.OnFileScanFailed` to receive the path of the file and the exception:

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

The callback can be called concurrently from several threads, as files are scanned in parallel.

## Stop at the first failure

An exception thrown by `OnFileScanFailed` stops the scan, and the scan method throws it. Use it to fail fast:

<!-- ref:../../samples/DependencyScannerSample/ReadmeExamples.cs;region=DependencyScanner_ErrorHandling;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ScannerOptions
{
    OnFileScanFailed = (filePath, exception) => throw new InvalidOperationException($"Cannot scan '{filePath}'", exception),
};
```
<!-- endref -->

## What stops a scan

| Cause | Result |
| --- | --- |
| A file cannot be opened, read or parsed | The file is skipped and `OnFileScanFailed` is called |
| `OnFileScanFailed` throws | The scan stops and the scan method throws the exception |
| The `onDependencyFound` callback throws | The scan stops and the scan method throws the exception |
| The cancellation token is canceled | The scan method throws an `OperationCanceledException` |
| The folder passed to `ScanDirectoryAsync` does not exist | The scan method throws |

`ScanFileAsync`, `ScanFilesAsync` and `ScanDirectoryAsync` behave the same way, and `ScanFileAsync` returns the
dependencies found before the failure. The `ScanFileAsync` overloads that scan content held in memory take no options,
so they skip a failure silently.
