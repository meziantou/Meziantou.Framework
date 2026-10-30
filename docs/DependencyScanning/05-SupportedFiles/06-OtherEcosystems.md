---
title: Other ecosystems
description: The packages found in the manifests and lock files of Python, Ruby, Rust, Go, Java, PHP and Swift projects.
---

## Python

Reported as `PyPi`.

**Requirements files** (`PythonRequirementsDependencyScanner`): `*requirements*.txt` and `*constraints*.txt` files, their
pip-tools `.in` counterparts (such as `requirements.in`), and the `.txt` and `.in` files of a `requirements` folder. Only
exact pins (`==`) are reported; arbitrary equality (`===`) is not.

**Projects** (`PythonProjectDependencyScanner`): `pyproject.toml` and `Pipfile`.

- The exact versions of the dependency sections are reported: `[project]` `dependencies`,
  `[project.optional-dependencies]`, `[dependency-groups]`, `[build-system]` `requires`, `[tool.uv]`
  `dev-dependencies`, `constraint-dependencies` and `override-dependencies`, the Poetry dependency tables, and the
  Pipfile `[packages]` and `[dev-packages]`.
- Requirement strings follow PEP 508 (`name[extras] == version ; marker`). Poetry and Pipfile entries can be a string or
  an inline table with a `version` key.
- Only exact pins (`==`, or a bare version in Poetry and Pipfile) are reported; ranges and arbitrary equality (`===`) are
  not. Poetry's `python` entry is not a package and is ignored.
- A name or a version is not updatable when its TOML string contains an escape sequence or spans several lines.

**Lock files** (`PythonLockFileDependencyScanner`): `poetry.lock`, `uv.lock` and `Pipfile.lock`. The versions are not
updatable. The local projects are ignored: the `editable`, `virtual`, `path` or `directory` sources of `uv.lock`, and the
`directory` or `file` sources of `poetry.lock`.

## Ruby

Reported as `RubyGem`.

**`Gemfile` and `*.gemspec`** (`RubyGemDependencyScanner`):

- The `gem` and `add_*dependency` calls that start a statement are reported, so a call in a comment, a string, a
  heredoc or an `=begin` block is not.
- Names and requirements can be written as `"..."`, `'...'` or `%q<...>` literals, optionally followed by `.freeze`, and
  requirements can be passed as an array.
- A gem with several requirements is reported with the requirements joined by `, ` and a version location that is not
  updatable, as is a requirement that contains an escape sequence or an interpolation.

**`Gemfile.lock`** (`RubyGemLockFileDependencyScanner`): the top-level specs are reported, with versions that are not
updatable. A gem with native extensions has one entry per platform, such as `nokogiri (1.15.0-x86_64-linux)`: each entry
is reported with the version without the platform, and the platform in `Dependency.Metadata` under the `platform` key.

## Rust

Reported as `RustCrate`.

**`Cargo.toml`** (`CargoDependencyScanner`): the `[dependencies]`, `[dev-dependencies]`, `[build-dependencies]` and
`[workspace.dependencies]` sections, and their platform-specific `[target.'cfg(...)'.dependencies]` and
`[target.<triple>.dependencies]` forms.

- A crate declared over several entries, such as a `[dependencies.serde]` table or dotted keys (`serde.version` and
  `serde.features`), is reported once.
- Registry version requirements are updatable; git, path, workspace and other specifications are not.
- A renamed dependency (`json = { package = "serde_json" }`) is reported as the crate of its `package`, which is its name
  location, with the key in `Dependency.Metadata` under the `alias` key.
- A key is updatable only when it appears once, and a key or a value only when its text in the file is exactly its value
  (no escape sequences).

**`Cargo.lock`** (`CargoLockFileDependencyScanner`): the versions are not updatable, as their checksums would no longer
match. The packages without a `source`, which are the crates of the workspace and the path dependencies, are not
reported.

## Go

Reported as `GoModule`.

**`go.mod`** (`GoModuleDependencyScanner`): the `require`, `replace`, `go` and `toolchain` directives. The versions are
updatable.

- A `replace` directive is reported as its replacement module, with the replaced module path in `Dependency.Metadata`
  under the `replaces` key. A replacement with a local folder (`=> ../fork`) is not reported.
- The `go` and `toolchain` directives are reported as the `go` and `toolchain` modules, with versions such as `1.23.0`
  and `go1.23.0`.
- The `exclude`, `retract`, `tool` and `godebug` directives, and quoted module paths, are not reported.

**`go.sum`** (`GoSumDependencyScanner`): the checksum entries, with versions that are not updatable, as their hashes
would no longer match.

## Java

Reported as `JavaPackage`, by `JavaDependencyScanner`.

**Maven** (`pom.xml`): the dependencies, plugins, plugin dependencies, extensions and parent, including in
`dependencyManagement`, `pluginManagement` and profiles, but not the coordinates in a plugin `configuration` or in
`exclusions`.

- A dependency without a version, such as one managed by an imported BOM, is reported with a `null` version.
- A version that is exactly a reference to a property defined once in the `properties` of the same pom, and not
  redefined by a profile, such as `${junit.version}`, is reported with the value of the property, the property name in
  `Dependency.Metadata` under the `property` key, and the location of the property definition. That location is not
  updatable when the pom references the property more than once, as updating it would change the other references too.
  References from other poms, such as child modules, are not counted.
- Other property references are reported as written, with a location that is not updatable.

**Gradle** (`build.gradle`, `build.gradle.kts`):

- Dependencies written as `"group:artifact:version"` strings or with named arguments (`group: 'g', name: 'a',
  version: '1.0'` in Groovy, `group = "g", name = "a", version = "1.0"` in Kotlin) are reported.
- Plugins declared with a version (`id 'x' version '1.0'`, `id("x") version "1.0"`, `kotlin("jvm") version "1.0"`) are
  reported under the name of their plugin marker artifact, `<id>:<id>.gradle.plugin`, with the plugin id in
  `Dependency.Metadata` under the `pluginId` key.
- Comments are ignored, and a version that is not a string literal, such as `"$version"`, is not reported.

**Version catalogs** (`libs.versions.toml`): the `[libraries]` and `[plugins]`, with a literal version, a `version.ref`
or a `version = { ref = "..." }`. A catalog version shared by several libraries or plugins, or written with escape
sequences, is not updatable.

## PHP

Reported as `PhpPackage`. `ComposerDependencyScanner` reads the `require` and `require-dev` entries of `composer.json`,
and `ComposerLockFileDependencyScanner` the packages of `composer.lock`. Platform requirements, such as `php` or
`ext-json`, are ignored.

## Swift

Reported as `SwiftPackage`. `SwiftPackageDependencyScanner` reads `Package.swift` and `Package@swift-X.Y.swift`, and
`SwiftPackageLockFileDependencyScanner` reads `Package.resolved`.

- In a manifest, the version of a dependency is its whole requirement as written, such as `from: "1.0.0"`, so that an
  update can change the kind of requirement.
- A name or a requirement that is not made of string literals only, such as an interpolated or concatenated URL or
  `from: version`, is reported as written with a location that is not updatable.
- Escape sequences in string literals are resolved, and a string that contains one is not updatable.
