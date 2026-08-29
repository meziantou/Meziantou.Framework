# Meziantou.Framework.SingleInstance

Library to help implementing applications that must only have a single instance.

Ensuring a single instance is supported on every operating system. Notifying the first instance uses a named pipe and is only supported on Windows: elsewhere `StartServer` defaults to `false` and `NotifyFirstInstance` returns `false`.

````c#
// Generate a unique Guid for the application
var applicationId = new Guid("dfae4e70-179f-4726-aa98-00a832315f5a");

using var singleInstance = new SingleInstance(applicationId);

// Subscribe before calling StartApplication. The server starts inside StartApplication,
// so a notification arriving before the handler is attached would be dropped.
singleInstance.NewInstance += (sender, e) =>
{
    // TODO logic
    // Can use e.Arguments to get arguments from the other instance
};

if (singleInstance.StartApplication())
{
    // This is the first instance of the application
}
else
{
    // Notify the first instance, then exit
    singleInstance.NotifyFirstInstance(args);
}
````
