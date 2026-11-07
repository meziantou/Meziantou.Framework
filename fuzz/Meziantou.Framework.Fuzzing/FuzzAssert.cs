using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Fuzzing;

/// <summary>Checks the properties of the code under test. A violated property throws <see cref="FuzzAssertionException"/>, which libFuzzer reports as a crash.</summary>
public static class FuzzAssert
{
    public static void True([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
            throw new FuzzAssertionException(message);
    }

    public static void False([DoesNotReturnIf(true)] bool condition, string message)
    {
        if (condition)
            throw new FuzzAssertionException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new FuzzAssertionException($"{message}{Environment.NewLine}Expected: {Format(expected)}{Environment.NewLine}Actual:   {Format(actual)}");
    }

    [DoesNotReturn]
    public static void Fail(string message) => throw new FuzzAssertionException(message);

    /// <summary>Formats a value for a failure message; strings are quoted and their control characters and surrogates escaped.</summary>
    public static string Format(object? value) => value switch
    {
        null => "null",
        string text => FuzzText.Escape(text),
        _ => value.ToString() ?? "",
    };
}
