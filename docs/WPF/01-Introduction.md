---
title: Introduction
description: Meziantou.Framework.WPF provides commands, enum markup extensions, a boolean converter, and dispatcher and window helpers for WPF applications.
---

`Meziantou.Framework.WPF` is a set of small helpers for WPF applications that follow the MVVM pattern: the pieces that
most applications write again in every project.

## What you can do with it

- **Bind buttons and menus to methods of a view model**, synchronous or asynchronous, with `DelegateCommand`. See
  [Commands](03-Commands.md).
- **Fill a list or a combo box with the values of an enum**, with display names from the `[Display]` attribute. See
  [Enum values](04-EnumValues.md).
- **Convert a boolean to any value** in a binding, such as a `Visibility` or a text. See
  [Boolean converter](05-BooleanConverter.md).
- **Come back to the UI thread** after background work with `await dispatcher.SwitchToDispatcherThread()`. See
  [Dispatcher](06-Dispatcher.md).
- **Close a window with the Escape key**. See [Closing on Escape](07-CloseOnEscape.md).

The collection that can be updated from any thread while it is bound to a control, `ConcurrentObservableCollection<T>`,
is in the `Meziantou.Framework.Threading` package. See
[Observable collection](../Threading/07-ObservableCollection.md).

## Namespace

The types are in the `Meziantou.Framework.WPF` namespace:

| Type | Contains |
| --- | --- |
| `DelegateCommand`, `IDelegateCommand` | Commands that call delegates |
| `EnumValuesExtension`, `LocalizedEnumValuesExtension`, `LocalizedEnumValue` | Markup extensions that list the values of an enum |
| `BooleanToValueConverter` | A value converter for booleans |
| `DispatcherExtensions` | `SwitchToDispatcherThread` |
| `WindowUtilities` | The `CloseOnEscapeKeyDown` attached property |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and use it from XAML.
