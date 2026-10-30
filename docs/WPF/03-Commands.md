---
title: Commands
description: Bind buttons and menu items to synchronous and asynchronous methods of a view model with DelegateCommand.
---

A WPF button or menu item runs an `ICommand`. [DelegateCommand.Create](cref:Meziantou.Framework.WPF.DelegateCommand.Create) creates one from the methods of a view model: one
that runs the action, and optionally one that tells whether the action is available. It returns an [IDelegateCommand](cref:Meziantou.Framework.WPF.IDelegateCommand),
an `ICommand` that can also raise `CanExecuteChanged`.

## Create a command

<!-- ref:../../tests/Meziantou.Framework.WPF.Tests/DelegateCommandTests.cs;region=DelegateCommand_ViewModel;format=md-fenced-code-block;dedent=true -->
```csharp
public class MyViewModel
{
    public IDelegateCommand SaveCommand { get; }

    public MyViewModel()
    {
        SaveCommand = DelegateCommand.Create(Save, CanSave);
    }

    private void Save()
    {
        // Save logic
    }

    private bool CanSave()
    {
        return true;
    }
}
```
<!-- endref -->

Bind the command in XAML; the `DataContext` of the controls is the view model:

<!-- ref:../../samples/Meziantou.Framework.WPF.EnumComboBox/Examples/DelegateCommandExample.xaml;format=md-fenced-code-block;language=xml -->
```xml
<UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- The DataContext is the view model that exposes the commands -->
    <StackPanel>
        <TextBox Text="{Binding FileName, UpdateSourceTrigger=PropertyChanged}" />
        <Button Content="Save" Command="{Binding SaveCommand}" />
        <Button Content="Open report" Command="{Binding OpenCommand}" CommandParameter="report.txt" />
    </StackPanel>
</UserControl>
```
<!-- endref -->

The button calls `CanExecute` to enable or disable itself, and `Execute` when it is clicked. Without a `canExecute`
delegate, the command can always execute. A `null` action does nothing.

## Run an asynchronous method

Pass a method that returns a `Task` to create an asynchronous command. While the task runs, `CanExecute` returns
`false`, whatever `canExecute` says: the button is disabled, and clicking again does not start the method twice.

<!-- ref:../../tests/Meziantou.Framework.WPF.Tests/DelegateCommandTests.cs;region=DelegateCommand_AsyncCommand;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class EditorViewModel
{
    public EditorViewModel()
    {
        // The command cannot execute while SaveAsync runs, so the bound button is disabled
        SaveCommand = DelegateCommand.Create(SaveAsync, CanSave);
    }

    public IDelegateCommand SaveCommand { get; }

    public string Text { get; set; } = "";

    public string? ErrorMessage { get; private set; }

    public string? FileName
    {
        get;
        set
        {
            field = value;

            // CanSave depends on FileName: tell the bound controls to call it again
            SaveCommand.RaiseCanExecuteChanged();
        }
    }

    private bool CanSave() => !string.IsNullOrEmpty(FileName);

    private async Task SaveAsync()
    {
        try
        {
            await File.WriteAllTextAsync(FileName!, Text);
            ErrorMessage = null;
        }
        catch (IOException ex)
        {
            // Handle the errors here: the caller of the command cannot catch them
            ErrorMessage = ex.Message;
        }
    }
}
```
<!-- endref -->

Execution is fire-and-forget: the control that executes the command does not wait for the task. When the task fails,
the exception is rethrown on the dispatcher, where your code cannot catch it; it reaches
`Application.DispatcherUnhandledException`. Handle the errors inside the method, as the example does.

## Update the state of the command

WPF does not know when the result of `canExecute` changes. Call [RaiseCanExecuteChanged](cref:Meziantou.Framework.WPF.IDelegateCommand.RaiseCanExecuteChanged) when a value it depends on
changes, as the setter of `FileName` does in the example above: the bound controls then call `CanExecute` again.

[RaiseCanExecuteChanged](cref:Meziantou.Framework.WPF.IDelegateCommand.RaiseCanExecuteChanged) can be called from any thread. The command raises `CanExecuteChanged` on the thread of the
dispatcher where it was created, or of the dispatcher of the application when it was created on a thread without a
dispatcher. When it is called from another thread, the event is posted to that dispatcher, and raised later. Without
any dispatcher, for instance in a unit test, the event is raised immediately on the calling thread.

## Use the command parameter

The overloads that take an `Action<object?>` or a `Func<object?, Task>` receive the `CommandParameter` of the control,
and so do the `canExecute` delegates that take an `object?`:

<!-- ref:../../tests/Meziantou.Framework.WPF.Tests/DelegateCommandTests.cs;region=DelegateCommand_Parameter;format=md-fenced-code-block;dedent=true -->
```csharp
var openedFiles = new List<string>();
var openCommand = DelegateCommand.Create(
    execute: parameter => openedFiles.Add((string)parameter!),
    canExecute: parameter => parameter is string);

// The parameter is the CommandParameter of the button or menu item
Assert.False(openCommand.CanExecute(parameter: null));
Assert.True(openCommand.CanExecute("report.txt"));

openCommand.Execute("report.txt");
Assert.Equal(["report.txt"], openedFiles);
```
<!-- endref -->
