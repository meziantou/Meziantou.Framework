namespace Meziantou.Framework;

/// <summary>Thrown when writing to a <see cref="ProcessPipe"/> whose reader is closed.</summary>
internal sealed class ProcessPipeBrokenException : IOException
{
    public ProcessPipeBrokenException()
        : base("The reader of the pipe is closed.")
    {
    }

    public ProcessPipeBrokenException(string message)
        : base(message)
    {
    }

    public ProcessPipeBrokenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
