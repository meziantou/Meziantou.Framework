using Meziantou.Framework.Win32;

// The readme example (src/Meziantou.Framework.Win32.RestartManager/readme.md) shuts down the applications that lock a
// file, so it is compiled in CI but not run. Run it with:
// dotnet run --project samples/Meziantou.Framework.Win32.RestartManager.Samples -- <path>
if (args is not [var path])
{
    Console.Error.WriteLine("Usage: <path of a locked file>");
    return 1;
}

ShutdownAndRestart(path);
return 0;

static void ShutdownAndRestart(string path)
{
    #region RestartManager_ShutdownAndRestart
    using var session = RestartManager.CreateSession();
    session.RegisterFile(path);

    // A non-zero reboot reason means shutting applications down cannot free the resources,
    // and that a system restart is required instead. Check it before shutting anything down.
    if (session.IsResourcesLocked() && session.RebootReason != RestartManagerRebootReason.None)
    {
        Console.WriteLine($"A system restart is required: {session.RebootReason}");
        return;
    }

    // Shutdown applications with options
    session.Shutdown(RestartManagerShutdownType.ForceShutdown);

    // Or with progress callback
    session.Shutdown(RestartManagerShutdownType.ForceShutdown, percentComplete =>
    {
        Console.WriteLine($"Shutdown progress: {percentComplete}%");
    });

    // Restart applications after shutdown
    session.Restart(percentComplete =>
    {
        Console.WriteLine($"Restart progress: {percentComplete}%");
    });
    #endregion
}
