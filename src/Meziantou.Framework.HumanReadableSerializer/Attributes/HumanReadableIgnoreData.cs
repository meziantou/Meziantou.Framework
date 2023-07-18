namespace Meziantou.Framework.HumanReadable;

public readonly struct HumanReadableIgnoreData
{
    internal HumanReadableIgnoreData(object? value, Exception? exception)
    {
        Value = value;
        Exception = exception;
    }

    public object? Value { get; }
    public Exception? Exception { get; }
}
