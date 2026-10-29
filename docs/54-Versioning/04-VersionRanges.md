---
title: Version ranges
description: Parse NuGet and npm version ranges, create ranges, and check whether a version satisfies them.
---

A `SemanticVersionRange` is an interval of versions: an optional minimum and an optional maximum, each inclusive or
exclusive. `Satisfies(version)` tells whether a version is in the range.

## NuGet ranges

`ParseNuGet` and `TryParseNuGet` read the range syntax of NuGet package references:

<!-- ref:../../tests/Meziantou.Framework.Versioning.Tests/SemanticVersionRangeTests.cs;region=SemanticVersionRange_NuGet;format=md-fenced-code-block;dedent=true -->
```csharp
var range = SemanticVersionRange.ParseNuGet("[1.0.0, 2.0.0)");
Assert.True(range.Satisfies(SemanticVersion.Parse("1.0.0")));
Assert.True(range.Satisfies(SemanticVersion.Parse("1.9.3")));
Assert.False(range.Satisfies(SemanticVersion.Parse("2.0.0")));

// A version alone is a minimum version
Assert.Equal("[1.0.0, )", SemanticVersionRange.ParseNuGet("1.0.0").ToString());

Assert.Equal("[1.0.0]", SemanticVersionRange.ParseNuGet("[1.0.0]").ToString());
Assert.Equal("(, 2.0.0]", SemanticVersionRange.ParseNuGet("(,2.0.0]").ToString());
Assert.False(SemanticVersionRange.TryParseNuGet("[1.0.0, 2.0.0", out _));
```
<!-- endref -->

| Range | Meaning |
| --- | --- |
| `1.0.0` | 1.0.0 or greater |
| `[1.0.0]` | 1.0.0 only |
| `[1.0.0,)` | 1.0.0 or greater |
| `(1.0.0,)` | Greater than 1.0.0 |
| `(,1.0.0]` | 1.0.0 or lower |
| `(,1.0.0)` | Lower than 1.0.0 |
| `[1.0.0,2.0.0]` | From 1.0.0 to 2.0.0, both included |
| `[1.0.0,2.0.0)` | From 1.0.0, included, to 2.0.0, excluded |
| `(1.0.0,2.0.0)` | Between 1.0.0 and 2.0.0, both excluded |

`[` and `]` include the bound, `(` and `)` exclude it. Spaces around the bounds are allowed.

## npm ranges

`ParseNpm` and `TryParseNpm` read the range syntax of npm `package.json` files:

<!-- ref:../../tests/Meziantou.Framework.Versioning.Tests/SemanticVersionRangeTests.cs;region=SemanticVersionRange_Npm;format=md-fenced-code-block;dedent=true -->
```csharp
var caret = SemanticVersionRange.ParseNpm("^1.2.3");
Assert.Equal("[1.2.3, 2.0.0)", caret.ToString());
Assert.True(caret.Satisfies(SemanticVersion.Parse("1.9.0")));
Assert.False(caret.Satisfies(SemanticVersion.Parse("2.0.0")));

Assert.Equal("[0.2.3, 0.3.0)", SemanticVersionRange.ParseNpm("^0.2.3").ToString());
Assert.Equal("[1.2.3, 1.3.0)", SemanticVersionRange.ParseNpm("~1.2.3").ToString());
Assert.Equal("[1.2.0, 1.3.0)", SemanticVersionRange.ParseNpm("1.2.x").ToString());
Assert.Equal("[1.0.0, 2.0.0]", SemanticVersionRange.ParseNpm("1.0.0 - 2.0.0").ToString());
Assert.Equal("[1.0.0, 2.0.0)", SemanticVersionRange.ParseNpm(">=1.0.0 <2.0.0").ToString());
Assert.Equal("[1.0.0]", SemanticVersionRange.ParseNpm("1.0.0").ToString());
Assert.Equal("*", SemanticVersionRange.ParseNpm("*").ToString());

// A range is a single interval, so a union of ranges is rejected
Assert.False(SemanticVersionRange.TryParseNpm("^1.0.0 || ^2.0.0", out _));
```
<!-- endref -->

| Range | Meaning |
| --- | --- |
| `1.0.0`, `=1.0.0` | 1.0.0 only |
| `>1.0.0`, `>=1.0.0`, `<1.0.0`, `<=1.0.0` | Comparisons |
| `>=1.0.0 <2.0.0` | Several comparisons separated by spaces: all of them apply |
| `~1.2.3`, `~1.2`, `~1` | Patch changes: `>=1.2.3 <1.3.0`, `>=1.2.0 <1.3.0`, `>=1.0.0 <2.0.0` |
| `^1.2.3`, `^0.2.3`, `^0.0.3` | Changes that keep the left-most non-zero number: `>=1.2.3 <2.0.0`, `>=0.2.3 <0.3.0`, `>=0.0.3 <0.0.4` |
| `1.0.0 - 2.0.0` | Hyphen range: `>=1.0.0 <=2.0.0` |
| `*`, `1.x`, `1.2.x` | Any version, any 1.x.x version, any 1.2.x version |

A `SemanticVersionRange` is a single interval, so a union such as `^1.0.0 || ^2.0.0` cannot be represented: `TryParseNpm`
returns `false`, and the `FormatException` of `ParseNpm` says so.

## Prerelease versions

`Satisfies` compares the version with the bounds using the [Semantic Versioning precedence](03-ComparingVersions.md),
and nothing else. A prerelease version is in a range when it is between the bounds:

<!-- ref:../../tests/Meziantou.Framework.Versioning.Tests/SemanticVersionRangeTests.cs;region=SemanticVersionRange_Prerelease;format=md-fenced-code-block;dedent=true -->
```csharp
// Bounds are compared with the Semantic Versioning precedence, so a prerelease
// of the upper bound is lower than the upper bound
var range = SemanticVersionRange.ParseNpm("^1.2.3");
Assert.True(range.Satisfies(SemanticVersion.Parse("1.5.0-beta")));
Assert.True(range.Satisfies(SemanticVersion.Parse("2.0.0-beta")));
```
<!-- endref -->

npm and NuGet apply extra rules to prerelease versions when they resolve a dependency; `Satisfies` does not. To keep
only stable versions, check `IsPrerelease` before you call it.

## Create a range

The constructor and the factory methods create a range from versions:

<!-- ref:../../tests/Meziantou.Framework.Versioning.Tests/SemanticVersionRangeTests.cs;region=SemanticVersionRange_Create;format=md-fenced-code-block;dedent=true -->
```csharp
var v1 = SemanticVersion.Parse("1.0.0");
var v2 = SemanticVersion.Parse("2.0.0");

var range = new SemanticVersionRange(v1, v2, isMinInclusive: true, isMaxInclusive: false);
Assert.Equal(SemanticVersionRange.ParseNuGet("[1.0.0, 2.0.0)"), range);

Assert.Equal("(1.0.0, )", SemanticVersionRange.GreaterThan(v1).ToString());
Assert.Equal("(, 2.0.0)", SemanticVersionRange.LessThan(v2).ToString());
Assert.True(SemanticVersionRange.All.Satisfies(SemanticVersion.Parse("0.0.1-alpha")));
```
<!-- endref -->

- `Exact`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan` and `LessThanOrEqual` create a range with one or two bounds.
- `SemanticVersionRange.All` contains every version.
- `MinVersion`, `MaxVersion`, `IsMinInclusive` and `IsMaxInclusive` return the bounds. A bound is `null` when the range
  has no limit on that side.
- `ToString` writes the range in the NuGet format, and `*` for `All`. Two ranges are equal when their bounds are equal.
