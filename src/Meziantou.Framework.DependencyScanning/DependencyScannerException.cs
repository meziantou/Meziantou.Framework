namespace Meziantou.Framework.DependencyScanning;

/// <summary>Represents errors that occur during dependency scanning operations.</summary>
public class DependencyScannerException : Exception
{
    public DependencyScannerException()
    {
    }

    public DependencyScannerException(string? message) : base(message)
    {
    }

    public DependencyScannerException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}
