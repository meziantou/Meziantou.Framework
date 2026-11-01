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
definition.WaitStrategies.Add(Wait.ForHttp(8080, "/health")); // an HTTP request gets a 2xx status code
definition.WaitStrategies.Add(Wait.ForCommand("pg_isready", "--host", "127.0.0.1")); // a command run in the container exits with code 0
definition.WaitStrategies.Add(Wait.ForHealthcheck()); // the HEALTHCHECK of the image reports healthy
definition.WaitStrategies.Add(Wait.ForDelay(TimeSpan.FromSeconds(1)));
definition.StartupTimeout = TimeSpan.FromMinutes(2); // 5 minutes by default
```
<!-- endref -->

| Strategy | Completes when |
| --- | --- |
| `Wait.ForLogMessage(text, occurrences)` | The given number of log lines contain the text. |
| `Wait.ForLogMessage(regex, occurrences)` | The given number of log lines match the regular expression. |
| `Wait.ForPort(containerPort)` | A TCP connection to the published host port, on `127.0.0.1`, reaches a listening service. |
| `Wait.ForHttp(containerPort, path, configure)` | An HTTP request to the published host port, on `127.0.0.1`, gets an expected response (any 2xx status code by default). |
| `Wait.ForCommand(command)` | A command run inside the container exits with code 0. It is run again until it does. |
| `Wait.ForHealthcheck(configure)` | The runtime reports the `HEALTHCHECK` of the image as healthy. |
| `Wait.ForAll(strategies)`, `Wait.ForAny(strategies)` | All of the strategies, or the first one, completed. See [Combine strategies](#combine-strategies). |
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
  data, then the real one: count the occurrences of the message, or combine it with [ForPort](cref:Meziantou.Framework.TemporaryContainers.Wait.ForPort). When the number of
  occurrences depends on the state of the data, check the service itself instead, with [ForCommand](cref:Meziantou.Framework.TemporaryContainers.Wait.ForCommand) or [ForHttp](cref:Meziantou.Framework.TemporaryContainers.Wait.ForHttp).
- **A command is checked from inside the container.** [ForCommand](cref:Meziantou.Framework.TemporaryContainers.Wait.ForCommand) runs the command with [ExecAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.ExecAsync), so it can use the tools
  of the image, such as `pg_isready` or `redis-cli ping`, and reach the service on `127.0.0.1` without a published
  port.
- **A health check that cannot succeed fails fast.** [ForHealthcheck](cref:Meziantou.Framework.TemporaryContainers.Wait.ForHealthcheck) throws an `InvalidOperationException` at once when
  the container has no health check (the image defines no `HEALTHCHECK`, or the runtime does not report health, like
  apple/container), when the runtime reports it unhealthy (the message gives the output of the last check), or when
  the container stops. [InspectAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.InspectAsync) returns the status in [ContainerInfo.Health](cref:Meziantou.Framework.TemporaryContainers.ContainerInfo.Health).
- **Polling.** [ForHttp](cref:Meziantou.Framework.TemporaryContainers.Wait.ForHttp), [ForCommand](cref:Meziantou.Framework.TemporaryContainers.Wait.ForCommand) and [ForHealthcheck](cref:Meziantou.Framework.TemporaryContainers.Wait.ForHealthcheck) check again every 250 milliseconds; set
  [PollInterval](cref:Meziantou.Framework.TemporaryContainers.PollingWaitStrategyOptions.PollInterval) to change it.

## HTTP requests

The options of [ForHttp](cref:Meziantou.Framework.TemporaryContainers.Wait.ForHttp) choose the request and the response that means the container is ready:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_HttpWaitStrategy;format=md-fenced-code-block;dedent=true -->
```csharp
definition.Ports.Add(8443);
definition.WaitStrategies.Add(Wait.ForHttp(8443, "/api/status", options =>
{
    options.UseHttps = true;
    options.ValidateServerCertificate = false; // the container serves a self-signed certificate
    options.Method = HttpMethod.Get;
    options.StatusCodes.Add(HttpStatusCode.OK);
    options.StatusCodes.Add(HttpStatusCode.Unauthorized); // the service answers, even without credentials
    options.ResponseBodyPredicate = body => !body.Contains("\"starting\"", StringComparison.Ordinal);
    options.PollInterval = TimeSpan.FromMilliseconds(500);
}));
```
<!-- endref -->

| Option | Default |
| --- | --- |
| [Method](cref:Meziantou.Framework.TemporaryContainers.HttpWaitStrategyOptions.Method) | `GET` |
| [StatusCodes](cref:Meziantou.Framework.TemporaryContainers.HttpWaitStrategyOptions.StatusCodes) | Empty: any 2xx status code. |
| [ResponseBodyPredicate](cref:Meziantou.Framework.TemporaryContainers.HttpWaitStrategyOptions.ResponseBodyPredicate) | None: the body is not read. |
| [UseHttps](cref:Meziantou.Framework.TemporaryContainers.HttpWaitStrategyOptions.UseHttps) | `false` |
| [ValidateServerCertificate](cref:Meziantou.Framework.TemporaryContainers.HttpWaitStrategyOptions.ValidateServerCertificate) | `true`. Set it to `false` for a self-signed certificate. |
| [RequestTimeout](cref:Meziantou.Framework.TemporaryContainers.HttpWaitStrategyOptions.RequestTimeout) | 5 seconds. A request that takes longer counts as a failed check, and the next one is sent. |
| [PollInterval](cref:Meziantou.Framework.TemporaryContainers.PollingWaitStrategyOptions.PollInterval) | 250 milliseconds |

A connection that fails or is reset, and a response that does not match, are not errors: the request is sent again
until it matches or the timeout elapses.

## Combine strategies

[ForAll](cref:Meziantou.Framework.TemporaryContainers.Wait.ForAll) and [ForAny](cref:Meziantou.Framework.TemporaryContainers.Wait.ForAny) run several strategies at the same time, and [WithTimeout](cref:Meziantou.Framework.TemporaryContainers.WaitStrategyExtensions.WithTimeout) gives a single strategy a
timeout of its own:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_ComposeWaitStrategies;format=md-fenced-code-block;dedent=true -->
```csharp
// Both must complete; they run at the same time
definition.WaitStrategies.Add(Wait.ForAll(
    Wait.ForLogMessage("Now listening on"),
    Wait.ForPort(8080)));

