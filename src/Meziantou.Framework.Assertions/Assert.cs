using System.ComponentModel;
using System.Diagnostics;

namespace Meziantou.Framework.Assertions;

// The assertion methods are not where a failure is: hiding their frames makes the stack trace of an AssertionException
// start at the line of the test that called the assertion.
[StackTraceHidden]
public static partial class Assert
{
    internal static AssertionFormatter ErrorFormatter
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = AssertionFormatter.Default;

    /// <summary>Gets or sets the options used to format assertion failure messages.</summary>
    /// <remarks>
    /// These options are shared by the whole process. To change the options for a single test without affecting tests
    /// running in parallel, use <see cref="UseFormatterOptions(FormatterOptions)"/> instead.
    /// </remarks>
    public static FormatterOptions FormatterOptions
    {
        get => ErrorFormatter.Options;
        set => ErrorFormatter.Options = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Uses the specified options to format assertion failure messages in the current asynchronous flow, until the returned scope is disposed.</summary>
    /// <param name="options">The options to use instead of <see cref="FormatterOptions"/>.</param>
    /// <returns>A scope that restores the previous options when disposed.</returns>
    /// <remarks>
    /// The options flow to the code called by the current method, including asynchronous continuations and tasks started from it,
    /// but not to other tests running in parallel. Scopes can be nested.
    /// </remarks>
    public static IDisposable UseFormatterOptions(FormatterOptions options)
    {
        return AssertionFormatter.UseOptions(options);
    }

    [Obsolete("This is an override of Object.Equals(). Use Assert.Equal() instead.", true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static new bool Equals(object? a, object? b) => throw new InvalidOperationException("Assert.Equals should not be used");

    [Obsolete("This is an override of Object.ReferenceEquals(). Use Assert.Same() instead.", true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static new bool ReferenceEquals(object? a, object? b) => throw new InvalidOperationException("Assert.ReferenceEquals should not be used");


    /// <summary>Fails the assertion with the specified message.</summary>
    /// <param name="message">The message that describes the failure.</param>
    [DoesNotReturn]
    public static void Fail(string? message = null)
    {
        throw new AssertionException(ErrorFormatter.Format(new FailAssertionError(message)));
    }
}
