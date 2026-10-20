# Meziantou.Framework.SingleInstance

Library to help implementing applications that must only have a single instance.

Works on Windows, Linux, and macOS. The instance is scoped to the current user, so two users of the same machine can each run their own instance. Notifying the first instance uses a named pipe, which is backed by a Unix domain socket on Linux and macOS.

The `Program.cs` of a console application (`SingleInstance` is in the `Meziantou.Framework` namespace):

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
