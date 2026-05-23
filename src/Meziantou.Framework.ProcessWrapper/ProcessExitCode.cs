using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>Represents a process exit code.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct ProcessExitCode(int value) : IEquatable<ProcessExitCode>
{
    /// <summary>Gets the numeric exit code value.</summary>
    public int Value { get; } = value;

    /// <summary>Gets a value indicating whether the process exited successfully.</summary>
    public bool IsSuccess => Value is 0;

    /// <summary>
    /// Determines whether the exit code matches any of the expected values.
    /// </summary>
    /// <param name="expectedValues">The expected exit code values.</param>
    /// <returns><see langword="true"/> if the exit code matches any of the expected values; otherwise, <see langword="false"/>.</returns>
    public bool IsAnyOf(params ReadOnlySpan<int> expectedValues)
    {
        foreach (var expectedValue in expectedValues)
        {
            if (Value == expectedValue)
                return true;
        }

        return false;
    }

    /// <inheritdoc />
    public bool Equals(ProcessExitCode other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ProcessExitCode other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public static explicit operator ProcessExitCode(int value) => new(value);
    public static implicit operator int(ProcessExitCode exitCode) => exitCode.Value;
    public static bool operator ==(ProcessExitCode left, ProcessExitCode right) => left.Equals(right);
    public static bool operator !=(ProcessExitCode left, ProcessExitCode right) => !(left == right);
}
