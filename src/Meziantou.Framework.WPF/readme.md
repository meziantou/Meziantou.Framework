# Meziantou.Framework.WPF

[![NuGet](https://img.shields.io/nuget/v/Meziantou.Framework.WPF.svg)](https://www.nuget.org/packages/Meziantou.Framework.WPF/)

Utilities for WPF applications, including:

- `DelegateCommand` for synchronous and asynchronous commands
- `ConcurrentObservableCollection<T>` for thread-safe collections that can be bound to WPF controls
- Enum markup extensions (`EnumValuesExtension`, `LocalizedEnumValuesExtension`)
- `BooleanToValueConverter` for flexible bool-to-value conversion in bindings
- Dispatcher helpers (`SwitchToDispatcherThread`)

## Install

```powershell
dotnet add package Meziantou.Framework.WPF
```

## Usage

### Commands

```csharp
using Meziantou.Framework.WPF;

public sealed class SampleViewModel
{
    public IDelegateCommand SaveCommand { get; } = DelegateCommand.Create(
        execute: () => Console.WriteLine("Saved"),
        canExecute: () => true);
}
```

### Thread-safe bindable collection

```csharp
using Meziantou.Framework.WPF.Collections;

var collection = new ConcurrentObservableCollection<string>();
listBox.ItemsSource = collection.AsObservable;

await Task.Run(() => collection.Add("Item from background thread"));
```

### Enum values in XAML

```xml
<Window
    xmlns:wpf="clr-namespace:Meziantou.Framework.WPF;assembly=Meziantou.Framework.WPF">
    <ComboBox
        ItemsSource="{wpf:LocalizedEnumValues {x:Type local:MyEnum}}"
        DisplayMemberPath="Name"
        SelectedValuePath="Value" />
</Window>
```

### Boolean converter

```xml
<Window.Resources>
    <wpf:BooleanToValueConverter
        x:Key="BoolToVisibility"
        TrueValue="{x:Static Visibility.Visible}"
        FalseValue="{x:Static Visibility.Collapsed}" />
</Window.Resources>
```
