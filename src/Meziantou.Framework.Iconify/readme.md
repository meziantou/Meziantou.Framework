# Meziantou.Framework.Iconify

`Meziantou.Framework.Iconify` gives strongly-typed access to the icons of [Iconify](https://iconify.design): Material
Design Icons, Lucide, Tabler, Fluent, Material Symbols, and 200+ other icon sets. A source generator, which ships in
the package, turns the icon sets you declare in the project file into properties such as `Iconify.Mdi.Icons.Home`. The
library also loads any icon set at run time.

The generated code is trimmable and NativeAOT compatible: a trimmed application only contains the icons it uses.

## Install the package

```bash
dotnet add package Meziantou.Framework.Iconify
```

## Declare the icon sets

Add an `IconifyIconSet` item for each icon set to the project file:

```xml
<ItemGroup>
  <!-- Downloads json/mdi.json from the iconify/icon-sets repository on the first build -->
  <IconifyIconSet Include="mdi" Version="2.2.553" />

  <!-- Generates only some icons, with another name -->
  <IconifyIconSet Include="lucide" Version="2.2.553" Icons="house;arrow-*" ClassName="Lucide" />

  <!-- Uses a local file in the IconifyJSON format -->
  <IconifyIconSet Include="assets/my-icons.json" />
</ItemGroup>
```

| Metadata | Description |
| --- | --- |
| `Version` | The tag, branch or commit of [iconify/icon-sets](https://github.com/iconify/icon-sets) to download. The default is `master`. Pin it for reproducible builds. |
| `ClassName` | The name of the generated property. The default is the prefix in PascalCase (`material-symbols` becomes `MaterialSymbols`). |
| `Icons` | The names of the icons to generate, separated by `;`. `*` matches any characters. The default is every icon. |

An `Include` that ends with `.json` is a local file. Any other value is the prefix of an icon set, downloaded once and
cached in `obj/iconify` (set the `IconifyCacheDirectory` property to change it).

The generated types are `internal`. Set `<IconifyVisibility>public</IconifyVisibility>` to make them `public`.

## Use the icons

<!-- ref:../../samples/Meziantou.Framework.Iconify.Samples/Program.cs;region=Iconify_StaticIcon;format=md-fenced-code-block;dedent=true -->
```csharp
IconifyIcon home = Iconify.Mdi.Icons.Home;
Console.WriteLine(home.Name);   // home
Console.WriteLine(home.Width);  // 24
Console.WriteLine(home.Body);   // <path fill="currentColor" d="..."/>
Console.WriteLine(home.Svg);    // <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">...</svg>
```
<!-- endref -->

`Body` is the content of the `svg` element. `Svg` is the full SVG image, with a `width` and a `height` equal to the size
of the view box. The rotations and flips of the icon set are already applied.

The names of the icons are converted to PascalCase: `account-circle-outline` becomes `AccountCircleOutline`, and a name
that starts with a digit gets a `_` prefix (`4k` becomes `_4k`).

Each icon set has its own license. The generated code exposes it:

<!-- ref:../../samples/Meziantou.Framework.Iconify.Samples/Program.cs;region=Iconify_IconSetInfo;format=md-fenced-code-block;dedent=true -->
```csharp
var info = Iconify.Mdi.Info;
Console.WriteLine($"{info?.Name} by {info?.AuthorName}, {info?.LicenseSpdx}"); // Material Design Icons by Pictogrammers, Apache-2.0
```
<!-- endref -->

## Load an icon set at run time

`Iconify.Load` reads a file in the [IconifyJSON](https://iconify.design/docs/types/iconify-json.html) format, such as
the files of the iconify/icon-sets repository:

<!-- ref:../../tests/Meziantou.Framework.Iconify.Tests/IconifyIconSetTests.cs;region=Iconify_Load;format=md-fenced-code-block;dedent=true -->
```csharp
var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-icons.json");
IconifyIconSet iconSet = Iconify.Load(path);
IconifyIcon icon = iconSet["home"];
Assert.Equal("home", icon.Name);
Assert.Equal(24d, icon.Width);
Assert.Equal("""<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"><path fill="currentColor" d="M10 20v-6h4v6h5v-8h3L12 3L2 12h3v8z"/></svg>""", icon.Svg);

if (iconSet.TryGetIcon("house", out var alias))
{
    Assert.Equal(icon.Body, alias.Body);
}
```
<!-- endref -->

## Third-party notices

The rendering of the icons is ported from [@iconify/utils](https://github.com/iconify/iconify/tree/main/packages/utils).
See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
