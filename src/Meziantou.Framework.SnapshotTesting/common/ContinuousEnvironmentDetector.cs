namespace Meziantou.Framework;

/// <summary>
/// Combines the detection of the environments where a snapshot must not be updated: a continuous integration server,
/// a continuous testing runner, and an LLM agent.
/// </summary>
internal static class ContinuousEnvironmentDetector
{
    /// <summary>Replaces the detection of the current process. Only meant for tests.</summary>
    internal static Func<string?>? DescriptionOverride { get; set; }

    /// <summary>
    /// Describes the environment that was detected, such as <c>a continuous integration server (CI)</c>, or
    /// returns <see langword="null" /> when the process does not run in such an environment.
    /// </summary>
    public static string? GetDetectedEnvironmentDescription()
    {
        if (DescriptionOverride is { } descriptionOverride)
            return descriptionOverride();

        List<string>? parts = null;
        if (BuildServerDetector.DetectedVariable is { } variable)
        {
            (parts ??= []).Add($"a continuous integration server ({variable})");
        }

        if (ContinuousTestingDetector.Detected)
        {
            (parts ??= []).Add("a continuous testing runner (NCrunch or ReSharper)");
        }

        if (LLMEnvironmentDetector.Detected)
        {
            (parts ??= []).Add($"an LLM agent ({string.Join(", ", LLMEnvironmentDetector.DetectedContexts)})");
        }

        return parts is null ? null : string.Join(" and ", parts);
    }

    /// <summary>
    /// Reads the default value of <c>AutoDetectContinuousEnvironment</c> from an environment variable. Detection is
    /// enabled unless the variable is set to <c>false</c>, <c>0</c>, <c>no</c> or <c>off</c>.
    /// </summary>
    public static bool IsAutoDetectionEnabled(string environmentVariableName)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariableName)?.Trim();
        return !(value is "0" ||
            string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "off", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Explains why a snapshot was not updated, and how to allow updates in this environment.</summary>
    public static string FormatUpdatesDisabledMessage(string environmentDescription, string environmentVariableName, string settingsTypeName)
    {
        return $"Snapshot updates are disabled because {environmentDescription} was detected. " +
            $"To allow updates in this environment, set the {environmentVariableName} environment variable to false, or set {settingsTypeName}.AutoDetectContinuousEnvironment to false.";
    }
}
