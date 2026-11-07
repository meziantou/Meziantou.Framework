namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>A violated fuzz property. <see cref="Signature"/> identifies the defect (minimization keeps it unchanged).</summary>
internal sealed record FuzzFailure(string Signature, string Message)
{
    public static FuzzFailure Property(string name, string message) => new("property:" + name, message);

    /// <summary>An undocumented exception: the signature is the operation, the exception type and the first library frame.</summary>
    public static FuzzFailure FromException(string operation, Exception exception)
    {
        var frame = exception.StackTrace?.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Contains("Meziantou.Framework.Imaging.", StringComparison.Ordinal) && !line.Contains(".Tests.", StringComparison.Ordinal) && !line.Contains("FuzzTests", StringComparison.Ordinal)) ?? "?";
        var at = frame.IndexOf(" in ", StringComparison.Ordinal);
        if (at >= 0)
        {
            frame = frame[..at];
        }

        return new FuzzFailure($"exception:{operation}:{exception.GetType().FullName}:{frame}", $"{operation} threw an undocumented {exception.GetType().FullName}: {exception}");
    }
}
