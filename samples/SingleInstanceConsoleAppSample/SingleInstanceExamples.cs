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

    public static void UiThread()
    {
        #region SingleInstance_UiThread
        // Run this code on the UI thread (WPF, Windows Forms, ...), for instance when the application starts
        var uiContext = SynchronizationContext.Current;

        using var singleInstance = new SingleInstance(new Guid("7b0c3c5e-4f7a-4d8e-9a43-0f3f6f1d2c11"));
        singleInstance.NewInstance += (sender, e) =>
        {
            // NewInstance is raised on a thread-pool thread: post the work to the UI thread
            uiContext?.Post(_ => OpenFiles(e.Arguments), state: null);
        };

        void OpenFiles(string[] files)
        {
            // Update the UI: open the files, bring the main window to the front, ...
            foreach (var file in files)
            {
                Console.WriteLine("Opening " + file);
            }
        }
        #endregion
    }

    public static int DetectOnly()
    {
        #region SingleInstance_DetectOnly
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
        #endregion
    }
}
