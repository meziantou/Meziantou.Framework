namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>The result of an external process execution.</summary>
/// <param name="ExitCode">The exit code.</param>
/// <param name="StandardOutput">The raw standard output bytes.</param>
/// <param name="StandardError">The standard error text.</param>
public sealed record ProcessResult(int ExitCode, ReadOnlyMemory<byte> StandardOutput, string StandardError)
{
    /// <summary>Gets the standard output decoded as UTF-8.</summary>
    public string StandardOutputText => Encoding.UTF8.GetString(StandardOutput.Span);
}
