using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;

[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.Fuzz.FuzzColor))]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.Fuzz.FuzzPermission))]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.Fuzz.FuzzLarge))]

namespace Meziantou.Framework.FastEnumGenerator.Fuzz;

/// <summary>
/// The intercepted <c>Enum.Parse&lt;TEnum&gt;</c> and <c>Enum.TryParse&lt;TEnum&gt;</c> calls, and the generated
/// <c>TEnum.Parse</c> and <c>TEnum.TryParse</c> members, agree with the reflection-based <see cref="Enum.Parse(Type, string, bool)"/>
/// and <see cref="Enum.TryParse(Type, string?, bool, out object?)"/>, which are not intercepted: same result, and for the
/// intercepted calls the same exception. A parsed value formats back to a text that parses to the same value.
/// </summary>
public static partial class FastEnumGeneratorFuzzTargets
{
    private static readonly string[] Tokens =
    [
        "Red", "red", "RED", "Green", "Blue", "Purple", "Zero", "Alias", "None", "Read", "Write", "Execute", "ReadWrite", "All", "read access",
        "Min", "Mid", "Max", ",", ", ", " ", "\t", "\n", "\u00A0", "\u2028", "+", "-", "0", "1", "2", "3", "7", "8", "255", "256", "-3",
        "9223372036854775808", "18446744073709551615", "18446744073709551616", "\u0660", "\uFF11", "\u0130", "\u212A",
    ];

    private delegate T ParseDelegate<T>(string value, bool ignoreCase);

    private delegate bool TryParseDelegate<T>(string value, bool ignoreCase, out T result);

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("fast-enum-parse", Parse) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// The first byte selects the enum and <c>ignoreCase</c>, the rest is the text to parse.
    /// </summary>
    private static void Parse(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var text = FuzzText.Decode(data);
        var ignoreCase = (options & 0x80) != 0;
        switch ((options & 0x7F) % 3)
        {
            case 0:
                CheckColor(text, ignoreCase);
                break;
            case 1:
                CheckPermission(text, ignoreCase);
                break;
            default:
                CheckLarge(text, ignoreCase);
                break;
        }
    }

#pragma warning disable RS0030 // Banned API -- the overloads without ignoreCase are intercepted too
    private static void CheckColor(string text, bool ignoreCase)
    {
        Check(text, ignoreCase,
            intercepted: [(value, ic) => Enum.Parse<FuzzColor>(value, ic), (value, ic) => Enum.Parse<FuzzColor>(value.AsSpan(), ic)],
            interceptedCaseSensitive: [(value, _) => Enum.Parse<FuzzColor>(value), (value, _) => Enum.Parse<FuzzColor>(value.AsSpan())],
            interceptedTry: [(string value, bool ic, out FuzzColor result) => Enum.TryParse(value, ic, out result), (string value, bool ic, out FuzzColor result) => Enum.TryParse(value.AsSpan(), ic, out result)],
            interceptedTryCaseSensitive: [(string value, bool _, out FuzzColor result) => Enum.TryParse(value, out result), (string value, bool _, out FuzzColor result) => Enum.TryParse(value.AsSpan(), out result)],
            generated: [(value, ic) => FuzzColor.Parse(value, ic), (value, ic) => FuzzColor.Parse(value.AsSpan(), ic)],
            generatedTry: [(string value, bool ic, out FuzzColor result) => FuzzColor.TryParse(value, ic, out result), (string value, bool ic, out FuzzColor result) => FuzzColor.TryParse(value.AsSpan(), ic, out result)],
            format: value => value.ToString());
    }

