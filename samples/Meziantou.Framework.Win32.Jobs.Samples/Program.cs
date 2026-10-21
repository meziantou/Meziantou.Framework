using Meziantou.Framework.Win32.Jobs.Samples;

// The readme examples (src/Meziantou.Framework.Win32.Jobs/readme.md) put the current process in a job and restrict
// it, so they are compiled in CI but not run. Run one with:
// dotnet run --project samples/Meziantou.Framework.Win32.Jobs.Samples -- <kill-on-close|limits>
if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240))
{
    Console.Error.WriteLine("Windows 10 or later is required");
    return 1;
}

switch (args)
{
    case ["kill-on-close"]:
        ReadmeExamples.KillChildProcessesOnExit();
        return 0;

    case ["limits"]:
        ReadmeExamples.SetLimits();
        return 0;

    default:
        Console.Error.WriteLine("Usage: <kill-on-close|limits>");
        return 1;
}
