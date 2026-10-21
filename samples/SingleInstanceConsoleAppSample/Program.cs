using Meziantou.Framework;

#region SingleInstance_Usage
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
#endregion
