---
title: Command-line tool
description: List the dependencies of a repository, find the ones that can be upgraded, and update them to the latest versions.
---

`Meziantou.Framework.DependencyScanning.Tool` scans a folder with the library, then asks the registries for newer
versions and writes them in the files.

## Install the tool

Install or update it as a global tool:

```bash
dotnet tool update Meziantou.Framework.DependencyScanning.Tool --global
```

The command has the name of the package. To pin the version in a repository, install it as a local tool instead
(`dotnet new tool-manifest`, then `dotnet tool install Meziantou.Framework.DependencyScanning.Tool`) and run it with
`dotnet tool run Meziantou.Framework.DependencyScanning.Tool`.

## List the dependencies

`list` scans a folder and writes the dependencies it found:

```bash
Meziantou.Framework.DependencyScanning.Tool list --directory .
```

- Without `--directory`, the current folder is scanned.
- The text output starts with the folders searched, then the number of dependencies and one line per dependency, sorted
  by type, name and version.
- `--format Json` writes a JSON array instead. Each item has the `type`, `name`, `version`, `filePath` and
  `isUpdatable` of a dependency, so scripts can process the result.

## Find the dependencies to upgrade

`--upgradable` only lists the dependencies that `update` would change:

```bash
Meziantou.Framework.DependencyScanning.Tool list --directory . --upgradable --format Json
```

It queries the registries the same way as `update`, including the [minimum age](#minimum-age), and writes nothing to the
files. A dependency whose registry cannot be reached is reported on the standard error and left out of the list.

## Update the dependencies

`update` scans the folder, then writes the newest suitable version of each dependency it can update:

```bash
Meziantou.Framework.DependencyScanning.Tool update --directory .
```

The output lists the dependencies found, then one `Updated <dependency> -> <version>` line per update, and the number of
dependencies updated. The exit code is `1` when a dependency could not be checked or updated, for instance because a
registry did not answer; the other dependencies are still updated.

Commit or stash your changes before running it, so that you can review the changes and revert them.

## What can be updated

`update` and `--upgradable` handle these dependency types. The other types are listed, but never updated:

| Type | Source of the versions |
| --- | --- |
| [NuGet](cref:Meziantou.Framework.DependencyScanning.DependencyType.NuGet) | The NuGet sources of the `nuget.config` files of the repository, or nuget.org when there is none |
| [Npm](cref:Meziantou.Framework.DependencyScanning.DependencyType.Npm) | The registry of the `.npmrc` files of the repository, or `https://registry.npmjs.org/` |
| [DockerImage](cref:Meziantou.Framework.DependencyScanning.DependencyType.DockerImage) | The registry of the image |
| [GitHubActions](cref:Meziantou.Framework.DependencyScanning.DependencyType.GitHubActions) | The tags of the repository of the action, on GitHub |
| [DotNetSdk](cref:Meziantou.Framework.DependencyScanning.DependencyType.DotNetSdk) | The latest SDK of each .NET release channel |

The new version must be compatible with the current one:

- Only a greater version is used, and a dependency whose version is not a valid version for its ecosystem, such as an
  npm range like `>=1.0 <2.0` or a Git branch, is skipped. The update can move to a new major version.
- For NuGet, npm, GitHub Actions and the .NET SDK, a stable version is never updated to a prerelease. A prerelease is
  updated to a stable version, or to a newer prerelease of the same version number.
- An npm version keeps its prefix, such as `^` or `~`.
- A Docker tag keeps its shape: `8.0` is only updated to tags with two numbers, such as `8.1`, and a suffix such as
  `-alpine` must be the same.
- A GitHub Actions version keeps its `v` prefix and its number of components: `v4` is updated to `v5`, not to `v5.0.1`.

## Package sources

- **NuGet**: the tool reads the `nuget.config` files of the folder of the project and of its parent folders, whatever
  their casing, and ignores the user-wide and machine-wide files, so that the result does not depend on the machine.
  Package source mapping is applied: a package is only searched on the sources mapped to it. The credentials declared in
  the configuration files are used, so authenticated feeds work.
- **npm**: the tool reads the `.npmrc` files of the folder of the `package.json` file and of its parent folders. A
  scoped registry (`@scope:registry=...`) is used for the packages of the scope. A registry declared next to the
  dependency, such as in a plugin marketplace, wins.
- **GitHub Actions**: anonymous requests to the GitHub API are rate-limited, and a repository with a few dozen actions
  can reach the limit in one run. Set the `GITHUB_TOKEN` or `GH_TOKEN` environment variable to send authenticated
  requests, which have a higher limit. When the API refuses a request, the tool says so on the standard error.

## Minimum age

`--minimum-age` is the minimum number of days since a version was published before the tool uses it. It is `7` by
default, which gives time to find and remove a broken or malicious release before it reaches your repository:

```bash
Meziantou.Framework.DependencyScanning.Tool update --directory . --minimum-age 14
```

- `0` or a negative value disables the filter.
- A version whose publication date is unknown is skipped, except for Docker images, as registries do not expose
  publication dates.

## Choose the files and the types

`--dependency-type` restricts the types, as a list separated by spaces, `,` or `;`, in any case:

```bash
Meziantou.Framework.DependencyScanning.Tool update --directory . --dependency-type NuGet,DotNetSdk
```

`--files` replaces the files to scan with glob patterns, relative to the root folder. A pattern that starts with `!`
excludes paths:

```bash
Meziantou.Framework.DependencyScanning.Tool list --directory . --files "src/**/*.csproj" --files "!src/legacy/**/*"
```

Without `--files`, the tool scans every file, including those in folders whose name starts with `.` such as
`.github` and `.config`, except the content of `node_modules` and `.playwright/package` folders.

## Lock files

`--update-lock-files` regenerates the lock files after the updates:

- for NuGet, it runs `dotnet restore --no-cache` on the projects that have a `packages.lock.json` file;
- for npm, it runs `npm install --no-audit` in the folder of the `package-lock.json` file of each updated `package.json`.

```bash
Meziantou.Framework.DependencyScanning.Tool update --directory . --update-lock-files
```

Only use this option on repositories you trust: `dotnet restore` evaluates the MSBuild targets of the projects, and
`npm install` runs the install scripts of the dependencies. Both execute code from the repository.

## Options

| Option | Commands | Description |
| --- | --- | --- |
| `--directory <directory>` | `list`, `update` | The root folder. Defaults to the current folder |
| `--files <files>` | `list`, `update` | Glob patterns of the files to scan |
| `--dependency-type <types>` | `list`, `update` | The dependency types to include |
| `--minimum-age <days>` | `list`, `update` | The minimum age of a version, in days. Defaults to 7 |
| `--upgradable` | `list` | Only list the dependencies that can be upgraded |
| `--format <Text\|Json>` | `list` | The output format |
| `--update-lock-files` | `update` | Regenerate the NuGet and npm lock files |

`Meziantou.Framework.DependencyScanning.Tool --help` and `--help` on each command show the same options.
