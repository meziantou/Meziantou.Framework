---
title: Deterministic snapshots
description: Scrub GUIDs, dates, JSON and XML values and whole lines, and show invisible characters.
---

A snapshot must be the same on every run and every machine. Values that change from one run to the next (GUIDs, the
current time, the user name) make a test fail for no reason: replace them before the comparison.

There are two places to do it:

- **During serialization**, with the options of the human-readable serializer: you get the actual values, and can
  replace a value by type wherever it appears.
- **After serialization**, with scrubbers: they rewrite the text, line by line.

## Scrub values by type

[ScrubValue\<T\>](cref:Meziantou.Framework.InlineSnapshotTesting.HumanReadableSerializerScrubExtensions.ScrubValue) replaces each distinct value of a type with `<Type>_<index>`. Equal values get the same replacement, so
the snapshot still shows which values are the same:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_ScrubValue;format=md-fenced-code-block;dedent=true -->
```csharp
var data = new string[] { "a", "a", "b" };
InlineSnapshot
    .WithSerializer(options => options.ScrubValue<string>())
    .Validate(data, """
        - String_0
        - String_0
        - String_1
        """);
```
<!-- endref -->

Pass an equality comparer to decide which values are the same:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_ScrubValueWithComparer;format=md-fenced-code-block;dedent=true -->
```csharp
var data = new string[] { "a", "A", "b" };
InlineSnapshot
    .WithSerializer(options => options.ScrubValue<string>(StringComparer.OrdinalIgnoreCase))
    .Validate(data, """
        - String_0
        - String_0
        - String_1
        """);
```
<!-- endref -->

Pass a function to write the replacement from the value and its index:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_ScrubValueWithFormatter;format=md-fenced-code-block;dedent=true -->
```csharp
var data = new string[] { "a", "A", "b" };
InlineSnapshot
    .WithSerializer(options => options.ScrubValue<string>((value, index) => $"{value}_{index}", StringComparer.OrdinalIgnoreCase))
    .Validate(data, """
        - a_0
        - a_0
        - b_1
        """);
```
<!-- endref -->

## Scrub GUIDs

[ScrubGuid](cref:Meziantou.Framework.InlineSnapshotTesting.HumanReadableSerializerScrubExtensions.ScrubGuid) replaces each distinct GUID with a sequential one, and keeps `Guid.Empty`:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_ScrubGuid;format=md-fenced-code-block;dedent=true -->
```csharp
var data = new Guid[] { Guid.NewGuid(), Guid.NewGuid(), Guid.Empty };
InlineSnapshot
    .WithSerializer(options => options.ScrubGuid())
    .Validate(data, """
        - 00000000-0000-0000-0000-000000000001
        - 00000000-0000-0000-0000-000000000002
        - 00000000-0000-0000-0000-000000000000
        """);
```
<!-- endref -->

## Relative dates

[UseRelativeDateTime](cref:Meziantou.Framework.InlineSnapshotTesting.HumanReadableSerializerScrubExtensions.UseRelativeDateTime) writes `DateTime` values as their difference with a reference time. [UseRelativeDateTimeOffset](cref:Meziantou.Framework.InlineSnapshotTesting.HumanReadableSerializerScrubExtensions.UseRelativeDateTimeOffset)
and [UseRelativeTimeSpan](cref:Meziantou.Framework.InlineSnapshotTesting.HumanReadableSerializerScrubExtensions.UseRelativeTimeSpan) do the same for `DateTimeOffset` and `TimeSpan` values:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_RelativeDateTime;format=md-fenced-code-block;dedent=true -->
```csharp
var now = DateTime.UtcNow;
var data = now.AddSeconds(10);
InlineSnapshot
    .WithSerializer(options => options.UseRelativeDateTime(now))
    .Validate(data, "00:00:10"); // TimeSpan relative to the now variable
```
<!-- endref -->

## Scrub JSON and XML values

[ScrubJsonValue](cref:Meziantou.Framework.InlineSnapshotTesting.HumanReadableSerializerScrubExtensions.ScrubJsonValue) replaces the nodes a JSONPath selects in JSON values:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_ScrubJsonValue;format=md-fenced-code-block;dedent=true -->
```csharp
var data = JsonNode.Parse("""{ "prop": "value" }""");
InlineSnapshot
    .WithSerializer(options => options.ScrubJsonValue("$.prop", node => "[redacted]"))
    .Validate(data, """
        {
          "prop": "[redacted]"
        }
        """);
```
<!-- endref -->

[ScrubXmlAttribute](cref:Meziantou.Framework.InlineSnapshotTesting.HumanReadableSerializerScrubExtensions.ScrubXmlAttribute) and [ScrubXmlNode](cref:Meziantou.Framework.InlineSnapshotTesting.HumanReadableSerializerScrubExtensions.ScrubXmlNode) replace the attributes and nodes an XPath selects in XML values. Overloads take an
`IXmlNamespaceResolver` for namespaced documents:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_ScrubXmlAttribute;format=md-fenced-code-block;dedent=true -->
```csharp
var data = XDocument.Parse("""
                <root>
                  <item attr="1">test1</item>
                </root>
                """);
InlineSnapshot
    .WithSerializer(options => options.ScrubXmlAttribute("//item/@attr", attribute => "[redacted]"))
    .Validate(data, """
        <root>
          <item attr="[redacted]">test1</item>
        </root>
        """);
```
<!-- endref -->

## Scrub lines

Scrubbers run on the serialized text. Remove the lines that match a predicate:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_ScrubLines;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshot
    .WithSettings(settings => settings.ScrubLines(line => line.Contains("dummy", StringComparison.Ordinal)))
    .Validate("abc\ndummy", "abc");
```
<!-- endref -->

Remove the lines that contain a text:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_ScrubLinesContaining;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshot
    .WithSettings(settings => settings.ScrubLinesContaining(StringComparison.Ordinal, "dummy"))
    .Validate("abc\ndummy", "abc");
```
<!-- endref -->

Remove the lines that match a regular expression:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_ScrubLinesMatching;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshot
    .WithSettings(settings => settings.ScrubLinesMatching("d.*y"))
    .Validate("abc\ndummy", "abc");
```
<!-- endref -->

Rewrite each line:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_ScrubLinesWithReplace;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshot
    .WithSettings(settings => settings.ScrubLinesWithReplace(line => line.Replace("abc", "123", StringComparison.Ordinal)))
    .Validate("abcdef", "123def");
```
<!-- endref -->

[ScrubUserName](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettingsScrubberExtensions.ScrubUserName) and [ScrubMachineName](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettingsScrubberExtensions.ScrubMachineName) replace the name of the current user and of the machine where they form a whole
word, ignoring case. For anything else, derive from [Scrubber](cref:Meziantou.Framework.InlineSnapshotTesting.Scrubber), implement `Scrub(string text)`, and add an instance to
[InlineSnapshotSettings.Scrubbers](cref:Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings.Scrubbers).

## Invisible characters

When spaces, tabs or line endings matter, write them as visible characters. Line endings are shown as control pictures
(`␍`, `␊`) followed by a normalized line break, a tab as `␉`, a space as `␠` only at the start or the end of a line, and
other space characters as their code point (`<U+00A0>`):

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_InvisibleCharacters;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshot
    .WithSerializer(options => options.ShowInvisibleCharactersInValues = true)
    .Validate("line 1 \r\nline\t2", """
        line 1␠␍␊
        line␉2
        """);
```
<!-- endref -->
