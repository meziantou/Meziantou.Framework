---
title: Package dependencies
description: See which Meziantou.Framework packages you also get when you install one.
---

When you install a Meziantou.Framework package, NuGet also installs the packages it depends on. The graphs below
show these dependencies. An arrow goes from a package to a package it depends on.

Select a package to highlight what it depends on in green and what depends on it in orange. Select it again to open
its page.

## All packages

<DependencyGraph Packages="*" />

## Imaging

`Meziantou.Framework.Imaging` brings the fonts, hashing and Unicode packages.

<DependencyGraph Packages="Meziantou.Framework.Imaging" Direction="TopDown" />

## Web platform

Packages that are not part of Meziantou.Framework have a dashed border and link to nuget.org.

<DependencyGraph Packages="Meziantou.Framework.WebPlatform*" External="true" />

## Snapshot testing

<DependencyGraph Packages="Meziantou.Framework.SnapshotTesting*, Meziantou.Framework.InlineSnapshotTesting" />

## ASP.NET Core service defaults

<DependencyGraph Packages="Meziantou.AspNetCore.ServiceDefaults" External="true" />
