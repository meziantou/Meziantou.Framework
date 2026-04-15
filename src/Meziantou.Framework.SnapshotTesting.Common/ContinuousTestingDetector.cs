namespace Meziantou.Framework;

public static class ContinuousTestingDetector
{
    private static bool HasEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name) is not null;

    public static bool Detected { get; } =
        HasEnvironmentVariable("NCrunch")
        || HasEnvironmentVariable("RESHARPER_UNIT_TEST_RUNNER")
        || HasEnvironmentVariable("JetBrains.ReSharper.TaskRunner.CLR45.x64");
}
