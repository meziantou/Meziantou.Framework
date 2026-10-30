---
title: Getting started
description: Install the package, parse a version, read its parts, and create versions.
---

## Install the package

Add the `Meziantou.Framework.Versioning` package to your project:

```bash
dotnet add package Meziantou.Framework.Versioning
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.Versioning;`.
They check their results with assertions such as `Assert.Equal`, which show what each call returns.

## Parse a version

<!-- ref:../../tests/Meziantou.Framework.Versioning.Tests/SemanticVersionTests.cs;region=SemanticVersion_Example;format=md-fenced-code-block;dedent=true -->
```csharp
// Parse a semantic version string
var version = SemanticVersion.Parse("1.2.3-alpha.1+build");
Assert.Equal(1, version.Major);
Assert.Equal(2, version.Minor);
Assert.Equal(3, version.Patch);
Assert.True(version.IsPrerelease);
Assert.Equal(["alpha", "1"], version.PrereleaseLabels);
Assert.True(version.HasMetadata);
Assert.Equal(["build"], version.Metadata);

// Create and compare versions (prerelease versions have lower precedence)
var v1 = new SemanticVersion(1, 0, 0, "alpha");
var v2 = new SemanticVersion(1, 0, 0);
Assert.True(v1 < v2);

// Get the next version
Assert.Equal("1.0.1", v2.NextPatchVersion().ToString());
Assert.Equal("1.1.0", v2.NextMinorVersion().ToString());
Assert.Equal("2.0.0", v2.NextMajorVersion().ToString());
```
<!-- endref -->

- [Major](cref:Meziantou.Framework.Versioning.SemanticVersion.Major), [Minor](cref:Meziantou.Framework.Versioning.SemanticVersion.Minor) and [Patch](cref:Meziantou.Framework.Versioning.SemanticVersion.Patch) are the three numbers.
- [PrereleaseLabels](cref:Meziantou.Framework.Versioning.SemanticVersion.PrereleaseLabels) holds the dot-separated identifiers after `-`, and [IsPrerelease](cref:Meziantou.Framework.Versioning.SemanticVersion.IsPrerelease) tells whether there is any.
- [Metadata](cref:Meziantou.Framework.Versioning.SemanticVersion.Metadata) holds the dot-separated identifiers after `+`, and [HasMetadata](cref:Meziantou.Framework.Versioning.SemanticVersion.HasMetadata) tells whether there is any.

## Validation and formatting

[Parse](cref:Meziantou.Framework.Versioning.SemanticVersion.Parse) and [TryParse](cref:Meziantou.Framework.Versioning.SemanticVersion.TryParse) accept the Semantic Versioning 2.0.0 syntax, and nothing else:

<!-- ref:../../tests/Meziantou.Framework.Versioning.Tests/SemanticVersionTests.cs;region=SemanticVersion_ParseAndFormat;format=md-fenced-code-block;dedent=true -->
```csharp
// A leading "v" is accepted, and not written back
var version = SemanticVersion.Parse("v2.1.0-rc.1+build.42");
Assert.Equal("2.1.0-rc.1+build.42", version.ToString());

// Versions that do not follow Semantic Versioning 2.0.0 are rejected
Assert.False(SemanticVersion.TryParse("1.0", out _));       // Three numbers are required
Assert.False(SemanticVersion.TryParse("1.02.0", out _));    // No leading zero
Assert.False(SemanticVersion.TryParse("1.0.0-beta.01", out _));
Assert.Throws<ArgumentException>(() => SemanticVersion.Parse("1.0.0.0"));

// Create a version from its parts
var prerelease = new SemanticVersion(1, 4, 0, prereleaseLabel: "beta.2", metadata: "sha.5114f85");
Assert.Equal("1.4.0-beta.2+sha.5114f85", prerelease.ToString());
Assert.Equal(["beta", "2"], prerelease.PrereleaseLabels);
Assert.Equal(["sha", "5114f85"], prerelease.Metadata);
```
<!-- endref -->

- A `v` or `V` prefix is accepted, as in Git tags such as `v1.2.3`. `ToString` never writes it.
- The three numbers are required, and numbers have no leading zero. Prerelease and metadata identifiers are made of
  ASCII letters, digits and `-`, and are not empty.
- [Parse](cref:Meziantou.Framework.Versioning.SemanticVersion.Parse) throws an `ArgumentException` for an invalid version; [TryParse](cref:Meziantou.Framework.Versioning.SemanticVersion.TryParse) returns `false`. Both have a
  `ReadOnlySpan<char>` overload, and [SemanticVersion](cref:Meziantou.Framework.Versioning.SemanticVersion) implements `IParsable<SemanticVersion>` and
  `ISpanParsable<SemanticVersion>`.
- The constructors validate the labels too and throw an `ArgumentException` for an invalid one, and a negative number
  throws an `ArgumentOutOfRangeException`. One overload takes the prerelease labels and the metadata as dot-separated
  strings, another as lists of identifiers.
- `ToString` writes the canonical form. The only formats are `null`, an empty string and `"G"`; the result does not
  depend on the culture.

## Next steps

- [Comparing versions](03-ComparingVersions.md): sort versions and compute the next one.
- [Version ranges](04-VersionRanges.md): check a version against a NuGet or npm range.
