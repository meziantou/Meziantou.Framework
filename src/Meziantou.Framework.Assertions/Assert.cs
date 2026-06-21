using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public static partial class Assert
{
    internal static AssertionFormatter ErrorFormatter
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = AssertionFormatter.Default;

    [Obsolete("This is an override of Object.Equals(). Use Assert.Equal() instead.", true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static new bool Equals(object? a, object? b) => throw new InvalidOperationException("Assert.Equals should not be used");

    [Obsolete("This is an override of Object.ReferenceEquals(). Use Assert.Same() instead.", true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static new bool ReferenceEquals(object? a, object? b) => throw new InvalidOperationException("Assert.ReferenceEquals should not be used");


    // TODO move to another file
    /// <summary>
    /// Fails the assertion with the specified message.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    public static void Fail(string? message = null)
    {
        throw new AssertionException(ErrorFormatter.Format(new FailAssertionError(message)));
    }
}

internal readonly ref struct FailAssertionError(string? message)
{
    public string? Message { get; } = message;
}