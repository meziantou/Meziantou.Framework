---
title: Configuring the process
description: Set the arguments, the working directory, the environment variables and the other start options of a process.
---

## Arguments

`WithArguments` replaces the arguments with the ones you pass, as a list, a span or any `IEnumerable<string>`. Each
string is one argument: spaces, quotes and backslashes reach the process as you wrote them, on every operating system.
You never build a command line yourself.

`ToString` returns the command line, with the arguments quoted when they need it, for logs and error messages:

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_ToString;format=md-fenced-code-block;dedent=true -->
```csharp
var command = ProcessWrapper.Create("dotnet")
    .WithArguments("build", "My Project.csproj");

// The command line, with the arguments quoted when needed
Assert.Equal(@"dotnet build ""My Project.csproj""", command.ToString());
```
<!-- endref -->

## Working directory

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_WorkingDirectory;format=md-fenced-code-block;dedent=true -->
```csharp
await ProcessWrapper.Create("git")
    .WithArguments("status")
    .WithWorkingDirectory("/path/to/repo")
    .ExecuteAsync();
```
<!-- endref -->

The executable is looked up in the directories of `PATH`, not in the working directory, so a file in the working
directory cannot replace the command `PATH` finds for the same name. Windows usually searches the current directory
first; `ProcessWrapper` does not, on any operating system. When the working directory is trusted and the executable
lives there, opt in with `WithSearchWorkingDirectory`:

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_SearchWorkingDirectory;format=md-fenced-code-block;dedent=true -->
```csharp
await ProcessWrapper.Create("run.sh")
    .WithWorkingDirectory("/path/to/scripts")
    .WithSearchWorkingDirectory()
    .ExecuteAsync();
```
<!-- endref -->

## Environment variables

The process inherits the environment variables of the current process. Change them with a callback or a dictionary:

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_EnvironmentVariables;format=md-fenced-code-block;dedent=true -->
```csharp
// Using callback
await ProcessWrapper.Create("my-app")
    .WithEnvironmentVariables(env => env
        .Set("MY_VAR", "value")
        .Remove("UNWANTED_VAR"))
    .ExecuteAsync();

// Using dictionary (null removes the variable)
await ProcessWrapper.Create("my-app")
    .WithEnvironmentVariables(new Dictionary<string, string?>
    {
        ["MY_VAR"] = "value",
        ["UNWANTED_VAR"] = null,
    })
    .ExecuteAsync();
```
<!-- endref -->

- `Set` adds or replaces a variable, and `Remove` removes it. In the dictionary, a `null` value removes the variable.
- Successive calls accumulate: each one changes the variables left by the previous ones.

## Windows credentials

On Windows, `WithCredentials(userName, password, domain)` starts the process as another user. The password is a
`string` or a `SecureString`, and the user name can be in the `user@domain` format when you do not pass a domain.
`WithUseCredentialsForNetworkingOnly` uses the credentials for network access only, like `runas /netonly`.

## Options of .NET 11

When the application targets .NET 11, more options are available:

| Method | Effect | Platforms |
| --- | --- | --- |
| `WithKillOnParentExit` | Kills the process when the current process exits | Windows, Linux, Android |
| `WithStartDetached` | Detaches the process from the current process | All |
| `WithStartSuspended` | Starts the process suspended | Windows, macOS |
| `StartAndForget` | Starts the process without tracking it and returns its id | All but iOS and tvOS |

`StartAndForget` throws an `InvalidOperationException` when the command has output targets, an input source or
[resource limits](08-ResourceLimits.md): nothing would read or apply them once the process is started.
