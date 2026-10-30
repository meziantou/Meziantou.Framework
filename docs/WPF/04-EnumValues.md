---
title: Enum values
description: Fill a combo box or a list with the values of an enum from XAML, with the localized names of the Display attribute.
---

## List the values of an enum

`EnumValues` returns all the values of an enum, to use as the `ItemsSource` of a `ComboBox` or a `ListBox`:

<!-- ref:../../samples/Meziantou.Framework.WPF.EnumComboBox/Examples/EnumValuesExample.xaml;format=md-fenced-code-block;language=xml -->
```xml
<UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:wpf="clr-namespace:Meziantou.Framework.WPF;assembly=Meziantou.Framework.WPF"
             xmlns:local="clr-namespace:Meziantou.Framework.WPF.EnumComboBox">
    <ComboBox ItemsSource="{wpf:EnumValues {x:Type local:Week}}" />
</UserControl>
```
<!-- endref -->

The items are the enum values themselves, so the control shows their names, and `SelectedItem` binds to a property of
the enum type. The markup extension throws an `InvalidOperationException` when the type is not set or is not an enum.

## Show localized names

To show readable or translated names, put a `[Display]` attribute (from `System.ComponentModel.DataAnnotations`) on the
members of the enum. Its `Name` is the text to show, or, with `ResourceType`, the name of a static property of that type
that returns the text, such as the properties of a resource file:

<!-- ref:../../samples/Meziantou.Framework.WPF.EnumComboBox/Week.cs;region=LocalizedEnumValuesExtension_Enum;format=md-fenced-code-block;dedent=true -->
```csharp
public enum Week
{
    [Display(ResourceType = typeof(TestEnumResources), Name = "TestEnum_First")]
    First,

    [Display(ResourceType = typeof(TestEnumResources), Name = "TestEnum_Second")]
    Second,

    [Display(ResourceType = typeof(TestEnumResources), Name = "TestEnum_Third")]
    Third,

    [Display(ResourceType = typeof(TestEnumResources), Name = "TestEnum_Fourth")]
    Fourth,

    [Display(ResourceType = typeof(TestEnumResources), Name = "TestEnum_Last")]
    Last,
}
```
<!-- endref -->

`LocalizedEnumValues` returns a `LocalizedEnumValue` for each member. Show its `Name`, and select its `Value`:

<!-- ref:../../samples/Meziantou.Framework.WPF.EnumComboBox/Examples/LocalizedEnumValuesExample.xaml;format=md-fenced-code-block;language=xml -->
```xml
<UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:wpf="clr-namespace:Meziantou.Framework.WPF;assembly=Meziantou.Framework.WPF"
             xmlns:local="clr-namespace:Meziantou.Framework.WPF.EnumComboBox">
    <!-- Displays the names from the [Display] attributes of the enum members -->
    <ComboBox ItemsSource="{wpf:LocalizedEnumValues {x:Type local:Week}}" DisplayMemberPath="Name" SelectedValuePath="Value" />
</UserControl>
```
<!-- endref -->

- `Name` is the name of the `[Display]` attribute, read from the resources each time it is read, so it follows the
  current UI culture. A member without the attribute, or with an attribute that has no `Name`, shows the member name.
- `Value` is the enum value: with `SelectedValuePath="Value"`, bind `SelectedValue` to a property of the enum type.
- `ToString()` returns the `Name`, so the control shows it even without `DisplayMemberPath`.
- The list of an enum type is created once and shared by every control that uses it.
