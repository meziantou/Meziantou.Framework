# Meziantou.Framework.SingleInstance

Library to help implementing applications that must only have a single instance.

Works on Windows, Linux, and macOS. The instance is scoped to the current user, so two users of the same machine can each run their own instance. Notifying the first instance uses a named pipe, which is backed by a Unix domain socket on Linux and macOS.

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
