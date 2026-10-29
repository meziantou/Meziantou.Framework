---
title: Configuration
description: Choose the namespace, class name, resource name and visibility of the generated classes, per project or per file.
---

## Project properties

| Property | Default | Effect |
| --- | --- | --- |
| `DefaultResourcesNamespace` | The namespace of the folder | Namespace of the classes of every `.resx` file. |
| `DefaultResourcesVisibility` | `internal` | Visibility of the classes: `public` or `internal`. |

```xml
<PropertyGroup>
  <DefaultResourcesNamespace>MyApp.Resources</DefaultResourcesNamespace>
  <DefaultResourcesVisibility>public</DefaultResourcesVisibility>
</PropertyGroup>
```

By default, the namespace is the root namespace of the project followed by the folders of the file: `Resources/Errors.resx`
is in `<RootNamespace>.Resources`. Any visibility other than `public`, whatever its case, gives `internal` classes.

## Per-file metadata

Set metadata on the `AdditionalFiles` item of a file to configure it alone. Metadata wins over the project properties:

```xml
<ItemGroup>
  <AdditionalFiles Include="**/*.resx" />
  <AdditionalFiles Update="Errors.resx"
                   Namespace="MyApp.Errors"
                   ClassName="ErrorMessages"
                   Visibility="public"
                   GenerateKeyNamesType="false" />
</ItemGroup>
```

| Metadata | Default | Effect |
| --- | --- | --- |
| `Namespace` | `DefaultResourcesNamespace` | Namespace of the classes. |
| `ClassName` | The file name | Name of the resource class. The names class is named `<ClassName>Names`. |
| `Visibility` | `DefaultResourcesVisibility` | Visibility of the classes. |
| `ResourceName` | The manifest resource name computed by MSBuild | Name of the embedded resource that the `ResourceManager` reads. |
| `GenerateResourcesType` | `true` | `false` to not generate the resource class. |
| `GenerateKeyNamesType` | `true` | `false` to not generate the names class. |

Use `Update` to change an item that a wildcard already includes, as above, or `Include` to add a single file with its
metadata.

The localized files of a resource (`Errors.fr.resx`...) share the classes of the neutral file, so their metadata must be
the same as the neutral file, or not set. Different values are reported by
[MFRG0004](06-Diagnostics.md#mfrg0004-inconsistent-properties) and the classes of the resource are not generated.

## Resource name

The `ResourceManager` needs the name under which MSBuild embeds the `.resx` file: the root namespace, the folders and the
file name, such as `MyApp.Resources.Errors`. The package reads the name that MSBuild computes for the `EmbeddedResource`
item of the file, so you rarely need `ResourceName`. Set it when the resources are embedded under another name, for
instance with a `LogicalName`: reading a resource then throws a `MissingManifestResourceException`.
