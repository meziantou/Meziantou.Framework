namespace Meziantou.Framework;

/// <summary>
/// Detects test runners that run the tests in the background while the code is being edited, where a merge tool
/// popping up on every failure would get in the way. Mirrors the detection of DiffEngine
/// (https://github.com/VerifyTests/DiffEngine/blob/main/src/DiffEngine/ContinuousTestingDetector.cs).
/// </summary>
internal static class ContinuousTestingDetector
{
    // An explicit static constructor, so the loaded assemblies are inspected when the detection is first used, not
    // whenever the runtime decides to initialize the type.
    static ContinuousTestingDetector()
    {
        Detected = IsLiveUnitTesting() || IsNCrunchBackgroundRun();
    }

    public static bool Detected { get; }

    // Visual Studio Live Unit Testing loads its runtime into the test process.
    private static bool IsLiveUnitTesting()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.FullName?.StartsWith("Microsoft.CodeAnalysis.LiveUnitTesting.Runtime", StringComparison.Ordinal) is true)
                return true;
        }

        return false;
    }

    // NCrunch runs the tests continuously, but a test the developer runs explicitly is run at high priority, and
    // that run is interactive.
    private static bool IsNCrunchBackgroundRun()
    {
        if (Environment.GetEnvironmentVariable("NCRUNCH") is null)
            return false;

        return Environment.GetEnvironmentVariable("NCrunch.IsHighPriority") is not "1";
    }
}
