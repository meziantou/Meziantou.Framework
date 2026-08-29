# Meziantou.Framework.SingleInstance

Library to help implementing applications that must only have a single instance.

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
