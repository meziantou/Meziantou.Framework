---
title: Limitations
description: What the library and the tool do not do, and how to work around it.
---

- **Declared dependencies only.** The library reads the files as text, XML, JSON, YAML or TOML: it does not evaluate
  MSBuild, run package managers, or resolve transitive dependencies. Scan the lock files to see the resolved versions.
  See [Lock files](05-SupportedFiles/01-Overview.md#lock-files).
- **Only simple properties and variables of the same file are resolved.** MSBuild properties, `#:property` directives,
  Dockerfile `ARG` defaults, Maven properties, Gradle variables and version catalog references are resolved when the
  file defines them once with a literal value. A property defined in another file (`Directory.Build.props`,
  `gradle.properties`, a parent pom), with a condition, from other properties, or by the environment, such as a compose
  file `${TAG}` or a GitHub Actions expression, is not resolved: the value is reported as written, with a `null`
  resolved version and a location that is not updatable. See
  [Properties and variables](05-SupportedFiles/01-Overview.md#properties-and-variables).
- **Some formats are not scanned**, such as the binary Bun lock file `bun.lockb`. See
  [JavaScript](05-SupportedFiles/03-JavaScript.md#lock-files). Use a [custom scanner](08-CustomScanners.md) for other
  formats.
- **The library does not choose versions.** It writes the value it is given. The command-line tool chooses versions
  for NuGet, npm, Docker images, GitHub Actions and the .NET SDK only. See
  [What can be updated](09-CommandLineTool.md#what-can-be-updated).
- **Lock files are not updated by the library.** Their entries are not updatable. The command-line tool can regenerate
  the NuGet and npm lock files. See [Lock files](09-CommandLineTool.md#lock-files).
- **The command-line tool only uses the built-in scanners.** To scan your own formats, call the library from your own
  program.
- **Updates are not transactional.** Each update writes its file on its own. When an update fails, the previous ones
  stay: use version control to review and revert them.
