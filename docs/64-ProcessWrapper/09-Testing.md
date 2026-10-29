---
title: Testing
description: Replace processes with fake ones in tests, check the arguments your code passes, and change commands globally with interceptors.
---

Code that runs processes is hard to test: the tests depend on the tools installed on the machine, and are slow. A process
factory replaces the processes with in-memory fakes that return the output and the exit code you choose.

## Replace every process

`ProcessWrapper.DefaultProcessFactory` creates the processes of every command that has no factory of its own:

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_DefaultProcessFactory;format=md-fenced-code-block;dedent=true -->
```csharp
var previousFactory = ProcessWrapper.DefaultProcessFactory;
ProcessWrapper.DefaultProcessFactory = new FakeProcessFactory(startInfo =>
{
    return FakeProcess.Create(
        exitCode: 0,
        outputText: "simulated output",
        errorText: "");
});

try
{
    var result = await ProcessWrapper.Create("my-command")
        .WithArguments("--version")
        .ExecuteBufferedAsync();

    Assert.Equal("simulated output", result.Output.StandardOutput.Single().Text);
}
finally
{
    ProcessWrapper.DefaultProcessFactory = previousFactory;
}
```
<!-- endref -->

`DefaultProcessFactory` is global: restore it at the end of the test, as in the example, and do not run tests that change
it in parallel with tests that start processes.

## Replace the processes of one command

`WithProcessFactory` sets the factory of one command, without affecting the others. Prefer it when the code under test
lets you configure the command:

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_ProcessFactory;format=md-fenced-code-block;dedent=true -->
```csharp
var result = await ProcessWrapper.Create("my-command")
    .WithProcessFactory(new FakeProcessFactory(_ => FakeProcess.Create(0, "instance output", "")))
    .ExecuteBufferedAsync();

Assert.Equal("instance output", result.Output.StandardOutput.Single().Text);
```
<!-- endref -->

## Fake processes

`FakeProcess.Create` creates a process that exits as soon as it is started, with the exit code and output you give, as
text (encoded in UTF-8) or as streams. `FakeProcessFactory` returns fake processes:

- `new FakeProcessFactory(startInfo => ...)` calls the callback for each process, with the `ProcessStartInfo` the
  command would start.
- `new FakeProcessFactory(process1, process2, ...)` returns the processes in order, one per execution.

## Check the arguments

The callback receives the `ProcessStartInfo`, so the test can check the executable, the arguments, the working directory
or the environment variables:

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_AssertArguments;format=md-fenced-code-block;dedent=true -->
```csharp
var startInfos = new List<ProcessStartInfo>();
var factory = new FakeProcessFactory(startInfo =>
{
    startInfos.Add(startInfo);
    return FakeProcess.Create(exitCode: 0);
});

await ProcessWrapper.Create("git")
    .WithArguments("commit", "--message", "Fix \"quoted\" text")
    .WithProcessFactory(factory)
    .ExecuteAsync();

// Each argument reaches the process as is: no quoting or escaping is needed
var startInfo = Assert.Single(startInfos);
Assert.Equal(["commit", "--message", "Fix \"quoted\" text"], startInfo.ArgumentList);
```
<!-- endref -->

## Control when a process exits

`FakeProcess.CreatePending` creates a process that keeps running until you call `Complete`, optionally with another
exit code. Use it to test code that does something while the process runs, or that kills it. `ReadInputAsText` returns
what the command wrote to the standard input of the fake process:

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_PendingFakeProcess;format=md-fenced-code-block;dedent=true -->
```csharp
// A pending fake process runs until Complete is called
var fakeProcess = FakeProcess.CreatePending(exitCode: 0, outputText: "processed", errorText: "");

var instance = ProcessWrapper.Create("my-command")
    .WithInputStream(InputSource.FromText("input data"))
    .WithProcessFactory(new FakeProcessFactory(fakeProcess))
    .ExecuteBufferedAsync();

// The code under test can observe the running process here, then the test lets it exit
fakeProcess.Complete();
var result = await instance;

Assert.Equal("processed", result.Output.StandardOutput.Single().Text);
Assert.Equal("input data", fakeProcess.ReadInputAsText());
```
<!-- endref -->

Killing a fake process completes it, with the exit code -1 when its configured exit code is 0.

## Interceptors

Interceptors change commands before they run, without changing the code that creates them: add an environment variable to
every process, log the command lines, or redirect an executable in tests. There are two kinds:

- An `IProcessWrapperInterceptor` receives the `ProcessWrapper`, and can call its `With*` and `Add*` methods.
- An `IProcessStartInfoInterceptor` receives the `ProcessWrapper` and the `ProcessStartInfo` right before the process is
  created, with the executable already resolved.

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_InterceptorTypes;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class SampleProcessWrapperInterceptor : IProcessWrapperInterceptor
{
    public void Intercept(ProcessWrapper processWrapper)
    {
        // You can inspect/mutate the wrapper instance
        processWrapper.WithEnvironmentVariables(env => env.Set("TRACE_ID", Guid.NewGuid().ToString("N")));
    }
}

public sealed class SampleProcessStartInfoInterceptor : IProcessStartInfoInterceptor
{
    public void Intercept(ProcessWrapper processWrapper, ProcessStartInfo processStartInfo)
    {
        // You can inspect/mutate ProcessStartInfo right before process creation
        Console.WriteLine($"Executing: {processStartInfo.FileName}");
    }
}
```
<!-- endref -->

Register them for all commands with `ProcessWrapper.AddInterceptor`, which returns a registration to dispose, or for one
command with `WithInterceptor`:

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_Interceptors;format=md-fenced-code-block;dedent=true -->
```csharp
using var globalWrapperRegistration = ProcessWrapper.AddInterceptor(new SampleProcessWrapperInterceptor());
using var globalStartInfoRegistration = ProcessWrapper.AddInterceptor(new SampleProcessStartInfoInterceptor());

var result = await ProcessWrapper.Create("dotnet")
    .WithArguments("--version")
    .WithInterceptor(new SampleProcessWrapperInterceptor())
    .WithInterceptor(new SampleProcessStartInfoInterceptor())
    .ExecuteBufferedAsync();
```
<!-- endref -->

- The global interceptors run before the interceptors of the command, in the order they were added.
- They run each time the command is executed.
- An `IProcessWrapperInterceptor` changes the `ProcessWrapper` itself, so its changes stay on the command after the
  execution. An `IProcessStartInfoInterceptor` changes a copy made for one execution.
