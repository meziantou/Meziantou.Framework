---
title: Introduction
description: Validate .nupkg files before you publish them, with a library and a command-line tool that share the same rules.
---

A NuGet package can build and install, and still be hard to use: no license, no readme, a placeholder description,
assemblies built in Debug, or symbols that the debugger cannot find. These problems show up once the package is
published, when they are the hardest to fix.

`Meziantou.Framework.NuGetPackageValidation` opens a `.nupkg` file and checks it against a set of rules, before you push
it. `Meziantou.Framework.NuGetPackageValidation.Tool` runs the same rules from the command line, so a build pipeline can
fail when a package does not follow them.

## What you can do with it

- **Validate a package** from code or from the command line. See [Getting started](02-GettingStarted.md).
- **Know what is checked**: metadata, icon, license, readme, optimized assemblies, XML documentation, symbols and
  Source Link. See [Rules](03-Rules.md) and [Error codes](04-ErrorCodes.md).
- **Choose the rules and ignore some errors**, use other symbol servers, and authenticate the HTTP requests. See
  [Configuring the validation](05-ConfiguringValidation.md).
- **Write your own rules**, for instance to enforce a naming convention. See [Custom rules](06-CustomRules.md).
- **Validate packages in a build pipeline** with the command-line tool. See
  [Command-line tool](07-CommandLineTool.md) and [Running in CI](08-RunningInCI.md).

## Principles

- **Rules report errors with a number.** Each problem has an error code, so you can ignore one problem without
  turning off the whole rule.
- **A problem is a result, not an exception.** A missing or unreadable package is reported as an error too.
- **Some rules use the network.** They check that the project URL answers, download the source files that the symbols
  point to, and query symbol servers. See [Network access](08-RunningInCI.md#network-access).
- **The package is read, never modified.**

## Namespace

The types are in the [Meziantou.Framework.NuGetPackageValidation](cref:Meziantou.Framework.NuGetPackageValidation) namespace. The package path is a [FullPath](cref:Meziantou.Framework.FullPath), from the
[Meziantou.Framework](cref:Meziantou.Framework.FullPath!N:Meziantou.Framework) namespace:

| Type | Contains |
| --- | --- |
| [NuGetPackageValidator](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidator) | [ValidateAsync](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidator.ValidateAsync), which validates a package |
| [NuGetPackageValidationRules](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationRules) | The built-in rules, and the [Default](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationRules.Default) set |
| [NuGetPackageValidationOptions](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationOptions) | The rules, the excluded error codes, the symbol servers and the HTTP configuration |
| [NuGetPackageValidationResult](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationResult) | [IsValid](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationResult.IsValid) and the [Errors](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationResult.Errors) |
| [NuGetPackageValidationError](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationError) | The [ErrorCode](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationError.ErrorCode), [Message](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationError.Message), [HelpText](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationError.HelpText) and [FileName](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationError.FileName) of an error |
| [NuGetPackageValidationRule](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationRule), [NuGetPackageValidationContext](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationContext) | The base class and the context of a custom rule |

## Next steps

- [Getting started](02-GettingStarted.md): validate your first package.
