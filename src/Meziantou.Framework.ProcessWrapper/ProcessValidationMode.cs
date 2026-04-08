namespace Meziantou.Framework;

/// <summary>Specifies process validation rules to apply after execution.</summary>
[Flags]
public enum ProcessValidationMode
{
    /// <summary>No validation is performed.</summary>
    None = 0,

    /// <summary>Throws <see cref="ProcessExecutionException"/> if the exit code is not zero.</summary>
    FailIfNonZeroExitCode = 1,

    /// <summary>Throws <see cref="ProcessExecutionException"/> if any text is written to standard error.</summary>
    FailIfStdError = 2,
}
