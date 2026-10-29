---
title: Introduction
description: xUnit.v3 attributes that run or skip a test depending on the environment the test suite runs in.
---

`Meziantou.Xunit` provides two attributes for xUnit.v3 tests: `RunIf` runs a test only in the environments you list,
and `SkipIf` skips it in those environments. A test that does not run is reported as *skipped*, with the reason, rather
than as passed or failed.

The attributes check the operating system, the globalization mode, the continuous integration server and the Windows
group of the current user. A condition is evaluated each time the test is about to run, so the same test assembly can
run everywhere and report what it could not test.

## What you can do with it

- **Run a test on some operating systems only**, or in a given
  [globalization mode](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/globalization). See
  [Getting started](02-GettingStarted.md).
- **Skip a test on a CI server**, or when the Windows user is, or is not, an administrator. See
  [Conditions](03-Conditions.md).
- **Combine conditions** on a test, on its class, or in an attribute of your own. See
  [Combining conditions](04-CombiningConditions.md).

## When to use it

Use these attributes for tests that cannot give a meaningful result in some environments: a test of a Windows API on
Linux, a test that needs the culture data of the operating system in invariant globalization mode, a test that needs
administrator rights. Do not use them to hide a test that fails for an unknown reason: a skipped test is easy to
forget.

To decide whether to skip a test from inside the test, call `Assert.XunitSkip` of
[Meziantou.Framework.Assertions](../78-Assertions/01-Introduction.md) instead.

## Namespace

The attributes and the enumerations are in the `Meziantou.Xunit` namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and skip your first test.
- [Conditions](03-Conditions.md): the conditions you can test, and what each one checks.
