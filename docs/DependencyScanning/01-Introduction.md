---
title: Introduction
description: Scan a repository for the packages, images, actions and SDKs it depends on, and update their versions without breaking the files.
---

A repository declares its dependencies in many files: project files, `package.json`, Dockerfiles, workflows, lock files,
`go.mod`, `Cargo.toml`, and more. `Meziantou.Framework.DependencyScanning` reads all of them with one call, and returns
each dependency with its type, its name, its version and where they are written. It can then write a new version at that
place, keeping the rest of the file as it is.

`Meziantou.Framework.DependencyScanning.Tool` is a command-line tool built on the library: it lists the dependencies of a
folder, and updates them to the latest versions published on NuGet, npm, Docker registries and GitHub.

## What you can do with it

- **Scan a folder, a list of files or in-memory content.** See [Getting started](02-GettingStarted.md) and
  [Scanning files](03-ScanningFiles.md).
- **Restrict the scan** to some dependency types, files or folders. See [Configuring the scan](04-ConfiguringTheScan.md).
- **Read the dependencies of more than twenty ecosystems and formats**: .NET, npm, Docker, Kubernetes, Helm, GitHub
  Actions, Azure Pipelines, Python, Ruby, Rust, Go, Java, PHP, Swift, and more. See
  [Supported files](05-SupportedFiles/01-Overview.md).
- **Update a version or a name in place.** See [Updating dependencies](06-UpdatingDependencies.md).
- **Keep scanning when a file is broken.** See [Handling errors](07-HandlingErrors.md).
- **Read your own file formats** with a regular expression or a custom scanner. See
  [Custom scanners](08-CustomScanners.md).
- **List and update dependencies from the command line.** See [Command-line tool](09-CommandLineTool.md).

## Principles

- **Declared, not resolved.** The library reads what the files say. It does not restore packages, evaluate MSBuild, or
  ask a registry which versions exist. The command-line tool queries the registries.
- **Precise locations.** Each name and version comes with its location in the file, so an update changes exactly that
  text and keeps the formatting of the file.
- **Safe updates.** An update first checks that the file still holds the value found by the scan, and refuses to write
  otherwise. Values that cannot be updated safely, such as lock file entries or values built from variables, are
  reported with a location that is not updatable.
- **Parallel.** Files are scanned in parallel.

## Namespaces

The types are in the [Meziantou.Framework.DependencyScanning](cref:Meziantou.Framework.DependencyScanning) namespace, and the built-in scanners in
[Meziantou.Framework.DependencyScanning.Scanners](cref:Meziantou.Framework.DependencyScanning.Scanners):

| Type | Contains |
| --- | --- |
| `DependencyScanner` | `ScanDirectoryAsync`, `ScanFilesAsync` and `ScanFileAsync`, and the base class of the scanners |
| `ScannerOptions` | The scanners, the filters, the parallelism, the error callback and the file system |
| `Dependency` | A dependency: its `Type`, `Name`, `Version`, locations, `Metadata` and `Tags` |
| `DependencyType` | The kinds of dependencies, such as `NuGet`, `Npm` or `DockerImage` |
| `Location` | Where a value is written, and `UpdateAsync` to change it |
| `RegexScanner` | A scanner configured with file patterns and a regular expression |

## Next steps

- [Getting started](02-GettingStarted.md): scan your first folder.
