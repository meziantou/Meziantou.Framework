namespace Meziantou.Framework;

/// <summary>Specifies which process command details are included in log-oriented text outputs.</summary>
[Flags]
public enum ProcessLogVerbosity
{
    /// <summary>No process command details are included.</summary>
    None = 0,

    /// <summary>Includes the resolved executable path.</summary>
    IncludeProcessPath = 1,

    /// <summary>Includes process arguments.</summary>
    IncludeArguments = 2,
}
