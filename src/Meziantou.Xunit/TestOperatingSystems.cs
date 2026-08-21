namespace Meziantou.Xunit;

/// <summary>
/// Describes the operating systems a test requires.
/// </summary>
[Flags]
public enum TestOperatingSystems
{
    /// <summary>
    /// The operating system is not part of the condition.
    /// </summary>
    None = 0,

    /// <summary>
    /// Windows.
    /// </summary>
    Windows = 1,

    /// <summary>
    /// Linux.
    /// </summary>
    Linux = 2,

    /// <summary>
    /// macOS.
    /// </summary>
    MacOS = 4,

    /// <summary>
    /// Windows, Linux or macOS.
    /// </summary>
    All = Windows | Linux | MacOS,
}
