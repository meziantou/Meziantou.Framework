---
title: Introduction
description: A collection of independent helpers and extension methods for strings, collections, files, processes, timing, reflection and more.
---

`Meziantou.Framework` is the core package of the repository: a collection of small, independent helpers and extension
methods that most applications end up writing themselves. Each helper solves one problem; you can use one without
learning the others.

## What you can do with it

| Topic | Helpers | Page |
| --- | --- | --- |
| Strings | Ordinal comparisons, prefixes and suffixes, diacritics, line splitting, Markdown escaping | [Strings](03-Strings.md) |
| Formatting and cultures | Invariant formatting, temporary cultures, ordinals, country flags | [Formatting and cultures](04-FormattingAndCultures.md) |
| String similarity | Levenshtein and Hamming distances, Soundex, Metaphone, short names | [String similarity](05-StringSimilarity.md) |
| Collections | Circular buffers, double-ended queues, sorted lists, tries, multi-value dictionaries, equatable immutable collections | [Collections](06-Collections/01-Overview.md) |
| Sequences | Filtering nulls, distinctness checks, async loops, cached sequences, dictionary helpers | [Sequences](07-Sequences.md) |
| Files and streams | Valid file names, recursive copy and delete, reading streams, restricted streams | [Files and streams](08-FilesAndStreams.md) |
| Processes | Finding executables in `PATH`, parent and child processes | [Processes](09-Processes.md) |
| Timing | Debounce and throttle actions | [Debounce and throttle](10-DebounceAndThrottle.md) |
| Dates, numbers and ranges | Start and end of periods, ranges, random values | [Dates, numbers and ranges](11-DatesNumbersAndRanges.md) |
| Reflection | Private members, attributes, assembly information, enums, expressions | [Reflection](12-Reflection.md) |
| Validation | Data annotations for absolute URIs and collection items | [Validation](13-Validation.md) |
| Diagnostics | Capture the activities started by a piece of code | [Activity listener](14-ActivityListener.md) |
| Other helpers | Deterministic GUIDs, optional values, lorem ipsum, handles, certificates | [Other helpers](15-OtherHelpers.md) |

## Namespaces

Most helpers are in the [Meziantou.Framework](cref:Meziantou.Framework!N:Meziantou.Framework) namespace. The others are in:

| Namespace | Contains |
| --- | --- |
| [Meziantou.Framework.Collections](cref:Meziantou.Framework.Collections) | The collections |
| [Meziantou.Framework.DataAnnotations](cref:Meziantou.Framework.DataAnnotations) | The validation attributes |
| [Meziantou.Framework.Diagnostics](cref:Meziantou.Framework!N:Meziantou.Framework.Diagnostics) | [ScopedActivityListener](cref:Meziantou.Framework.Diagnostics.ScopedActivityListener) |
| [Meziantou.Framework.Text](cref:Meziantou.Framework.Text) | UTF-8 helpers |

Most helpers are extension methods: once the namespace is imported, they appear on the types they extend, such as
`string`, `IEnumerable<T>`, `Stream` or `Process`.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and use a few helpers.
