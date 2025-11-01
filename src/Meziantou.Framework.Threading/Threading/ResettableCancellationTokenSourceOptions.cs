namespace Meziantou.Framework.Threading;

/// <summary>Specifies options for a <see cref="ResettableCancellationTokenSource"/>.</summary>
[Flags]
public enum ResettableCancellationTokenSourceOptions
{
    /// <summary>No special behavior.</summary>
    None = 0x0,

    /// <summary>Cancel the token when <see cref="ResettableCancellationTokenSource.Reset"/> is called.</summary>
    CancelOnReset = 0x1,

    /// <summary>Cancel the token when the <see cref="ResettableCancellationTokenSource"/> is disposed.</summary>
    CancelOnDispose = 0x2,
}
