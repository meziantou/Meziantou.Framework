---
title: Limitations
description: What the library and the tool do not do, and how to work around it.
---

- **Declared dependencies only.** The library reads the files as text, XML, JSON, YAML or TOML: it does not evaluate
  MSBuild, run package managers, or resolve transitive dependencies. Scan the lock files to see the resolved versions.
  See [Lock files](05-SupportedFiles/01-Overview.md#lock-files).
- **Variables are not resolved.** A value built from a variable or a property is reported as written, with a location
  that is not updatable. The exceptions are the Dockerfile `ARG` defaults and the Maven properties described in
  [Containers](05-SupportedFiles/04-Containers.md#dockerfiles) and
  [Other ecosystems](05-SupportedFiles/06-OtherEcosystems.md#java).
- **Some formats are not scanned**, such as `pnpm-lock.yaml`, `yarn.lock`, `bun.lock` and npm lockfile version 1. See
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
