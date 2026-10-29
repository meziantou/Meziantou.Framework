---
title: JavaScript
description: The npm packages found in package.json files and npm lock files.
---

## package.json

`NpmPackageJsonDependencyScanner` reads `package.json` files, and reports as `Npm`:

- the dependency sections (`dependencies`, `devDependencies`, ...);
- `packageManager`, under the name it is written with;
- npm `overrides`, including nested overrides and their `.` key, Yarn `resolutions`, and pnpm `pnpm.overrides`.

The version is the text of the file, such as `^1.2.0`. More rules:

- In overrides and resolutions, the package is the last one of the key, without its version selector:
  `"**/a/@scope/b@^1.0.0": "2.0.0"` is reported as `@scope/b` version `2.0.0`.
- An alias (`"name": "npm:package@1.0.0"`) is reported as the aliased package, with the alias name in
  `Dependency.Metadata` under the `alias` key.
- Protocol and path specifiers (`workspace:`, `file:`, `link:`, git URLs, `github:`, tarball URLs, `owner/repo`
  shorthands, ...) and override references (`$name`) are reported with a version location that is not updatable.
- The version of `packageManager` is not updatable when a Corepack hash (`+sha512...`) follows it, as the hash would no
  longer match.

## Lock files

`NpmLockFileDependencyScanner` reads `package-lock.json` and `npm-shrinkwrap.json` (lockfile versions 2 and 3). It
reports the packages installed in `node_modules`, with versions that are not updatable, as their integrity hashes would
no longer match. The root project, the workspace folders and the links are not reported.

Lockfile version 1 (npm 6 and earlier), `pnpm-lock.yaml`, `yarn.lock` and `bun.lock` are not scanned.

The command-line tool can regenerate `package-lock.json` after an update: see
[Lock files](../09-CommandLineTool.md#lock-files).
