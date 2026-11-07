---
title: Closing on Escape
description: Close a WPF window when the user presses the Escape key with the CloseOnEscapeKeyDown attached property.
---

Set the `WindowUtilities.CloseOnEscapeKeyDown` attached property to `True` on a window to close it when the user presses
Escape, as dialog boxes usually do:

<!-- ref:../../samples/Meziantou.Framework.WPF.EnumComboBox/Examples/CloseOnEscapeKeyDownExample.xaml;format=md-fenced-code-block;language=xml -->
```xml
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:wpf="clr-namespace:Meziantou.Framework.WPF;assembly=Meziantou.Framework.WPF"
        wpf:WindowUtilities.CloseOnEscapeKeyDown="True">
</Window>
```
<!-- endref -->

- The window handles the key before its controls: Escape closes it even when a text box has the focus, and the key is
  marked as handled, so a Cancel button or another handler does not react to it too.
- The property only has an effect on a `Window`. Setting it back to `False` stops closing the window on Escape.
- From code, call `WindowUtilities.SetCloseOnEscapeKeyDown(window, true)`.
- Closing the window raises its `Closing` event as usual, so the window can still cancel the close, for instance when
  there are unsaved changes.
