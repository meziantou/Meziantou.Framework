---
title: Getting started
description: Install Meziantou.Framework.WPF in a WPF application and reference its namespace from C# and XAML.
---

## Install the package

Add the `Meziantou.Framework.WPF` package to a WPF application:

```bash
dotnet add package Meziantou.Framework.WPF
```

The package targets Windows, so the project must target Windows and enable WPF:

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
</PropertyGroup>
```

## Use it from C#

The C# examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.WPF;`.

## Use it from XAML

Declare an XML namespace for the assembly on the root element of the XAML file, then prefix the types with it. For
instance, to close a window when the user presses Escape:

<!-- ref:../../samples/Meziantou.Framework.WPF.EnumComboBox/Examples/CloseOnEscapeKeyDownExample.xaml;format=md-fenced-code-block;language=xml -->
```xml
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:wpf="clr-namespace:Meziantou.Framework.WPF;assembly=Meziantou.Framework.WPF"
        wpf:WindowUtilities.CloseOnEscapeKeyDown="True">
</Window>
```
<!-- endref -->

The XAML examples of this documentation use the `wpf` prefix for this namespace.

## Next steps

- [Commands](03-Commands.md): bind buttons to the methods of a view model.
- [Enum values](04-EnumValues.md): fill a combo box with the values of an enum.
