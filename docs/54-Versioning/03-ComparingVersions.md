---
title: Comparing versions
description: Compare and sort versions with the Semantic Versioning precedence, and compute the next patch, minor or major version.
---

## Compare and sort versions

`SemanticVersion` implements `IComparable<SemanticVersion>`, `IEquatable<SemanticVersion>` and the comparison
operators:

<!-- ref:../../tests/Meziantou.Framework.Versioning.Tests/SemanticVersionTests.cs;region=SemanticVersion_Compare;format=md-fenced-code-block;dedent=true -->
```csharp
var versions = new[] { "1.0.0", "1.0.0-rc.1", "1.0.0-beta.11", "1.0.0-beta.2", "1.0.0-alpha", "0.9.0" }
    .Select(SemanticVersion.Parse)
    .Order()
    .Select(version => version.ToString());

Assert.Equal(["0.9.0", "1.0.0-alpha", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0"], versions);

// Build metadata does not take part in comparisons and equality
Assert.True(SemanticVersion.Parse("1.0.0+linux") == SemanticVersion.Parse("1.0.0+windows"));
```
<!-- endref -->

The order is the precedence of Semantic Versioning 2.0.0:

1. The major, minor and patch numbers are compared as numbers.
2. A prerelease version is lower than the release with the same numbers: `1.0.0-rc.1` < `1.0.0`.
3. Prerelease labels are compared one identifier at a time. Numeric identifiers are compared as numbers, so `beta.2` <
   `beta.11`; other identifiers are compared as ASCII text, so they are case-sensitive; a numeric identifier is lower
   than a text one. When all the identifiers are equal, the version with fewer identifiers is lower: `1.0.0-alpha` <
   `1.0.0-alpha.1`.
4. Build metadata is ignored: `1.0.0+linux` and `1.0.0+windows` are equal, so a `HashSet<SemanticVersion>` keeps only
   one of them. Compare `ToString()` when the metadata matters.

`null` is lower than any version.

## Compute the next version

The `SemanticVersionExtensions` methods return the next stable version:

<!-- ref:../../tests/Meziantou.Framework.Versioning.Tests/SemanticVersionTests.cs;region=SemanticVersion_NextVersions;format=md-fenced-code-block;dedent=true -->
```csharp
var stable = SemanticVersion.Parse("1.2.3");
Assert.Equal("1.2.4", stable.NextPatchVersion().ToString());
Assert.Equal("1.3.0", stable.NextMinorVersion().ToString());
Assert.Equal("2.0.0", stable.NextMajorVersion().ToString());

// A prerelease is followed by the smallest stable version that is greater
var beta = SemanticVersion.Parse("1.2.3-beta");
Assert.Equal("1.2.3", beta.NextPatchVersion().ToString());
Assert.Equal("1.3.0", beta.NextMinorVersion().ToString());
Assert.Equal("2.0.0", beta.NextMajorVersion().ToString());

var rc = SemanticVersion.Parse("2.0.0-rc.1");
Assert.Equal("2.0.0", rc.NextPatchVersion().ToString());
Assert.Equal("2.0.0", rc.NextMinorVersion().ToString());
Assert.Equal("2.0.0", rc.NextMajorVersion().ToString());
```
<!-- endref -->

- `NextPatchVersion` increments the patch number of a stable version, and removes the labels of a prerelease:
  `1.2.3-beta` is followed by `1.2.3`.
- `NextMinorVersion` returns the smallest stable version that is greater and whose patch number is 0.
- `NextMajorVersion` returns the smallest stable version that is greater and whose minor and patch numbers are 0.
- The result never has prerelease labels or metadata. A number that would exceed `int.MaxValue` throws an
  `OverflowException`.