    private static void CheckPermission(string text, bool ignoreCase)
    {
        Check(text, ignoreCase,
            intercepted: [(value, ic) => Enum.Parse<FuzzPermission>(value, ic), (value, ic) => Enum.Parse<FuzzPermission>(value.AsSpan(), ic)],
            interceptedCaseSensitive: [(value, _) => Enum.Parse<FuzzPermission>(value), (value, _) => Enum.Parse<FuzzPermission>(value.AsSpan())],
            interceptedTry: [(string value, bool ic, out FuzzPermission result) => Enum.TryParse(value, ic, out result), (string value, bool ic, out FuzzPermission result) => Enum.TryParse(value.AsSpan(), ic, out result)],
            interceptedTryCaseSensitive: [(string value, bool _, out FuzzPermission result) => Enum.TryParse(value, out result), (string value, bool _, out FuzzPermission result) => Enum.TryParse(value.AsSpan(), out result)],
            generated: [(value, ic) => FuzzPermission.Parse(value, ic), (value, ic) => FuzzPermission.Parse(value.AsSpan(), ic)],
            generatedTry: [(string value, bool ic, out FuzzPermission result) => FuzzPermission.TryParse(value, ic, out result), (string value, bool ic, out FuzzPermission result) => FuzzPermission.TryParse(value.AsSpan(), ic, out result)],
            format: value => value.ToString());
    }

    private static void CheckLarge(string text, bool ignoreCase)
    {
        Check(text, ignoreCase,
            intercepted: [(value, ic) => Enum.Parse<FuzzLarge>(value, ic), (value, ic) => Enum.Parse<FuzzLarge>(value.AsSpan(), ic)],
            interceptedCaseSensitive: [(value, _) => Enum.Parse<FuzzLarge>(value), (value, _) => Enum.Parse<FuzzLarge>(value.AsSpan())],
            interceptedTry: [(string value, bool ic, out FuzzLarge result) => Enum.TryParse(value, ic, out result), (string value, bool ic, out FuzzLarge result) => Enum.TryParse(value.AsSpan(), ic, out result)],
            interceptedTryCaseSensitive: [(string value, bool _, out FuzzLarge result) => Enum.TryParse(value, out result), (string value, bool _, out FuzzLarge result) => Enum.TryParse(value.AsSpan(), out result)],
            generated: [(value, ic) => FuzzLarge.Parse(value, ic), (value, ic) => FuzzLarge.Parse(value.AsSpan(), ic)],
            generatedTry: [(string value, bool ic, out FuzzLarge result) => FuzzLarge.TryParse(value, ic, out result), (string value, bool ic, out FuzzLarge result) => FuzzLarge.TryParse(value.AsSpan(), ic, out result)],
            format: value => value.ToString());
    }
#pragma warning restore RS0030

