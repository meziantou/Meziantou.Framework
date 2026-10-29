---
title: Configuration
description: Choose the namespace, name and visibility of the generated class, name the members, and choose where the file is written.
---

The generated class is configured with MSBuild properties, and each file with the metadata of its `EmbeddedConstant`
item.

## MSBuild properties

| Property | Default | Effect |
| --- | --- | --- |
| `EmbeddedConstantsNamespace` | `$(RootNamespace)`, or the project name when it is empty | Namespace of the generated class. |
| `EmbeddedConstantsClassName` | `EmbeddedConstants` | Name of the generated class. |
| `EmbeddedConstantsClassVisibility` | `internal` | Visibility of the class: `internal` or `public`. |
| `EmbeddedConstantsMemberVisibility` | `public` | Visibility of the members: `internal` or `public`. |
| `EmbeddedConstantsOutputPath` | `$(IntermediateOutputPath)Meziantou.Framework.EmbeddedConstantsGenerator/EmbeddedConstants.g.cs` | Path of the generated file. |

Each property also has a prefixed form, such as `Meziantou_EmbeddedConstantsNamespace`, which takes precedence over
the short one. Use it when the short name conflicts with a property of another tool.

The visibilities are case-sensitive: `Public` is rejected with [MFECG0009](04-Errors.md).

### Share the constants with other projects

The class is `internal` by default, so only the project that declares the files can use it. To expose the files from
a library, make the class public:

```xml
<PropertyGroup>
  <EmbeddedConstantsClassVisibility>public</EmbeddedConstantsClassVisibility>
</PropertyGroup>
```

A `public const string` is copied into the assemblies that use it when they compile: they keep the old value until
they are rebuilt against the new version of the library. Expose a `static` property that returns the constant if the
content changes between versions.

### Write the file to another folder

By default, the file is written to the `obj` folder. Set `EmbeddedConstantsOutputPath` to write it somewhere else, for
instance to commit it and review its changes:

```xml
<PropertyGroup>
  <EmbeddedConstantsOutputPath>Generated/EmbeddedConstants.g.cs</EmbeddedConstantsOutputPath>
</PropertyGroup>
```

A relative path is relative to the project directory. The package makes sure the file is compiled once, even when it is
inside the project directory where the SDK includes every `.cs` file. When the project has several target frameworks,
the target framework is inserted into the path, unless the path already contains it: `Generated/EmbeddedConstants.g.cs`
becomes `Generated/net10.0/EmbeddedConstants.g.cs`, so the builds of each framework never write the same file.

## Item metadata

| Metadata | Required | Effect |
| --- | --- | --- |
| `Kind` | Yes | `Text`, `Binary` or `Both`, in any case. |
| `Name` | No | Base name of the generated members, instead of the file name. |

`EmbeddedConstantKind` and `Meziantou_EmbeddedConstantKind` are accepted instead of `Kind`, and `EmbeddedConstantName`
and `Meziantou_EmbeddedConstantName` instead of `Name`. When several are set, the prefixed one wins.

`EmbeddedConstant` is an item like any other, so wildcards work. Each file gets its own members:

```xml
<ItemGroup>
  <EmbeddedConstant Include="Sql/*.sql" Kind="Text" />
</ItemGroup>
```

## Member names

The base name of the members is the `Name` metadata when it is set, otherwise the file name without its extension. It
is converted to a PascalCase identifier: the characters that are not valid in an identifier separate words, the first
letter of each word is made uppercase, and a name that starts with a digit gets a `_` prefix. Then `Text` and `Bytes`
are appended:

| File | `Kind` | Members |
| --- | --- | --- |
| `hello-world.txt` | `Text` | `HelloWorldText` |
| `appsettings.Development.json` | `Both` | `AppsettingsDevelopmentText`, `AppsettingsDevelopmentBytes` |
| `logo.bin` | `Binary` | `LogoBytes` |
| `2024-report.csv` | `Text` | `_2024ReportText` |

Only the first letter of each word changes: the other letters keep their case.

### Two files with the same name

When two files without `Name` metadata produce the same member name, such as `Folder1/data.json` and
`Folder2/data.json`, their names are computed from their path relative to the project directory instead:
`Folder1DataText` and `Folder2DataText`. A file outside the project directory keeps its file name, because a name
computed from an absolute path would change with the location of the repository.

The names that still collide fail the build with [MFECG0005](04-Errors.md#mfecg0005-embedded-constant-member-name-is-duplicated):
set the `Name` metadata on all but one of the files. A member cannot have the name of the class either
([MFECG0010](04-Errors.md#mfecg0010-embedded-constant-member-name-is-the-same-as-the-generated-class-name)).

## Text and binary content

- A `Text` file must be valid UTF-8. A UTF-8 byte order mark at the start of the file is removed from the string.
- A `Text` file is limited to 1 MiB, so the generated source stays practical to compile.
- `Bytes` members always contain the file exactly as it is. A `Both` file that starts with a byte order mark has a
  `Text` member without it and a `Bytes` member with it.
- Binary files have no size limit, but they are written to the generated source as a list of bytes: keep them small.

## Incremental builds

The generated file is rewritten only when its content changes, and the files listed in `EmbeddedConstant` are inputs
of the Visual Studio up-to-date check: editing one of them rebuilds the project.
