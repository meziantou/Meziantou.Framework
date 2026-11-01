---
title: Wait strategies
description: Decide when StartAsync considers a container ready, with the built-in strategies or a check of your own.
---

A container is started long before the service inside it accepts connections. Wait strategies tell [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) what
"ready" means: it starts the container, then runs them, and only returns once they have all completed.

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_WaitStrategies;format=md-fenced-code-block;dedent=true -->
```csharp
definition.WaitStrategies.Add(Wait.ForLogMessage("Now listening on")); // a log line contains the text
definition.WaitStrategies.Add(Wait.ForLogMessage(new Regex(@"ready in \d+ ms", RegexOptions.None, TimeSpan.FromSeconds(1)), occurrences: 2));
definition.WaitStrategies.Add(Wait.ForPort(8080)); // a TCP connection succeeds on the published port
definition.WaitStrategies.Add(Wait.ForDelay(TimeSpan.FromSeconds(1)));
definition.StartupTimeout = TimeSpan.FromMinutes(2); // 5 minutes by default
```
<!-- endref -->

| Strategy | Completes when |
| --- | --- |
| `Wait.ForLogMessage(text, occurrences)` | The given number of log lines contain the text. |
| `Wait.ForLogMessage(regex, occurrences)` | The given number of log lines match the regular expression. |
| `Wait.ForPort(containerPort)` | A TCP connection to the published host port, on the [host address](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.GetHostAddress) of the port, reaches a listening service. |
| `Wait.ForDelay(delay)` | The delay has elapsed. |

- The strategies run one after the other, in the order you added them, so combine them: a log message says that the
  service started, an open port that it accepts connections.
- Without any strategy, [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) returns as soon as the container is started.
- Prefer a log message or a port to a delay: a delay is either too long on a fast machine or too short on a loaded build
  agent.

## How the built-in strategies behave

- **Log messages of the current run only.** A container that was stopped and started again keeps the logs of its earlier
  runs; those are counted and skipped, so the ready message of the earlier run does not make the container look ready.
- **A container that exits fails fast.** When the container stops while a log message strategy waits, [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) throws
  an `InvalidOperationException` that gives the exit code and the last lines the container printed, without waiting
  for the timeout.
- **A port is open when a service listens.** The port forwarders of the runtimes accept connections before the service
  listens; [ForPort](cref:Meziantou.Framework.TemporaryContainers.Wait.ForPort) only completes once the connection reaches the service.
- **Counting occurrences.** Some images start the service twice, for instance a temporary server that initializes the
  data, then the real one: count the occurrences of the message, or combine it with [ForPort](cref:Meziantou.Framework.TemporaryContainers.Wait.ForPort).

## Timeout

The strategies have [StartupTimeout](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.StartupTimeout) to complete, 5 minutes by default. Past it, [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) throws a
`TimeoutException` whose message names the strategy that did not complete, the state of the container, and the last
lines it logged. The time to pull the image is not part of it: the timeout starts once the container is started.

[StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) also takes a `CancellationToken`, which stops it whatever stage it is at.

## Write your own strategy

Implement [IWaitStrategy](cref:Meziantou.Framework.TemporaryContainers.IWaitStrategy) for a check the built-in strategies cannot do, such as a health endpoint:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_CustomWaitStrategy;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class HttpHealthWaitStrategy(int containerPort, string path) : IWaitStrategy
{
    public async Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken)
    {
        var url = new UriBuilder("http", container.GetHostAddress(containerPort), container.GetMappedPort(containerPort), path).Uri;
        using var httpClient = new HttpClient();
        while (true)
        {
            try
            {
                using var response = await httpClient.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // Not listening yet
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    // Shown in the TimeoutException when the container is not ready in time
    public override string ToString() => "HTTP health check " + path;
}
```
<!-- endref -->

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_UseCustomWaitStrategy;format=md-fenced-code-block;dedent=true -->
```csharp
definition.Ports.Add(8080);
definition.WaitStrategies.Add(new HttpHealthWaitStrategy(8080, "/health"));
```
<!-- endref -->

- [WaitAsync](cref:Meziantou.Framework.TemporaryContainers.IWaitStrategy.WaitAsync) returns once the container is ready. Loop until then, and pass the `CancellationToken` to every call: it
  is canceled when [StartupTimeout](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.StartupTimeout) elapses, which [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) reports as a `TimeoutException`.
- The `TimeoutException` names the strategy with its `ToString()`: override it with a description that helps when a
  test fails.
- An exception thrown by [WaitAsync](cref:Meziantou.Framework.TemporaryContainers.IWaitStrategy.WaitAsync) stops [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) at once, with that exception.

## Wait again

[WaitUntilReadyAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.WaitUntilReadyAsync) runs the wait strategies of a container that is already started, with the same timeout. Call it
after [RestartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.RestartAsync), which does not run them. See [Working with a container](../05-WorkingWithContainers.md).
