# Meziantou.Framework.Versioning

Provide `SemanticVersion` which allows to parse [semantic version 2.0.0](https://semver.org/). It allows provide comparison methods and operators to compare multiple semantic versions.

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
