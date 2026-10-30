---
title: Comparisons and checks
description: The rules that report comparisons of the strings of paths, FullPath.Equals with a string, and checks for the empty path.
---

## MFFP0015: Compare FullPath values instead of their string representation

This rule is a **warning**. It reports a comparison of strings where one of the strings is a `FullPath`:

- `fullPath == text`, `fullPath != text`, `fullPath.Value == other.Value`, `string.Equals(fullPath, text)` and
  `fullPath.Value.Equals(text)` compare the strings ordinally and case-sensitively, on every operating system.
  Comparing `FullPath` values ignores case on Windows, macOS, iOS and tvOS.
- `string.Compare(fullPath, text)` and `fullPath.Value.CompareTo(text)` compare the strings with the current culture,
  so the order of the paths depends on the machine. `FullPath` values sort ordinally.

To fix it, convert the string to a `FullPath` and compare the paths: `fullPath == FullPath.FromPath(text)`. Choose the
case sensitivity with `Equals(other, ignoreCase)` or a `FullPathComparer`: see
[Comparing paths](../05-ComparingPaths.md).

The rule does not report the overloads that take a `StringComparison`, such as
`string.Equals(fullPath, text, StringComparison.Ordinal)`: they state that a string comparison is intended.

## MFFP0016: FullPath.Equals with a string argument is always false

This rule is a **warning**. `fullPath.Equals("value")`, `fullPath.Equals(other.Value)` or
`fullPath.Equals(other.ToString())` calls `Equals(object)`, which returns `true` for a `FullPath` only: with a string,
the result is always `false`.

Compare with a `FullPath` instead: `fullPath.Equals(other)`, or `fullPath.Equals(FullPath.FromPath(text))` for a
string. The rule does not report an argument typed as `object`, which can hold a `FullPath`.

## MFFP0018: Path.IsPathRooted and Path.IsPathFullyQualified are redundant on a FullPath

`Path.IsPathRooted(fullPath)` and `Path.IsPathFullyQualified(fullPath)` are `true` for every `FullPath` except the
empty one. The fix replaces them with `!fullPath.IsEmpty`, which gives the same result.

## MFFP0021: Use FullPath.IsEmpty

The rule reports the ways to check for the empty path other than `IsEmpty`:

| Reported | Fixed to |
| --- | --- |
| `string.IsNullOrEmpty(fullPath)` or `string.IsNullOrWhiteSpace(fullPath)` | `fullPath.IsEmpty` |
| `fullPath == FullPath.Empty` | `fullPath.IsEmpty` |
| `fullPath != FullPath.Empty` | `!fullPath.IsEmpty` |
| `fullPath.Value.Length == 0` | `fullPath.IsEmpty` |

The results are the same, and `IsEmpty` is clearer.
