using System.Collections;
using System.Globalization;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The conversions of the values of a script engine that a .NET API reads itself, when their Web IDL type is <c>any</c> (e.g.
/// the descriptions of the <see cref="CanvasFilter"/> objects): the values are the ones the bindings pass to .NET (the values
/// of the engine, the strings and the platform objects) and the ones these operations return. The values that are not
/// values of the engine are .NET values (see <see cref="DotNetScriptValueOperations"/>).
/// </summary>
internal interface IScriptValueOperations
{
    /// <summary>Whether the value is <c>undefined</c> (a missing value) or <c>null</c>.</summary>
    bool IsNullOrUndefined(object? value);

    /// <summary>Whether the value is an object (not a primitive value).</summary>
    bool IsObject(object? value);

    /// <summary>
    /// Returns the elements of an iterable object (https://webidl.spec.whatwg.org/#js-sequence), or null when the value is not
    /// an object or has no iterator (as the conversion to a union that has a sequence type checks it).
    /// </summary>
    List<object?>? TryToSequence(object? value);

    /// <summary>
    /// Returns the entries of a record (https://webidl.spec.whatwg.org/#js-record): the enumerable own properties of an object,
    /// or no entry for <c>null</c> and <c>undefined</c>.
    /// </summary>
    /// <exception cref="TypeErrorException">The value is not an object.</exception>
    List<KeyValuePair<string, object?>> ToRecord(object? value);

    /// <summary>https://tc39.es/ecma262/#sec-tonumber</summary>
    double ToNumber(object? value);

    /// <summary>https://tc39.es/ecma262/#sec-toboolean</summary>
    bool ToBoolean(object? value);

    /// <summary>https://webidl.spec.whatwg.org/#js-DOMString</summary>
    string ToDomString(object? value);
}

/// <summary>
/// The conversions of the .NET values, used when there is no script engine, and for the values of the .NET APIs: null, the
/// strings, the booleans and the numbers are the JavaScript primitive values, a read-only dictionary of strings is an object
/// (a record), and another enumerable is an iterable object.
/// </summary>
internal sealed class DotNetScriptValueOperations : IScriptValueOperations
{
    public static DotNetScriptValueOperations Instance { get; } = new();

    private DotNetScriptValueOperations()
    {
    }

    public bool IsNullOrUndefined(object? value) => value is null;

    public bool IsObject(object? value) => value is not (null or string or bool) && !(value is IConvertible convertible && IsNumeric(convertible));

    public List<object?>? TryToSequence(object? value) => value switch
    {
        null or string or IReadOnlyDictionary<string, object?> => null,
        IEnumerable enumerable => [.. enumerable.Cast<object?>()],
        _ => null,
    };

    public List<KeyValuePair<string, object?>> ToRecord(object? value) => value switch
    {
        null => [],
        IReadOnlyDictionary<string, object?> dictionary => [.. dictionary],
        IEnumerable<KeyValuePair<string, object?>> entries => [.. entries],
        string or bool or IConvertible => throw new TypeErrorException("The value is not an object"),
        _ => [],
    };

    public double ToNumber(object? value) => value switch
    {
        null => 0,
        bool boolean => boolean ? 1 : 0,
        string text => StringToNumber(text),
        double number => number,
        float number => number,
        decimal number => (double)number,
        IConvertible convertible when IsNumeric(convertible) => convertible.ToDouble(CultureInfo.InvariantCulture),

        // An array is converted to a string (its elements joined by commas), then to a number
        _ when TryToSequence(value) is not null => StringToNumber(ToDomString(value)),
        _ => double.NaN,
    };

    public bool ToBoolean(object? value) => value switch
    {
        null => false,
        bool boolean => boolean,
        string text => text.Length > 0,
        IConvertible convertible when IsNumeric(convertible) => ToNumber(convertible) is var number && number != 0 && !double.IsNaN(number),
        _ => true,
    };

    public string ToDomString(object? value) => ToDomString(value, converting: null);

    /// <summary>
    /// Converts a value to a string; a list is the strings of its elements joined by commas, and a list that contains itself
    /// is converted once (its nested occurrences are empty strings, as <c>Array.prototype.join</c> does).
    /// </summary>
    private string ToDomString(object? value, HashSet<object>? converting) => value switch
    {
        null => "null",
        bool boolean => boolean ? "true" : "false",
        string text => text,
        IConvertible convertible when IsNumeric(convertible) => ToNumber(convertible) switch
        {
            double.PositiveInfinity => "Infinity",
            double.NegativeInfinity => "-Infinity",
            var number => number.ToString("R", CultureInfo.InvariantCulture),
        },
        _ when TryToSequence(value) is { } elements => JoinElements(value, elements, converting ?? new HashSet<object>(ReferenceEqualityComparer.Instance)),
        _ => "[object Object]",
    };

    private string JoinElements(object list, List<object?> elements, HashSet<object> converting)
    {
        if (!converting.Add(list))
            return "";

        var result = string.Join(',', elements.Select(element => element is null ? "" : ToDomString(element, converting)));
        converting.Remove(list);
        return result;
    }

    private static bool IsNumeric(IConvertible value) => value.GetTypeCode() is >= TypeCode.SByte and <= TypeCode.Decimal;

    /// <summary>https://tc39.es/ecma262/#sec-stringtonumber (the decimal literals, <c>Infinity</c> and the hexadecimal, octal and binary integers).</summary>
    private static double StringToNumber(string value)
    {
        // The white spaces and the line terminators of ECMAScript are the white spaces of .NET and U+FEFF (and U+0085 is trimmed)
        var text = value.AsSpan().Trim().Trim('\uFEFF');
        if (text.IsEmpty)
            return 0;

        if (text.Length > 2 && text[0] == '0' && (text[1] | 0x20) is 'x' or 'o' or 'b')
        {
            var radix = (text[1] | 0x20) switch { 'x' => 16, 'o' => 8, _ => 2 };
            var result = 0d;
            foreach (var c in text[2..])
            {
                var digit = c switch
                {
                    >= '0' and <= '9' => c - '0',
                    >= 'a' and <= 'z' => c - 'a' + 10,
                    >= 'A' and <= 'Z' => c - 'A' + 10,
                    _ => int.MaxValue,
                };
                if (digit >= radix)
                    return double.NaN;

                result = (result * radix) + digit;
            }

            return result;
        }

        var unsigned = text[0] is '+' or '-' ? text[1..] : text;
        if (unsigned.SequenceEqual("Infinity"))
            return text[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;

        // The decimal literals: digits, an optional fraction and an optional exponent (no "NaN", no thousands separators)
        foreach (var c in unsigned)
        {
            if (!char.IsAsciiDigit(c) && c is not ('.' or 'e' or 'E' or '+' or '-'))
                return double.NaN;
        }

        return double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var number) ? number : double.NaN;
    }
}
