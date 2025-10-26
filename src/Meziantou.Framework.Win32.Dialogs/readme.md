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

````c#
using Meziantou.Framework.Win32;

// Basic usage
var dialog = new OpenFolderDialog();
var result = dialog.ShowDialog();
if (result == DialogResult.OK)
{
    Console.WriteLine($"Selected folder: {dialog.SelectedPath}");
}

// Advanced usage with all options
var dialog = new OpenFolderDialog
{
    Title = "Select a folder",
    OkButtonLabel = "Choose",
    InitialDirectory = @"C:\Users",
    ChangeCurrentDirectory = false
};

var result = dialog.ShowDialog();
if (result == DialogResult.OK)
{
    Console.WriteLine($"Selected folder: {dialog.SelectedPath}");
}

// Show dialog with owner window (WPF example)
var dialog = new OpenFolderDialog
{
    Title = "Sample Open Folder dialog",
    OkButtonLabel = "Select Folder"
};
var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
var result = dialog.ShowDialog(hwnd);
````

