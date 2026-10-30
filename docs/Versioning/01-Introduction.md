---
title: Introduction
description: What SemanticVersion and SemanticVersionRange do.
---

`Meziantou.Framework.Versioning` implements [Semantic Versioning 2.0.0](https://semver.org/): versions such as
`1.4.0`, `2.0.0-rc.1` or `1.0.0+build.42`, made of a major, a minor and a patch number, optional prerelease labels and
optional build metadata.

## What you can do with it

- **Parse and write versions**, and create them from their parts. See [Getting started](02-GettingStarted.md).
- **Compare and sort versions** with the Semantic Versioning precedence, where `1.0.0-alpha` < `1.0.0-beta.2` <
  `1.0.0-beta.11` < `1.0.0`. See [Comparing versions](03-ComparingVersions.md).
- **Compute the next version**: the next patch, minor or major version. See
  [Comparing versions](03-ComparingVersions.md#compute-the-next-version).
- **Check a version against a range** written in the NuGet format (`[1.0.0, 2.0.0)`) or the npm format (`^1.2.3`).
  See [Version ranges](04-VersionRanges.md).

## SemanticVersion or System.Version

`System.Version` has up to four numbers and no labels: it cannot hold `2.0.0-rc.1`, and it orders `1.0` before `1.0.0`.
Use [SemanticVersion](cref:Meziantou.Framework.Versioning.SemanticVersion) for the versions of packages, APIs and releases that follow Semantic Versioning.

## Namespaces

[SemanticVersion](cref:Meziantou.Framework.Versioning.SemanticVersion), [SemanticVersionExtensions](cref:Meziantou.Framework.Versioning.SemanticVersionExtensions) and [SemanticVersionRange](cref:Meziantou.Framework.Versioning.SemanticVersionRange) are in the [Meziantou.Framework.Versioning](cref:Meziantou.Framework.Versioning)
namespace.
