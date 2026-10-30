---
title: Folder dialog
description: Let the user pick a folder with the modern Windows folder dialog, from a console, WPF or Windows Forms application.
---

[OpenFolderDialog](cref:Meziantou.Framework.Win32.OpenFolderDialog) from the `Meziantou.Framework.Win32.Dialogs` package shows the folder picker of Windows, the same
dialog as File Explorer uses to open files, configured to select a folder. It needs no UI framework: it works from a
console application as well as from WPF or Windows Forms.

```bash
dotnet add package Meziantou.Framework.Win32.Dialogs
```

The examples of this page show a dialog: they are compiled, not run, by the build of the repository.

## Select a folder

<!-- ref:../../samples/Meziantou.Framework.Win32.DialogsSamples/ReadmeExamples.cs;region=OpenFolderDialog_Basic;format=md-fenced-code-block;dedent=true -->
```csharp
var dialog = new OpenFolderDialog();
var result = dialog.ShowDialog();
if (result == DialogResult.OK)
{
    Console.WriteLine($"Selected folder: {dialog.SelectedPath}");
}
```
<!-- endref -->

[ShowDialog](cref:Meziantou.Framework.Win32.OpenFolderDialog.ShowDialog) blocks until the user closes the dialog. [SelectedPath](cref:Meziantou.Framework.Win32.OpenFolderDialog.SelectedPath) is the full path of the folder when the result is
[DialogResult.OK](cref:Meziantou.Framework.Win32.DialogResult.OK), and `null` otherwise: each call resets it.

## Configure the dialog

<!-- ref:../../samples/Meziantou.Framework.Win32.DialogsSamples/ReadmeExamples.cs;region=OpenFolderDialog_Advanced;format=md-fenced-code-block;dedent=true -->
```csharp
var dialog = new OpenFolderDialog
{
    Title = "Select a folder",
    OkButtonLabel = "Choose",
    InitialDirectory = @"C:\Users",
    ChangeCurrentDirectory = false,
};

var result = dialog.ShowDialog();
if (result == DialogResult.OK)
{
    Console.WriteLine($"Selected folder: {dialog.SelectedPath}");
}
```
<!-- endref -->

| Property | Description |
| --- | --- |
| [Title](cref:Meziantou.Framework.Win32.OpenFolderDialog.Title) | The text of the title bar |
| [OkButtonLabel](cref:Meziantou.Framework.Win32.OpenFolderDialog.OkButtonLabel) | The text of the button that selects the folder |
| [InitialDirectory](cref:Meziantou.Framework.Win32.OpenFolderDialog.InitialDirectory) | The folder shown when the dialog opens |
| [ChangeCurrentDirectory](cref:Meziantou.Framework.Win32.OpenFolderDialog.ChangeCurrentDirectory) | `true` to make the selected folder the current directory of the process. The default, `false`, keeps the current directory |

[InitialDirectory](cref:Meziantou.Framework.Win32.OpenFolderDialog.InitialDirectory) accepts a path, with `\` or `/` separators, or a shell location such as `shell:Downloads`. When the
folder does not exist, or names a drive that is not mounted, the dialog ignores it and opens the folder Windows would
choose by default:

<!-- ref:../../samples/Meziantou.Framework.Win32.DialogsSamples/OpenFolderDialogExamples.cs;region=OpenFolderDialog_ShellLocation;format=md-fenced-code-block;dedent=true -->
```csharp
var dialog = new OpenFolderDialog
{
    // A path or a shell location. The dialog ignores a location that does not exist.
    InitialDirectory = "shell:Downloads",
};

dialog.ShowDialog();
```
<!-- endref -->

## Set the owner window

A dialog with an owner window stays in front of it, and the owner is disabled while the dialog is open. Pass the handle
of the window to [ShowDialog](cref:Meziantou.Framework.Win32.OpenFolderDialog.ShowDialog). In the code of a WPF `Window`:

<!-- ref:../../samples/Meziantou.Framework.Win32.DialogsSamples/MainWindow.xaml.cs;region=OpenFolderDialog_Owner;format=md-fenced-code-block;dedent=true -->
```csharp
var dialog = new OpenFolderDialog
{
    Title = "Sample Open Folder dialog",
    OkButtonLabel = "Select Folder",
};

var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
var result = dialog.ShowDialog(hwnd);
```
<!-- endref -->

In Windows Forms, pass the `Handle` of the form. Without an owner, [ShowDialog()](cref:Meziantou.Framework.Win32.OpenFolderDialog.ShowDialog) uses the active window of the calling
thread.

In the code of a WPF `Window`, [DialogResult](cref:Meziantou.Framework.Win32.DialogResult) refers to the `Window.DialogResult` property: write
[Meziantou.Framework.Win32.DialogResult.OK](cref:Meziantou.Framework.Win32.DialogResult.OK) to compare the result of [ShowDialog](cref:Meziantou.Framework.Win32.OpenFolderDialog.ShowDialog).

## Handle errors

[ShowDialog](cref:Meziantou.Framework.Win32.OpenFolderDialog.ShowDialog) returns [DialogResult.Cancel](cref:Meziantou.Framework.Win32.DialogResult.Cancel) when the user closes the dialog, and [DialogResult.Abort](cref:Meziantou.Framework.Win32.DialogResult.Abort) when the dialog
cannot be shown. It does not throw: [LastHResult](cref:Meziantou.Framework.Win32.OpenFolderDialog.LastHResult) gives the error code of the last call, which
`Marshal.GetExceptionForHR` turns into an exception:

<!-- ref:../../samples/Meziantou.Framework.Win32.DialogsSamples/OpenFolderDialogExamples.cs;region=OpenFolderDialog_HandleErrors;format=md-fenced-code-block;dedent=true -->
```csharp
var dialog = new OpenFolderDialog();
switch (dialog.ShowDialog())
{
    case DialogResult.OK:
        Console.WriteLine($"Selected folder: {dialog.SelectedPath}");
        break;

    case DialogResult.Cancel:
        // The user closed the dialog
        break;

    default:
        // The dialog could not be shown: LastHResult tells why
        throw Marshal.GetExceptionForHR(dialog.LastHResult) ?? new InvalidOperationException("The dialog failed");
}
```
<!-- endref -->
