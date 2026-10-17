namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>Thrown when a required external tool is missing, has an unexpected version, or lacks a required capability.</summary>
public sealed class ExternalToolUnavailableException : Exception
{
    public ExternalToolUnavailableException()
    {
    }

    public ExternalToolUnavailableException(string message)
        : base(message)
    {
    }

    public ExternalToolUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
