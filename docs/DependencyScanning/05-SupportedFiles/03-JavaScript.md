---
title: JavaScript
description: The npm packages found in package.json files and in the npm, pnpm, Yarn and Bun lock files.
---

## package.json

[NpmPackageJsonDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.NpmPackageJsonDependencyScanner) reads `package.json` files, and reports as [Npm](cref:Meziantou.Framework.DependencyScanning.DependencyType.Npm):

- the dependency sections (`dependencies`, `devDependencies`, ...);
- `packageManager`, under the name it is written with;
- npm `overrides`, including nested overrides and their `.` key, Yarn `resolutions`, and pnpm `pnpm.overrides`.

The version is the text of the file, such as `^1.2.0`. More rules:

- In overrides and resolutions, the package is the last one of the key, without its version selector:
  `"**/a/@scope/b@^1.0.0": "2.0.0"` is reported as `@scope/b` version `2.0.0`.
- An alias (`"name": "npm:package@1.0.0"`) is reported as the aliased package, with the alias name in
  [Dependency.Metadata](cref:Meziantou.Framework.DependencyScanning.Dependency.Metadata) under the `alias` key.
- Protocol and path specifiers (`workspace:`, `file:`, `link:`, git URLs, `github:`, tarball URLs, `owner/repo`
  shorthands, ...) and override references (`$name`) are reported with a version location that is not updatable.
- The version of `packageManager` is not updatable when a Corepack hash (`+sha512...`) follows it, as the hash would no
  longer match.

## Lock files

Each package manager has its own lock file scanner. They report the installed packages as
[Npm](cref:Meziantou.Framework.DependencyScanning.DependencyType.Npm), with versions that are not updatable, as their
integrity hashes would no longer match. The root project, the workspaces and the packages installed from a local folder
are not reported. A package installed under another name is reported as the installed package, with the name it is
installed under in [Dependency.Metadata](cref:Meziantou.Framework.DependencyScanning.Dependency.Metadata) under the
`alias` key.

| Scanner | Files | Notes |
| --- | --- | --- |
| [NpmLockFileDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.NpmLockFileDependencyScanner) | `package-lock.json`, `npm-shrinkwrap.json` | Lockfile versions 1, 2 and 3. Version 1 (npm 6 and earlier) is read from its `dependencies` tree. |
| [PnpmLockFileDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.PnpmLockFileDependencyScanner) | `pnpm-lock.yaml` | Lockfile versions 5, 6 and 9. A package is reported once per version, even when pnpm writes one entry per set of peer dependencies. |
| [YarnLockFileDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.YarnLockFileDependencyScanner) | `yarn.lock` | The Yarn 1 format and the YAML format of Yarn 2 and later. The patched copies of packages (`patch:`) are not reported. |
| [BunLockFileDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.BunLockFileDependencyScanner) | `bun.lock` | The text lock file of Bun 1.2 and later. The packages installed from a Git repository or a tarball URL have no version in the file and are not reported. The binary `bun.lockb` is not scanned. |

The packages installed from a Git repository or a tarball URL are reported with the version written in the lock file,
when it has one.

The command-line tool can regenerate `package-lock.json` after an update: see
[Lock files](../09-CommandLineTool.md#lock-files).
