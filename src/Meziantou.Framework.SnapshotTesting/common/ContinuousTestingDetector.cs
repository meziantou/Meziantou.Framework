namespace Meziantou.Framework;

/// <summary>
/// Detects test runners that run the tests from an IDE or in the background, where the developer does not expect a
/// merge tool to pop up on every failure.
/// </summary>
internal static class ContinuousTestingDetector
{
    // An explicit static constructor, so the loaded assemblies are inspected when the detection is first used, not
    // whenever the runtime decides to initialize the type.
    static ContinuousTestingDetector()
    {
        Detected = HasEnvironmentVariable("NCrunch")
            || HasEnvironmentVariable("RESHARPER_UNIT_TEST_RUNNER")
            || HasEnvironmentVariable("JetBrains.ReSharper.TaskRunner.CLR45.x64")
            || IsLiveUnitTesting();
    }

    public static bool Detected { get; }

    private static bool HasEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name) is not null;

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
}