    private static void Check<T>(
        string text,
        bool ignoreCase,
        ParseDelegate<T>[] intercepted,
        ParseDelegate<T>[] interceptedCaseSensitive,
        TryParseDelegate<T>[] interceptedTry,
        TryParseDelegate<T>[] interceptedTryCaseSensitive,
        ParseDelegate<T>[] generated,
        TryParseDelegate<T>[] generatedTry,
        Func<T, string> format)
        where T : struct, Enum
    {
        var expectedSucceeded = Enum.TryParse(typeof(T), text, ignoreCase, out var boxed);
        var expected = expectedSucceeded ? (T)boxed! : default;
        var expectedException = GetParseException(() => _ = Enum.Parse(typeof(T), text, ignoreCase));
        FuzzAssert.Equal(expectedSucceeded, expectedException is null, $"Enum.Parse and Enum.TryParse disagree on {FuzzAssert.Format(text)}");

        foreach (var parse in ignoreCase ? intercepted : [.. intercepted, .. interceptedCaseSensitive])
        {
            var actual = default(T);
            var exception = GetParseException(() => actual = parse(text, ignoreCase));
            FuzzAssert.Equal(expectedException?.GetType(), exception?.GetType(), $"The intercepted Enum.Parse<{typeof(T).Name}> throws another exception for {FuzzAssert.Format(text)} (ignoreCase: {ignoreCase})");
            FuzzAssert.Equal(expectedException?.Message, exception?.Message, $"The intercepted Enum.Parse<{typeof(T).Name}> throws another message for {FuzzAssert.Format(text)} (ignoreCase: {ignoreCase})");
            FuzzAssert.Equal(expected, actual, $"The intercepted Enum.Parse<{typeof(T).Name}> returns another value for {FuzzAssert.Format(text)} (ignoreCase: {ignoreCase})");
        }

        foreach (var tryParse in ignoreCase ? interceptedTry : [.. interceptedTry, .. interceptedTryCaseSensitive])
        {
            var succeeded = tryParse(text, ignoreCase, out var actual);
            FuzzAssert.Equal(expectedSucceeded, succeeded, $"The intercepted Enum.TryParse<{typeof(T).Name}> disagrees on {FuzzAssert.Format(text)} (ignoreCase: {ignoreCase})");
            FuzzAssert.Equal(expected, actual, $"The intercepted Enum.TryParse<{typeof(T).Name}> returns another value for {FuzzAssert.Format(text)} (ignoreCase: {ignoreCase})");
        }

        // The generated members throw the same exception types, with their own message.
        foreach (var parse in generated)
        {
            var actual = default(T);
            var exception = GetParseException(() => actual = parse(text, ignoreCase));
            FuzzAssert.Equal(expectedException?.GetType(), exception?.GetType(), $"{typeof(T).Name}.Parse throws another exception for {FuzzAssert.Format(text)} (ignoreCase: {ignoreCase})");
            FuzzAssert.Equal(expected, actual, $"{typeof(T).Name}.Parse returns another value for {FuzzAssert.Format(text)} (ignoreCase: {ignoreCase})");
        }

        foreach (var tryParse in generatedTry)
        {
            var succeeded = tryParse(text, ignoreCase, out var actual);
            FuzzAssert.Equal(expectedSucceeded, succeeded, $"{typeof(T).Name}.TryParse disagrees on {FuzzAssert.Format(text)} (ignoreCase: {ignoreCase})");
            FuzzAssert.Equal(expected, actual, $"{typeof(T).Name}.TryParse returns another value for {FuzzAssert.Format(text)} (ignoreCase: {ignoreCase})");
        }

        if (!expectedSucceeded)
            return;

        // The intercepted ToString formats like Enum.ToString, and its text parses back to the same value.
        var formatted = format(expected);
        FuzzAssert.Equal(((Enum)boxed!).ToString(), formatted, $"The intercepted ToString differs from Enum.ToString for {FuzzAssert.Format(text)}");
        FuzzAssert.True(interceptedTry[0](formatted, false, out var roundTripped), $"{FuzzAssert.Format(formatted)}, the text of a parsed value, does not parse");
        FuzzAssert.Equal(expected, roundTripped, $"{FuzzAssert.Format(formatted)} does not parse back to the value it was formatted from");
    }

    private static Exception? GetParseException(Action parse)
    {
        try
        {
            parse();
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return exception;
        }
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        var index = 0;
        foreach (var text in Tokens.Concat(GetTestStrings()).Distinct(StringComparer.Ordinal))
        {
            // Every enum, with and without ignoreCase
            foreach (var options in (byte[])[0x00, 0x01, 0x02, 0x80, 0x81, 0x82])
            {
                yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"parse-{index++:D4}"), [options], text);
            }
        }
    }

    private static IEnumerable<string> GetTestStrings()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in (string[])["FastEnumGeneratedBehaviorTests.cs", "FastEnumInterceptorTests.cs"])
        {
            var source = FuzzResources.ReadText(typeof(FastEnumGeneratorFuzzTargets).Assembly, name);
            foreach (Match match in StringLiteralRegex().Matches(source))
            {
                var value = Unescape(match.Groups["value"].Value);
                if (result.Add(value))
                    yield return value;
            }
        }
    }

    private static string Unescape(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                result.Append(value[i]);
                continue;
            }

            i++;
            switch (value[i])
            {
                case 'n':
                    result.Append('\n');
                    break;
                case 'r':
                    result.Append('\r');
                    break;
                case 't':
                    result.Append('\t');
                    break;
                case '0':
                    result.Append('\0');
                    break;
                case 'u' when i + 4 < value.Length:
                    result.Append((char)int.Parse(value.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default:
                    result.Append(value[i]);
                    break;
            }
        }

        return result.ToString();
    }

    [GeneratedRegex(@"(?<![@$])""(?<value>(?:[^""\\\r\n]|\\.)*)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringLiteralRegex();
}
