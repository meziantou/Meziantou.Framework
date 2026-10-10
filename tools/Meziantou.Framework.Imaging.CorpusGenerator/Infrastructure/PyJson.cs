using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

/// <summary>
/// The manifest's JSON dialect: objects are insertion-ordered <see cref="Obj"/> dictionaries, written with a two-space
/// indent, ", "-free separators, unescaped non-ASCII characters and shortest round-trip floats ("1.0", "1e-05"), exactly
/// like Python's <c>json.dumps(value, indent=2, ensure_ascii=False)</c>.
/// </summary>
internal static class PyJson
{
    public static string Dumps(object? value)
    {
        var sb = new StringBuilder();
        Write(sb, value, 0);
        return sb.ToString();
    }

    /// <summary>Parses JSON into <see cref="Obj"/>, <see cref="List{T}"/> of object, string, long (or BigInteger),
    /// double, bool and null.</summary>
    public static object? Loads(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Convert(document.RootElement);
    }

    private static object? Convert(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new Obj();
                foreach (var property in element.EnumerateObject())
                    obj[property.Name] = Convert(property.Value);
                return obj;
            case JsonValueKind.Array:
                return element.EnumerateArray().Select(Convert).ToList();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                var raw = element.GetRawText();
                if (raw.AsSpan().IndexOfAny(".eE") >= 0)
                    return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                    return integer;
                return BigInteger.Parse(raw, CultureInfo.InvariantCulture);
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null;
            default:
                throw new InvalidOperationException("Unexpected JSON value " + element.ValueKind);
        }
    }

    private static void Write(StringBuilder sb, object? value, int level)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                return;
            case bool b:
                sb.Append(b ? "true" : "false");
                return;
            case string s:
                WriteString(sb, s);
                return;
            case double d:
                sb.Append(FormatFloat(d));
                return;
            case float f:
                sb.Append(FormatFloat(f));
                return;
            case byte or sbyte or short or ushort or int or uint or long or ulong or BigInteger:
                sb.Append(System.Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            case Px px:
                WriteArray(sb, Enumerable.Range(0, px.Count).Select(i => (object?)px[i]).ToList(), level);
                return;
            case IDictionary dictionary:
                WriteObject(sb, dictionary, level);
                return;
            case System.Runtime.CompilerServices.ITuple tuple:
                WriteArray(sb, Enumerable.Range(0, tuple.Length).Select(i => tuple[i]).ToList(), level);
                return;
            case IEnumerable enumerable:
                WriteArray(sb, enumerable.Cast<object?>().ToList(), level);
                return;
            default:
                throw new InvalidOperationException("Value of type " + value.GetType() + " is not JSON serializable");
        }
    }

    private static string FormatFloat(double value)
    {
        if (double.IsNaN(value))
            return "NaN";
        if (double.IsPositiveInfinity(value))
            return "Infinity";
        if (double.IsNegativeInfinity(value))
            return "-Infinity";
        return Py.Repr(value);
    }

    private static void WriteObject(StringBuilder sb, IDictionary dictionary, int level)
    {
        if (dictionary.Count == 0)
        {
            sb.Append("{}");
            return;
        }

        sb.Append('{');
        var first = true;
        foreach (DictionaryEntry entry in dictionary)
        {
            if (!first)
                sb.Append(',');
            first = false;
            NewLine(sb, level + 1);
            WriteString(sb, (string)entry.Key);
            sb.Append(": ");
            Write(sb, entry.Value, level + 1);
        }

        NewLine(sb, level);
        sb.Append('}');
    }

    private static void WriteArray(StringBuilder sb, List<object?> items, int level)
    {
        if (items.Count == 0)
        {
            sb.Append("[]");
            return;
        }

        sb.Append('[');
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            NewLine(sb, level + 1);
            Write(sb, items[i], level + 1);
        }

        NewLine(sb, level);
        sb.Append(']');
    }

    private static void NewLine(StringBuilder sb, int level) => sb.Append('\n').Append(' ', 2 * level);

    private static void WriteString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
    }
}
