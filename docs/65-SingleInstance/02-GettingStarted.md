---
title: Getting started
description: Install the package, detect the first instance of the application and notify it from the next ones.
---

## Install the package

Add the `Meziantou.Framework.SingleInstance` package to a project that targets .NET 10 or later:

```bash
dotnet add package Meziantou.Framework.SingleInstance
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;`.

## Make an application single-instance

The `Program.cs` of a console application:

<!-- ref:../../samples/SingleInstanceConsoleAppSample/Program.cs;region=SingleInstance_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Generate a unique Guid for the application
var applicationId = new Guid("e5d38b35-b275-47f4-8aa6-bcee6361aae9");

using var singleInstance = new SingleInstance(applicationId);

// Subscribe before calling StartApplication. The server starts inside StartApplication,
// so a notification arriving before the handler is attached would be dropped.
singleInstance.NewInstance += (sender, e) =>
{
    // e.Arguments contains the arguments of the other instance
    Console.WriteLine("New instance " + e.ProcessId.ToString(CultureInfo.InvariantCulture));
    foreach (var arg in e.Arguments)
    {
        Console.WriteLine(arg);
    }
};

if (singleInstance.StartApplication())
{
    // This is the first instance of the application
    Console.WriteLine("Waiting for other instances");
    Console.ReadLine();
}
else
{
    // Notify the first instance, then exit
    singleInstance.NotifyFirstInstance(args);
}
```
<!-- endref -->

- The application id is a GUID that identifies the application. Every process of the application must use the same
  one, and no other application may use it: generate it once, for instance with `[guid]::NewGuid()` in PowerShell or
  `uuidgen`, and write it in the code.
- Subscribe to `NewInstance` before calling `StartApplication`. `StartApplication` starts listening for
  notifications, so a notification that arrives before the handler is attached is lost.
- `StartApplication` returns `true` in the first instance. The application runs as usual and receives the arguments of
  the next instances in `NewInstance`.
- In the other instances, `StartApplication` returns `false`. `NotifyFirstInstance` sends the arguments to the first
  instance, then the process exits.
- Keep the `SingleInstance` object alive, and do not dispose it, for as long as the application runs. Disposing it stops
  the notifications.

## Detect a running instance only

When the first instance does not need the arguments of the next ones, set `StartServer` to `false`: `StartApplication`
then only tells whether another instance is running, and no notification server is started.

<!-- ref:../../samples/SingleInstanceConsoleAppSample/SingleInstanceExamples.cs;region=SingleInstance_DetectOnly;format=md-fenced-code-block;dedent=true -->
```csharp
using var singleInstance = new SingleInstance(new Guid("2a9a8c1f-61d5-4b7e-8f0e-5c4d3b2a1908"))
{
    // Do not listen for notifications from other instances
    StartServer = false,
};

if (!singleInstance.StartApplication())
{
    Console.Error.WriteLine("The application is already running.");
    return 1;
}

// Run the application
return 0;
```
<!-- endref -->

An instance started with `StartServer = false` cannot receive notifications: `NotifyFirstInstance` returns `false` in
the next instances.

## Next steps

- [Receiving notifications](03-ReceivingNotifications.md): the content of the notification, the thread it is raised
  on, and what happens when the first instance does not answer.
- [How it works](04-HowItWorks.md): the scope of an instance and the lifetime of the `SingleInstance` object.
