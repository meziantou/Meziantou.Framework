---
title: Getting started
description: Install the package, declare the icon sets of the project, and use the generated icons.
---

## Install the package

Add the `Meziantou.Framework.Iconify` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.Iconify" />

The package contains the source generator too: there is no other package to add. The generator requires C# 14, the
default language version of .NET 10.

## Declare the icon sets

Add an `IconifyIconSet` item for each icon set to the project file. The `Include` is the prefix of the icon set, as
shown on [icon-sets.iconify.design](https://icon-sets.iconify.design):

```xml
<ItemGroup>
  <IconifyIconSet Include="mdi" Version="2.2.553" />
</ItemGroup>
```

The first build downloads `json/mdi.json` from the [iconify/icon-sets](https://github.com/iconify/icon-sets)
repository, and the next builds read it from the cache, so they work offline.

## Use the icons

The generator adds a property to the [Iconify](cref:Meziantou.Framework.Iconify) class for each icon set, and a
property for each icon to its `Icons`:

<!-- ref:../../samples/Meziantou.Framework.Iconify.Samples/Program.cs;region=Iconify_StaticIcon;format=md-fenced-code-block;dedent=true -->
```csharp
IconifyIcon home = Iconify.Mdi.Icons.Home;
Console.WriteLine(home.Name);   // home
Console.WriteLine(home.Width);  // 24
Console.WriteLine(home.Body);   // <path fill="currentColor" d="..."/>
Console.WriteLine(home.Svg);    // <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">...</svg>
```
<!-- endref -->

- [Body](cref:Meziantou.Framework.IconifyIcon.Body) is the content of the `svg` element: paths, shapes and groups.
- [Left](cref:Meziantou.Framework.IconifyIcon.Left), [Top](cref:Meziantou.Framework.IconifyIcon.Top),
  [Width](cref:Meziantou.Framework.IconifyIcon.Width) and [Height](cref:Meziantou.Framework.IconifyIcon.Height) are
  the view box.
- [Svg](cref:Meziantou.Framework.IconifyIcon.Svg) is the full SVG image. Its `width` and `height` attributes are the
  size of the view box.

The rotations and flips that the icon set declares are already applied to the body and to the view box. The aliases of
the icon set are icons too: they have their own property.

The names of the icons are converted to PascalCase: `account-circle-outline` becomes `AccountCircleOutline`. A name
that starts with a digit gets a `_` prefix (`4k` becomes `_4k`). When two names give the same property name, the
shortest name keeps it, and the others get a number (`123` is `_123`, `1-2-3` is `_1232`).

Each icon set also has a `Prefix` and an `Info` property, which gives its name, author and license:

<!-- ref:../../samples/Meziantou.Framework.Iconify.Samples/Program.cs;region=Iconify_IconSetInfo;format=md-fenced-code-block;dedent=true -->
```csharp
var info = Iconify.Mdi.Info;
Console.WriteLine($"{info?.Name} by {info?.AuthorName}, {info?.LicenseSpdx}"); // Material Design Icons by Pictogrammers, Apache-2.0
```
<!-- endref -->

## Options

| Metadata | Description |
| --- | --- |
| `Version` | The tag, branch or commit of the iconify/icon-sets repository to download. The default is `master`. |
| `ClassName` | The name of the generated property. The default is the prefix in PascalCase: `material-symbols` becomes `MaterialSymbols`. |
| `Icons` | The names of the icons to generate, separated by `;`. `*` matches any characters. The default is every icon. |

```xml
<ItemGroup>
  <IconifyIconSet Include="material-symbols" Version="2.2.553" Icons="home;settings*" ClassName="Symbols" />
</ItemGroup>
```

A pattern of `Icons` that matches no icon produces the `MFICONIFY004` warning.

### Use a local file

An `Include` that ends with `.json` is a local file in the IconifyJSON format, for instance an icon set you made:

```xml
<ItemGroup>
  <IconifyIconSet Include="assets/my-icons.json" />
</ItemGroup>
```

### Pin the version

Without `Version`, the build downloads the latest file of the `master` branch, then keeps using the cached copy: two
machines can build different icons. Pin the version to a tag of the iconify/icon-sets repository for reproducible
builds.

The cache is the `iconify` folder of the intermediate output folder (`obj/iconify`). Set the `IconifyCacheDirectory`
property to share it between projects, and delete it to download the files again.

### Make the generated types public

The generated types are `internal`, so each assembly has its own icons. To expose them to other assemblies, set the
`IconifyVisibility` property:

```xml
<PropertyGroup>
  <IconifyVisibility>public</IconifyVisibility>
</PropertyGroup>
```

## Diagnostics

| Id | Severity | Description |
| --- | --- | --- |
| `MFICONIFY001` | Error | The file is not a valid icon set. |
| `MFICONIFY002` | Error | The file cannot be read. |
| `MFICONIFY003` | Error | Two icon sets have the same name. Set the `ClassName` metadata of one of them. |
| `MFICONIFY004` | Warning | A pattern of the `Icons` metadata matches no icon. |

## See the generated code

The generated code is not written to disk by default. To inspect it, ask the compiler to write the generated files:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```
