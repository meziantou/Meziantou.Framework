---
title: Using containers in tests and CI
description: Start a container per test, per test class or per test run in xUnit, see its logs in the test output, and run the tests on a build agent.
---

Starting a container takes from a second to a minute, depending on the image. Decide how many your tests need:

- **One per test** isolates the tests completely, at the cost of a start for each one.
- **One per test class** or **one per test run** is started once, but the tests share it: give each test its own data
  (a database, a schema, a key prefix).
- **One across runs** keeps the container between test runs. See [Reusing a container](08-ReusingContainers.md).

The examples use xUnit v3. The other test frameworks have the same hooks: a class or assembly setup and teardown
(`[ClassInitialize]` and `[AssemblyInitialize]` in MSTest, `[OneTimeSetUp]` in NUnit) where you start and dispose the
container.

## A container per test

Start the container in the test and dispose it at the end of the test:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/TestFrameworkExamples.cs;region=TemporaryContainers_XunitContainerPerTest;format=md-fenced-code-block;dedent=true -->
```csharp
[Fact]
public async Task ApplyMigrations()
{
    var definition = ContainerDefinition.CreatePostgreSql();
    definition.Logging.Logger = XUnitLogger.CreateLogger(); // the container logs go to the output of the test

    await using var container = definition.CreateContainer();
    await container.StartAsync(TestContext.Current.CancellationToken);

    var connectionString = container.GetConnectionString();
    Assert.StartsWith("Host=127.0.0.1;", connectionString, StringComparison.Ordinal); // run the migrations against the database instead
}
```
<!-- endref -->

Pass the cancellation token of the test to `StartAsync`, so a test that is canceled or times out stops waiting for the
container.

## A container per test class

Start the container in a fixture that implements `IAsyncLifetime`:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/TestFrameworkExamples.cs;region=TemporaryContainers_XunitFixture;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = ContainerDefinition.CreatePostgreSql().CreateContainer();

    public async ValueTask InitializeAsync() => await Container.StartAsync();

    public ValueTask DisposeAsync() => Container.DisposeAsync();
}
```
<!-- endref -->

Then share it with the tests of a class with `IClassFixture<T>`. xUnit creates the fixture before the first test of the
class and disposes it after the last one:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/TestFrameworkExamples.cs;region=TemporaryContainers_XunitClassFixture;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class OrderRepositoryTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public void SaveOrder()
    {
        var connectionString = database.Container.GetConnectionString();
        Assert.StartsWith("Host=127.0.0.1;", connectionString, StringComparison.Ordinal); // open a connection and test the repository instead
    }
}
```
<!-- endref -->

## A container per test run

To start the container once for the whole test assembly, register the same fixture as an assembly fixture:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/TestFrameworkExamples.cs;region=TemporaryContainers_XunitAssemblyFixtureRegistration;format=md-fenced-code-block;dedent=true -->
```csharp
[assembly: AssemblyFixture(typeof(PostgreSqlFixture))]
```
<!-- endref -->

Every test class that takes it in its constructor gets the same instance:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/TestFrameworkExamples.cs;region=TemporaryContainers_XunitAssemblyFixture;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class InvoiceRepositoryTests(PostgreSqlFixture database)
{
    [Fact]
    public void SaveInvoice()
    {
        var connectionString = database.Container.GetConnectionString();
        Assert.StartsWith("Host=127.0.0.1;", connectionString, StringComparison.Ordinal); // open a connection and test the repository instead
    }
}
```
<!-- endref -->

## See the container logs in the test output

When a test fails, what the container printed often explains why. Forward the logs to the output of the test with the
`XUnitLogger` of the `Meziantou.Extensions.Logging.Xunit.v3` package, as in the
[container per test](#a-container-per-test) example above:

```bash
dotnet add package Meziantou.Extensions.Logging.Xunit.v3
```

`XUnitLogger.CreateLogger()` writes to the output of the current test. A fixture runs outside of any test, so there is
no test output to write to and the lines are dropped: give a fixture a logger of your own. Once the test is over, its
output rejects new lines and the forwarding stops; the container is not affected. See
[Forward the logs to a logger](05-WorkingWithContainers.md#forward-the-logs-to-a-logger).

## Remove the leftovers before the tests

A test run that was killed leaves its containers behind. Remove them when the next run starts, in an assembly fixture:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/TestFrameworkExamples.cs;region=TemporaryContainers_XunitCleanupFixture;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class ContainerCleanupFixture : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        // Removes what the earlier runs left behind when they were killed
        await ContainerRuntime.Auto.CleanupAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```
<!-- endref -->

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/TestFrameworkExamples.cs;region=TemporaryContainers_XunitCleanupFixtureRegistration;format=md-fenced-code-block;dedent=true -->
```csharp
[assembly: AssemblyFixture(typeof(ContainerCleanupFixture))]
```
<!-- endref -->

The cleanup never removes the containers of the current process, so the other assembly fixtures can start theirs at the
same time. See [Cleaning up](07-Cleanup.md).

## Skip the tests when there is no runtime

To let the tests run on a machine without a container runtime, skip the ones that need it:

<!-- ref:../../samples/Meziantou.Framework.TemporaryContainers.Samples/TestFrameworkExamples.cs;region=TemporaryContainers_XunitSkipWithoutRuntime;format=md-fenced-code-block;dedent=true -->
```csharp
[Fact]
public async Task RequiresAContainerRuntime()
{
    Assert.SkipUnless(await ContainerRuntime.Auto.IsSupportedAsync(TestContext.Current.CancellationToken), "No container runtime is available");

    await using var container = ContainerDefinition.CreateRedis().CreateContainer();
    await container.StartAsync(TestContext.Current.CancellationToken);
}
```
<!-- endref -->

On a build agent that is expected to run them, prefer to let them fail: a skipped test hides a broken agent.

## Run the tests on a build agent

- **Install and start a runtime on the agent.** The library detects it as on a developer machine. See
  [Container runtimes](06-Runtimes.md); set the runtime explicitly when the agent has several.
- **Pin the image tags** so a new release of an image cannot break the build, and pull from a mirror when your agents
  hit the rate limit of a public registry.
- **Give the registry credentials** with the command line of the runtime (`docker login`), or in the `config.json` file
  of the `DOCKER_CONFIG` directory for the Docker Engine API runtime. See
  [Private registries](04-Configuration/02-Images.md#private-registries).
- **Remove the leftovers.** On an agent that is reused between builds, call `CleanupAsync` when the tests start, and
  start a [reaper](07-Cleanup.md#remove-the-containers-as-soon-as-the-process-dies) so that a build that is canceled
  or times out removes its containers right away.
- **Allow for slower machines.** A build agent shared with other jobs starts containers more slowly than a developer
  machine: raise `StartupTimeout` rather than adding delays.
- **Limit the number of containers at once.** Each container takes memory and CPU. When the tests start many
  containers, share them with fixtures or run the test classes that start them one at a time.
- **Keep the logs.** Forward the container logs to the test output, so a failure on the agent can be diagnosed from the
  test report.
