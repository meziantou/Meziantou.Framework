---
title: Deterministic snapshots
description: Scrub GUIDs, dates, user and machine names, and control line endings and invisible characters.
---

A snapshot must be the same on every run and every machine. Values that change from one run to the next (GUIDs, the
current time, the user name, a temporary path) make a test fail for no reason: replace them before the comparison.

There are two places to do it:

- **During serialization**, with the options of the human-readable serializer: you get the actual values, and can
  replace a value by type wherever it appears.
- **After serialization**, with scrubbers: they rewrite the text, line by line. Scrubbers apply to text snapshots only
  (see [Text and binary snapshots](02-SnapshotTypes.md#text-and-binary-snapshots)).

## Scrub values and lines

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_Scrubbing;format=md-fenced-code-block;dedent=true -->
```csharp
var value = new { Id = Guid.NewGuid(), Name = "John", GeneratedAt = DateTime.UtcNow };

var settings = SnapshotSettings.Default with { };
settings.ConfigureHumanReadableSerializer(options => options.ScrubGuid());
settings.ScrubLinesContaining("GeneratedAt:");

Snapshot.Validate(value, SnapshotType.Default, settings);
```
<!-- endref -->

The verified snapshot contains:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/__snapshots__/SnapshotTests_ReadmeExample_Scrubbing.verified.txt;format=md-fenced-code-block;language=text -->
```text
Id: 00000000-0000-0000-0000-000000000001
Name: John
```
<!-- endref -->

`ScrubGuid` replaces each distinct GUID with a sequential one (`00000000-0000-0000-0000-000000000001`, `...0002`), so
two equal GUIDs stay equal in the snapshot. `ScrubLinesContaining` removes the lines that contain one of the texts.

`SnapshotSettings.Default with { }` creates a copy of the default settings, so the scrubbers only apply to this test. To
scrub every snapshot, configure `SnapshotSettings.Default` once (see [Settings](../04-Settings.md)).

## Serializer options

These extension methods of `HumanReadableSerializerOptions` are available in `ConfigureHumanReadableSerializer`:

| Method | Effect |
| --- | --- |
| `ScrubGuid()` | Replaces each distinct GUID with a sequential one |
| `ScrubValue<T>()` | Replaces each distinct value of type `T` with `<Type>_<index>` (`String_0`, `String_1`). Overloads take an equality comparer or a function that formats the value from the value and its index |
| `UseRelativeDateTime(origin)` | Writes `DateTime` values as their difference with `origin` |
| `UseRelativeDateTimeOffset(origin)` | Writes `DateTimeOffset` values as their difference with `origin` |
| `UseRelativeTimeSpan(origin)` | Writes `TimeSpan` values as their difference with `origin` |

Relative dates keep the meaning of a value without its exact time:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_RelativeDateTime;format=md-fenced-code-block;dedent=true -->
```csharp
var now = DateTime.UtcNow;
var settings = SnapshotSettings.Default with { };
settings.ConfigureHumanReadableSerializer(options => options.UseRelativeDateTime(now));

Snapshot.Validate(new { CreatedAt = now.AddMinutes(-5), ExpiresAt = now.AddHours(1) }, settings);
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/__snapshots__/SnapshotTests_ReadmeExample_RelativeDateTime.verified.txt;format=md-fenced-code-block;language=text -->
```text
CreatedAt: -00:05:00
ExpiresAt: 01:00:00
```
<!-- endref -->

The other options of the serializer, such as ignoring members or ordering properties, are described in
[Serializers and comparers](../05-SerializersAndComparers.md).

## Scrubbers

These extension methods of `SnapshotSettings` add a scrubber to `SnapshotSettings.Scrubbers`:

| Method | Effect |
| --- | --- |
| `ScrubLines(predicate)` | Removes the lines for which the predicate returns `true` |
| `ScrubLinesContaining(params string[])` | Removes the lines that contain one of the texts. An overload takes a `StringComparison` |
| `ScrubLinesMatching(pattern)` | Removes the lines that match a regular expression, given as a `Regex` or a pattern with options and a timeout |
| `ScrubLinesWithReplace(replaceLine)` | Replaces each line with the result of the function. Return `null` to remove the line |
| `ScrubUserName()` | Replaces the name of the current user with `TheUserName` |
| `ScrubMachineName()` | Replaces the name of the machine with `TheMachineName` |

The line scrubbers split the text on `\r\n`, `\r` and `\n` only, and keep the line endings. A text that ends with a line
break has no empty last line: `ScrubLinesWithReplace(line => "// " + line)` turns `"a\nb\n"` into `"// a\n// b\n"`. A
leading UTF-8 byte order mark is removed before the scrubbers run, so a pattern anchored at the start of the first line
matches.

`ScrubUserName` and `ScrubMachineName` only replace whole words, ignoring case: with the user `runner`,
`/home/runner/work` is scrubbed but `xunit.runner.visualstudio` is not.

For anything else, derive from `Scrubber`, implement `Scrub(string text)`, and add an instance to
`SnapshotSettings.Scrubbers`.

## Line endings

The human-readable serializer writes `\n` line endings on every platform, so a snapshot produced on Windows is identical
to one produced on Linux or macOS. To write `\r\n` instead, set `NewLine`:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_LineEndings;format=md-fenced-code-block;dedent=true -->
```csharp
var settings = SnapshotSettings.Default with { };
settings.ConfigureHumanReadableSerializer(options => options.NewLine = "\r\n");
```
<!-- endref -->

Text snapshots are compared without regard to line endings anyway (see
[How each format is compared](02-SnapshotTypes.md#how-each-format-is-compared)).

## Invisible characters

When spaces, tabs or line endings matter, a snapshot that looks correct can hide a difference. Set
`ShowInvisibleCharactersInValues` to write them as Unicode control pictures:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_InvisibleCharacters;format=md-fenced-code-block;dedent=true -->
```csharp
var settings = SnapshotSettings.Default with { };
settings.ConfigureHumanReadableSerializer(options => options.ShowInvisibleCharactersInValues = true);

Snapshot.Validate("line 1 \r\nline\t2", settings);
```
<!-- endref -->

The verified snapshot contains:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/__snapshots__/SnapshotTests_ReadmeExample_InvisibleCharacters.verified.txt;format=md-fenced-code-block;language=text -->
```text
line 1␠␍␊
line␉2
```
<!-- endref -->

- A tab is `␉`, the line endings are `␍` and `␊`, U+0000 is `␀`, and so on.
- A space is kept between other characters, but written as `␠` at the start or the end of a line, where it would be
  invisible and where editors often remove it.
- The other space characters have no control picture and are written as their code point, such as `<U+00A0>` for a
  no-break space. So are the zero-width characters U+200B, U+200C, U+2060 and U+FEFF, and U+200D (zero-width joiner),
  except when it joins emoji into a single one.
- The control pictures of a line ending are followed by a real line break, written with `NewLine`, so each value keeps
  its lines.
- Every value and property name is converted, including single-line ones.
