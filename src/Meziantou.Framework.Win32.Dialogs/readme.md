# Meziantou.Framework.Win32.Dialogs

A library that provides a modern folder browser dialog using the Vista+ style dialog on Windows.

## Overview

This library provides an `OpenFolderDialog` class that uses the native Windows IFileOpenDialog COM interface to display a modern folder picker dialog. This is a significant improvement over the older `FolderBrowserDialog` which uses the legacy Windows folder browser UI.

## Features

- Modern Vista+ style folder picker dialog
- Customizable dialog title
- Custom OK button label
- Set initial directory
- Option to change current directory

## Usage

The types are in the `Meziantou.Framework.Win32` namespace.

Basic usage:

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

Advanced usage with all options:

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

Show the dialog with an owner window (WPF example, in the code of a `Window`):

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

In the code of a WPF `Window`, `DialogResult` refers to the `Window.DialogResult` property: write
`Meziantou.Framework.Win32.DialogResult.OK` to compare the result of `ShowDialog`.
