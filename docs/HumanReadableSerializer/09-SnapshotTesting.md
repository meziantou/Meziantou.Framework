---
title: Snapshot testing
description: Configure the human-readable serializer used by the snapshot testing libraries.
---

`Meziantou.Framework.SnapshotTesting` and `Meziantou.Framework.InlineSnapshotTesting` write the value under test with the
human-readable serializer. They create the options with their own defaults, such as the HTTP converters and the JSON and
XML formatters, and let you change them:

- In `Meziantou.Framework.SnapshotTesting`, `ConfigureHumanReadableSerializer(options => ...)` on the settings. See
  [Serializers and comparers](../SnapshotTesting/05-SerializersAndComparers.md) and
  [Deterministic snapshots](../SnapshotTesting/03-WritingTests/06-DeterministicSnapshots.md), which also covers the
  scrubbing methods such as `ScrubGuid` and `UseRelativeDateTime`.
- In `Meziantou.Framework.InlineSnapshotTesting`, `WithSerializer(options => ...)` for one assertion, or
  `UseHumanReadableSerializer(options => ...)` on the settings. See
  [Serializers](../InlineSnapshotTesting/04-Serializers.md).

The callback receives a `HumanReadableSerializerOptions`: everything on the other pages of this section applies.

## Format HTML in every snapshot

Register the HTML formatter of `Meziantou.Framework.HumanReadableSerializer.Html` in a module initializer, so it applies
to every test of the project. With `Meziantou.Framework.SnapshotTesting`:

<!-- ref:../../samples/Meziantou.Framework.HumanReadableSerializer.Html.Samples/SnapshotTesting/SnapshotConfiguration.cs;format=md-fenced-code-block -->
```csharp
using System.Runtime.CompilerServices;
using Meziantou.Framework.HumanReadable.ValueFormatters;
using Meziantou.Framework.SnapshotTesting;

namespace SnapshotTestingSample;

internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SnapshotSettings.Default.ConfigureHumanReadableSerializer(options => options.AddHtmlFormatter(new HtmlFormatterOptions
        {
            OrderAttributes = true,
        }));
    }
}
```
<!-- endref -->

With `Meziantou.Framework.InlineSnapshotTesting`:

<!-- ref:../../samples/Meziantou.Framework.HumanReadableSerializer.Html.Samples/InlineSnapshotTesting/SnapshotConfiguration.cs;format=md-fenced-code-block -->
```csharp
using System.Runtime.CompilerServices;
using Meziantou.Framework.HumanReadable.ValueFormatters;
using Meziantou.Framework.InlineSnapshotTesting;

namespace InlineSnapshotTestingSample;

internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var settings = InlineSnapshotSettings.Default with { };
        settings.UseHumanReadableSerializer(options => options.AddHtmlFormatter(new HtmlFormatterOptions
        {
            OrderAttributes = true,
        }));
        InlineSnapshotSettings.Default = settings;
    }
}
```
<!-- endref -->

## Keep snapshots stable

- Sort the members and dictionary keys with `StringComparer.Ordinal`
  ([Members](04-Members.md#order-members-and-dictionary-keys)).
- Skip the members that change on every run, such as ids and timestamps, with `IgnoreMember`, or write them with a
  converter ([Members](04-Members.md#configure-types-you-do-not-own), [Converters](05-Converters.md)).
- Exclude or rewrite the HTTP headers that change ([HTTP messages](07-HttpMessages.md#hide-or-rewrite-headers)).
- Turn on `ShowInvisibleCharactersInValues` when two snapshots differ only by whitespace
  ([Output format](03-OutputFormat.md#invisible-characters-and-line-endings)).
