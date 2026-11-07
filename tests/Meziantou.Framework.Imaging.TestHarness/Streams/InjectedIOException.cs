namespace Meziantou.Framework.Imaging.TestHarness.Streams;

/// <summary>The I/O failure injected by <see cref="TestInputStream"/>; the library must propagate it unchanged.</summary>
public sealed class InjectedIOException : IOException
{
    public InjectedIOException()
        : base("Injected I/O failure.")
    {
    }

    public InjectedIOException(string message)
        : base(message)
    {
    }

    public InjectedIOException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public InjectedIOException(long position)
        : base(string.Create(CultureInfo.InvariantCulture, $"Injected I/O failure at position {position}."))
    {
    }
}
