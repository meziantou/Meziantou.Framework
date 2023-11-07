# Meziantou.Framework.SingleInstance

Library to help implementing applications that must only have a single instance.

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
