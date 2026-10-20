using Meziantou.Framework;

namespace SingleInstanceConsoleAppSample;

public static class SingleInstanceExamples
{
    public static void BasicUsage(string[] args)
    {
        #region SingleInstance_BasicUsage
        var applicationId = new Guid("dfae4e70-179f-4726-aa98-00a832315f5a");
        using var singleInstance = new SingleInstance(applicationId);

        // Subscribe before calling StartApplication, otherwise a notification raised
        // between the server starting and the handler being attached is dropped.
        singleInstance.NewInstance += (sender, e) =>
        {
            // Handle notification from another instance
            Console.WriteLine($"New instance started with {e.Arguments.Length} arguments");
        };

        if (singleInstance.StartApplication())
        {
            // This is the first instance
        }
        else
        {
            // Notify the first instance
            singleInstance.NotifyFirstInstance(args);
        }
        #endregion
    }
}
