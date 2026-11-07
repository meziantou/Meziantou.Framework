---
title: Boolean converter
description: Convert a boolean to a Visibility, a text or any other value in a WPF binding with BooleanToValueConverter.
---

[BooleanToValueConverter](cref:Meziantou.Framework.WPF.BooleanToValueConverter) converts a boolean to the value of your choice: declare it as a resource with the values for
`true` and `false`, then use it in bindings.

## Show or hide a control

<!-- ref:../../samples/Meziantou.Framework.WPF.EnumComboBox/Examples/BooleanToValueConverterExample.xaml;format=md-fenced-code-block;language=xml -->
```xml
<UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:wpf="clr-namespace:Meziantou.Framework.WPF;assembly=Meziantou.Framework.WPF">
    <UserControl.Resources>
        <wpf:BooleanToValueConverter x:Key="BoolToVisibility" TrueValue="{x:Static Visibility.Visible}" FalseValue="{x:Static Visibility.Collapsed}" />
    </UserControl.Resources>
    <TextBlock Text="Shown when IsEnabled is true" Visibility="{Binding IsEnabled, Converter={StaticResource BoolToVisibility}}" />
</UserControl>
```
<!-- endref -->

Declare one converter for each pair of values. The values can be of any type: a `Visibility`, a text, a brush, a
number…

## Handle null values

[NullValue](cref:Meziantou.Framework.WPF.BooleanToValueConverter.NullValue) is returned when the value is `null`, for instance a `bool?` property that is not set yet:

<!-- ref:../../samples/Meziantou.Framework.WPF.EnumComboBox/Examples/BooleanToValueConverterNullValueExample.xaml;format=md-fenced-code-block;language=xml -->
```xml
<UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:wpf="clr-namespace:Meziantou.Framework.WPF;assembly=Meziantou.Framework.WPF">
    <UserControl.Resources>
        <!-- NullValue is used when the value is null or cannot be converted to a boolean -->
        <wpf:BooleanToValueConverter x:Key="ApprovalToText" TrueValue="Approved" FalseValue="Rejected" NullValue="Pending" />
    </UserControl.Resources>
    <TextBlock Text="{Binding IsApproved, Converter={StaticResource ApprovalToText}}" />
</UserControl>
```
<!-- endref -->

## Conversion rules

| Value | Result |
| --- | --- |
| `true` | [TrueValue](cref:Meziantou.Framework.WPF.BooleanToValueConverter.TrueValue) |
| `false` | [FalseValue](cref:Meziantou.Framework.WPF.BooleanToValueConverter.FalseValue) |
| `null` | [NullValue](cref:Meziantou.Framework.WPF.BooleanToValueConverter.NullValue), or [FalseValue](cref:Meziantou.Framework.WPF.BooleanToValueConverter.FalseValue) when [NullValue](cref:Meziantou.Framework.WPF.BooleanToValueConverter.NullValue) is not set |
| Another value that converts to a boolean: a number (`0` is `false`, any other number is `true`), or the text `"true"` or `"false"` | [TrueValue](cref:Meziantou.Framework.WPF.BooleanToValueConverter.TrueValue) or [FalseValue](cref:Meziantou.Framework.WPF.BooleanToValueConverter.FalseValue) |
| A value that does not convert to a boolean, such as `"abc"` or a date | [NullValue](cref:Meziantou.Framework.WPF.BooleanToValueConverter.NullValue), or [FalseValue](cref:Meziantou.Framework.WPF.BooleanToValueConverter.FalseValue) when [NullValue](cref:Meziantou.Framework.WPF.BooleanToValueConverter.NullValue) is not set |

The converter is one-way: [ConvertBack](cref:Meziantou.Framework.WPF.BooleanToValueConverter.ConvertBack) throws a `NotSupportedException`. Do not use it in a `TwoWay` binding.
