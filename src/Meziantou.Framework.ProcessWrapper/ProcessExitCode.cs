using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>Represents a process exit code.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct ProcessExitCode : IEquatable<ProcessExitCode>
{
    public ProcessExitCode(int value)
    {
        Value = value;
    }

    /// <summary>Gets the numeric exit code value.</summary>
    public int Value { get; }

    /// <summary>Gets a value indicating whether the process exited successfully.</summary>
    public bool IsSuccess => Value == 0;

    public bool Equals(ProcessExitCode other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is ProcessExitCode other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(ProcessExitCode exitCode) => exitCode.Value;
    public static bool operator ==(ProcessExitCode left, ProcessExitCode right) => left.Equals(right);
    public static bool operator !=(ProcessExitCode left, ProcessExitCode right) => !(left == right);
}
