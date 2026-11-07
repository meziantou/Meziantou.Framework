---
title: Introduction
description: Use the icons of Iconify icon sets as strongly-typed properties generated at build time, or load icon sets at run time.
---

`Meziantou.Framework.Iconify` gives .NET code access to the icons of [Iconify](https://iconify.design), which gathers
more than 200 open-source icon sets in one format: Material Design Icons, Lucide, Tabler, Fluent, Material Symbols, and
many others.

## What you can do with it

- **Use icons as properties**: declare an icon set in the project file, and the source generator that ships in the
  package writes a property for each of its icons, such as `Iconify.Mdi.Icons.Home`. See
  [Getting started](02-GettingStarted.md).
- **Get the SVG of an icon**: an [IconifyIcon](cref:Meziantou.Framework.IconifyIcon) has the content of the `svg`
  element, its view box, and the full SVG image.
- **Load icon sets at run time**: read any file in the IconifyJSON format, for instance icon sets that your users
  provide. See [Loading icon sets](03-LoadingIconSets.md).

## NativeAOT and trimming

The generated icons are properties that create the icon when they are read: the code has no reflection and no
resources. A trimmed or NativeAOT application only contains the icons it uses, even when the project declares a whole
icon set. An application that is not trimmed contains every generated icon, so use the `Icons` metadata to generate a
subset of a large icon set.

## Licenses

The package does not contain any icon. Each icon set has its own license, which can require attribution: check it
before you ship icons. The generated code and [IconifyIconSet.Info](cref:Meziantou.Framework.IconifyIconSet.Info)
expose the license of an icon set as an [IconifyIconSetInfo](cref:Meziantou.Framework.IconifyIconSetInfo).
