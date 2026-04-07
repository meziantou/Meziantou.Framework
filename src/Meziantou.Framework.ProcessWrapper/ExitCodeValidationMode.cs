namespace Meziantou.Framework;

/// <summary>Specifies how the process exit code should be validated.</summary>
public enum ExitCodeValidationMode
{
    /// <summary>No validation is performed on the exit code.</summary>
    None,

    /// <summary>Throws <see cref="ProcessExecutionException"/> if the exit code is not zero.</summary>
    FailIfNotZero,
}