// The first one that completes is enough: images with a HEALTHCHECK, and the others
definition.WaitStrategies.Add(Wait.ForAny(
    Wait.ForHealthcheck(),
    Wait.ForHttp(8080, "/health")).WithTimeout(TimeSpan.FromSeconds(30)));
```
<!-- endref -->

- [ForAll](cref:Meziantou.Framework.TemporaryContainers.Wait.ForAll) completes once all the strategies have completed. The first one that fails stops the others, and
  its exception is the one thrown.
- [ForAny](cref:Meziantou.Framework.TemporaryContainers.Wait.ForAny) completes as soon as one strategy completes, and stops the others. A strategy that fails does not
  stop the wait: it fails only when all of them have failed, with an `AggregateException` of their failures (or the
  failure itself, for a single strategy).
- [WithTimeout](cref:Meziantou.Framework.TemporaryContainers.WaitStrategyExtensions.WithTimeout) throws a `TimeoutException` that names the strategy once the timeout elapses. The startup
  timeout of the container still applies, so a timeout longer than it has no effect.

## Timeout

The strategies have [StartupTimeout](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.StartupTimeout) to complete, 5 minutes by default. Past it, [StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) throws a
`TimeoutException` whose message names the strategy that did not complete, the state of the container, and the last
lines it logged. The time to pull the image is not part of it: the timeout starts once the container is started.

[StartAsync](cref:Meziantou.Framework.TemporaryContainers.TemporaryContainer.StartAsync) also takes a `CancellationToken`, which stops it whatever stage it is at.

## Write your own strategy

Implement [IWaitStrategy](cref:Meziantou.Framework.TemporaryContainers.IWaitStrategy) for a check the built-in strategies cannot do. This one is a simpler [ForHttp](cref:Meziantou.Framework.TemporaryContainers.Wait.ForHttp), to show the shape of a strategy:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_CustomWaitStrategy;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class HttpHealthWaitStrategy(int containerPort, string path) : IWaitStrategy
{
    public async Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken)
    {
        var url = new UriBuilder("http", "127.0.0.1", container.GetMappedPort(containerPort), path).Uri;
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
