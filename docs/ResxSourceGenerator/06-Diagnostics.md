---
title: Diagnostics
description: The warnings reported when a .resx file cannot be processed, and how to fix them.
---

The package includes an analyzer that reports the `.resx` files the generator cannot process. No class is generated for
the resource of such a file, so the errors that follow (a missing class or property) disappear once the warning is
fixed.

| ID | Severity | Title |
| --- | --- | --- |
| [MFRG0001](#mfrg0001-couldnt-parse-resx-file) | Warning | Couldn't parse Resx file |
| [MFRG0003](#mfrg0003-couldnt-compute-resource-name) | Warning | Couldn't compute resource name |
| [MFRG0004](#mfrg0004-inconsistent-properties) | Warning | Inconsistent properties |

## MFRG0001: Couldn't parse Resx file

The file is not valid XML.

**Fix:** open the file in an XML editor and fix the syntax, for instance a missing closing tag or an unescaped `&` or
`<` in a value.

## MFRG0003: Couldn't compute resource name

The generator could not determine the name of the embedded resource, which the `ResourceManager` needs.

**Fix:** set the `ResourceName` metadata on the `AdditionalFiles` item, or `GenerateResourcesType="false"` to generate
only the names class. See [Configuration](05-Configuration.md#resource-name).

## MFRG0004: Inconsistent properties

The files of a resource, such as `Errors.resx` and `Errors.fr.resx`, have different values for the same metadata, such
as `Namespace` or `ClassName`. The generator cannot choose one, so it generates nothing for the resource.

**Fix:** set the metadata on the neutral file only, or the same value on every file of the resource. A wildcard
`Update`, such as `Update="Errors*.resx"`, sets it on all of them.
