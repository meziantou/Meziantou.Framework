# Meziantou.Framework.SingleInstance

Library to help implementing applications that must only have a single instance.

Ensuring a single instance is supported on every operating system. Notifying the first instance uses a named pipe and is only supported on Windows: elsewhere `StartServer` defaults to `false` and `NotifyFirstInstance` returns `false`.

````c#
// Generate a unique Guid for the application
var applicationId = new Guid("dfae4e70-179f-4726-aa98-00a832315f5a");

using var singleInstance = new SingleInstance(applicationId);
if (singleInstance.StartApplication())
{
    // This is the first instance of the application

    // Handle the case where another instance is started and use NotifyFirstInstance
    singleInstance.NewInstance += (sender, e) =>
    {
        // TODO logic
        // Can use e.Arguments to get arguments from the other instance
    };
}
else
{
    // Notify the other instance
    // The 
    singleInstance.NotifyFirstInstance(args);
}
````
