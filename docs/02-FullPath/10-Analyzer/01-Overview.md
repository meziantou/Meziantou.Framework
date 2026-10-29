---
title: Overview
description: What the analyzer reports, how to apply its code fixes, and how to change the severity of its rules.
---

The `Meziantou.Framework.FullPath` package includes Roslyn analyzers and code fixes for C#. They find the code that
handles a `FullPath` as a string, and replace it with the `FullPath` members, operators and types. Nothing needs to
be installed or configured: the rules are active in every C# project that references the package.

The rules fall into three groups:

- **[Using FullPath members](02-UsingFullPathMembers.md)**: a `Path`, `Directory` or `Environment` method that has a
  `FullPath` equivalent, such as `Path.Combine(fullPath, "file.txt")` instead of `fullPath / "file.txt"`. Most of
  these rules have a code fix.
- **[Declaring FullPath instead of string](03-DeclaringFullPath.md)**: a method, a property, a variable or a parameter
  declared as `string` that only ever holds a `FullPath`.
- **[Comparisons and checks](04-ComparisonsAndChecks.md)**: comparisons that compare the strings of the paths instead
  of the paths, and checks for the empty path. Two of these rules are warnings, because the code is likely wrong.

## Rules

| Id | Description | Severity | Code fix |
| --- | --- | :--: | :--: |
| `MFFP0001` | [Use FullPath directly instead of Path.GetFullPath](02-UsingFullPathMembers.md) | Info | Yes |
| `MFFP0002` | [Use the right FullPath operand directly](02-UsingFullPathMembers.md) | Info | Yes |
| `MFFP0003` | [Use '/' with a FullPath base instead of Path.GetFullPath](02-UsingFullPathMembers.md) | Info | Yes |
| `MFFP0004` | [Use '/' operator instead of Path.Combine](02-UsingFullPathMembers.md) | Info | Yes |
| `MFFP0005` | [Use FullPath.Name instead of Path.GetFileName](02-UsingFullPathMembers.md) | Info | Yes |
| `MFFP0006` | [Use FullPath.NameWithoutExtension instead of Path.GetFileNameWithoutExtension](02-UsingFullPathMembers.md) | Info | Yes |
| `MFFP0007` | [Use FullPath.Extension instead of Path.GetExtension](02-UsingFullPathMembers.md) | Info | Yes |
| `MFFP0008` | [Use FullPath.Parent instead of Path.GetDirectoryName](02-UsingFullPathMembers.md) | Info | Yes |
| `MFFP0009` | [Use FullPath.WithExtension instead of Path.ChangeExtension](02-UsingFullPathMembers.md) | Info | Yes |
| `MFFP0010` | [Use FullPath.MakePathRelativeTo instead of Path.GetRelativePath](02-UsingFullPathMembers.md) | Info | Yes |
| `MFFP0011` | [Return FullPath instead of string](03-DeclaringFullPath.md) | Info | No |
| `MFFP0012` | [Declare the property as FullPath instead of string](03-DeclaringFullPath.md) | Info | No |
| `MFFP0013` | [Declare the variable as FullPath instead of string](03-DeclaringFullPath.md) | Info | No |
| `MFFP0014` | [Declare the parameter as FullPath instead of string](03-DeclaringFullPath.md) | Info | No |
| `MFFP0015` | [Compare FullPath values instead of their string representation](04-ComparisonsAndChecks.md) | Warning | No |
| `MFFP0016` | [FullPath.Equals with a string argument is always false](04-ComparisonsAndChecks.md) | Warning | No |
| `MFFP0018` | [Path.IsPathRooted and Path.IsPathFullyQualified are redundant on a FullPath](04-ComparisonsAndChecks.md) | Info | Yes |
| `MFFP0019` | [Simplify the FullPath.FromPath call](02-UsingFullPathMembers.md) | Info | Yes |
| `MFFP0020` | [Use FullPath.FromFileSystemInfo instead of FullPath.FromPath](02-UsingFullPathMembers.md) | Info | Yes |
| `MFFP0021` | [Use FullPath.IsEmpty](04-ComparisonsAndChecks.md) | Info | Yes |
| `MFFP0022` | [Use FullPath.CreateParentDirectory instead of Directory.CreateDirectory](02-UsingFullPathMembers.md) | Info | Yes |
| `MFFP0023` | [Use FullPath.Parent instead of Directory.GetParent](02-UsingFullPathMembers.md) | Info | No |
| `MFFP0024` | [Use the FullPath equivalent of Path.GetTempPath or Environment.GetFolderPath](02-UsingFullPathMembers.md) | Info | Yes |

All the rules are enabled by default. There is no `MFFP0017`.

## How a FullPath is recognized

The rules recognize a `FullPath` in all the forms that turn it into a string: the implicit conversion, a cast to
`string`, `Value`, `RawValue` and `ToString()`. `Path.GetFileName(fullPath)`, `Path.GetFileName(fullPath.Value)` and
`Path.GetFileName(fullPath.ToString())` are all reported, and all fixed to `fullPath.Name`.

The rules do nothing in a project that does not reference `Meziantou.Framework.FullPath`, and they skip generated code.

## Apply the code fixes

The code fixes are offered by the light bulb of the IDE, like the fixes of the .NET analyzers. Every code fix supports
**Fix all**, in a document, a project or a solution. Some fixes are not offered when they could change the behavior of
the code; each rule lists these cases.

A fix often changes the type of an expression from `string` to `FullPath`. As `FullPath` converts implicitly to
`string`, the code still compiles in most cases; once it does, the rules of
[Declaring FullPath instead of string](03-DeclaringFullPath.md) find the declarations to change next.

## Change the severity of a rule

Info rules appear as suggestions in the IDE and do not fail the build. To enforce a rule, raise its severity in the
`.editorconfig` file of the solution, for instance `dotnet_diagnostic.MFFP0004.severity = warning`. Set it to `none`
to disable a rule.
