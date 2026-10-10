namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>A failure injected by <see cref="TestStreamEncoder"/>.</summary>
internal sealed class InjectedEncoderException : Exception
{
    public InjectedEncoderException()
        : base("Injected encoder failure.")
    {
    }

    public InjectedEncoderException(string message)
        : base(message)
    {
    }

    public InjectedEncoderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
