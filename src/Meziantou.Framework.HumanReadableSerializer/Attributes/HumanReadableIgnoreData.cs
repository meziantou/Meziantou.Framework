namespace Meziantou.Framework.HumanReadable;

/// <summary>Represents data used when evaluating custom ignore conditions.</summary>
public readonly struct HumanReadableIgnoreData
{
    internal HumanReadableIgnoreData(object? value, Exception? exception)
    {
        Value = value;
        Exception = exception;
    }

    /// <summary>Gets the value being serialized.</summary>
    public object? Value { get; }

    /// <summary>Gets the exception that occurred when getting the value, if any.</summary>
    public Exception? Exception { get; }
}
