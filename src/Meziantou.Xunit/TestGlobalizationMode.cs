namespace Meziantou.Xunit;

/// <summary>
/// Describes the globalization mode a test requires.
/// </summary>
/// <remarks>
/// Invariant globalization mode is enabled by the <c>InvariantGlobalization</c> MSBuild property or the
/// <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT</c> environment variable. In this mode the culture data of the
/// operating system is not used, so culture-sensitive operations behave as if only the invariant culture existed.
/// </remarks>
public enum TestGlobalizationMode
{
    /// <summary>
    /// The globalization mode is not part of the condition.
    /// </summary>
    Any = 0,

    /// <summary>
    /// The application runs in invariant globalization mode.
    /// </summary>
    Invariant = 1,

    /// <summary>
    /// The application does not run in invariant globalization mode, so the culture data of the operating system is available.
    /// </summary>
    NotInvariant = 2,
}
