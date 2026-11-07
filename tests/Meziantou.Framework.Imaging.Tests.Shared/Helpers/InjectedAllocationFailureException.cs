namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Simulates an allocation failure injected through <c>SlabPool.RentFailureInjector</c>.</summary>
public sealed class InjectedAllocationFailureException : Exception
{
    public InjectedAllocationFailureException()
        : base("Injected allocation failure.")
    {
    }

    public InjectedAllocationFailureException(string message)
        : base(message)
    {
    }

    public InjectedAllocationFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
