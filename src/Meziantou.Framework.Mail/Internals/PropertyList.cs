using System.Globalization;
using System.Text;
using System.Xml;

namespace Meziantou.Framework.Mail.Internals;

/// <summary>Reads and writes the XML property lists of Apple (plist): <c>dict</c>, <c>array</c>, <c>string</c>, <c>integer</c>, <c>real</c>, <c>true</c>, <c>false</c>, <c>date</c> and <c>data</c>.</summary>
internal static class PropertyList
{
    private const int MaxDepth = 64;

    /// <summary>Reads the root dictionary of a property list.</summary>
    /// <exception cref="InvalidMailDataException">The XML is malformed, or its root is not a dictionary.</exception>
    public static Dictionary<string, object?> ReadDictionary(ReadOnlySpan<byte> data)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            MaxCharactersFromEntities = 0,
        };

        try
        {
            using var reader = XmlReader.Create(new MemoryStream(data.ToArray()), settings);
            reader.MoveToContent();
            if (reader.LocalName == "plist")
            {
                reader.Read();
                reader.MoveToContent();
            }

            return ReadValue(reader, depth: 0) as Dictionary<string, object?> ?? throw new InvalidMailDataException("The root of the property list is not a dictionary.");
        }
        catch (XmlException ex)
        {
            throw new InvalidMailDataException("The property list is not valid XML.", ex);
        }
    }

    public static void Write(Stream stream, IEnumerable<KeyValuePair<string, object?>> dictionary)
    {
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), Indent = true, IndentChars = "\t", NewLineChars = "\n" };
        using var writer = XmlWriter.Create(stream, settings);
        writer.WriteStartDocument();
        writer.WriteDocType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", subset: null);
        writer.WriteStartElement("plist");
        writer.WriteAttributeString("version", "1.0");
        WriteValue(writer, dictionary.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();
        stream.WriteByte((byte)'\n');
    }

    private static object? ReadValue(XmlReader reader, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidMailDataException("The property list is nested too deeply.");
        }

        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new InvalidMailDataException("The property list has an unexpected node.");
        }

        var name = reader.LocalName;
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return name switch
            {
                "dict" => new Dictionary<string, object?>(StringComparer.Ordinal),
                "array" => new List<object?>(),
                "true" => true,
                "false" => false,
                "string" => "",
                "data" => Array.Empty<byte>(),
                _ => null,
            };
        }

        switch (name)
        {
            case "dict":
                {
                    var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                    reader.Read();
                    while (reader.MoveToContent() == XmlNodeType.Element)
                    {
                        if (reader.LocalName != "key")
                        {
                            throw new InvalidMailDataException("A dictionary of the property list has a value without a key.");
                        }

                        var key = reader.ReadElementContentAsString();
                        reader.MoveToContent();
                        result[key] = ReadValue(reader, depth + 1);
                    }

                    reader.Read();
                    return result;
                }

            case "array":
                {
                    var result = new List<object?>();
                    reader.Read();
                    while (reader.MoveToContent() == XmlNodeType.Element)
                    {
                        result.Add(ReadValue(reader, depth + 1));
                    }

                    reader.Read();
                    return result;
                }

            default:
                {
                    var text = reader.ReadElementContentAsString();
                    return name switch
                    {
                        "string" or "key" => text,
                        "integer" => long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer) ? integer : null,
                        "real" => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var real) ? real : null,
                        "date" => DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null,
                        "data" => DecodeBase64(text),
                        "true" => true,
                        "false" => false,
                        _ => text,
                    };
                }
        }
    }

    private static byte[]? DecodeBase64(string text)
    {
        try
        {
            return Convert.FromBase64String(string.Concat(text.Where(c => !char.IsWhiteSpace(c))));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static void WriteValue(XmlWriter writer, object? value)
    {
        switch (value)
        {
            case IDictionary<string, object?> dictionary:
                writer.WriteStartElement("dict");
                foreach (var (key, item) in dictionary.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteElementString("key", key);
                    WriteValue(writer, item);
                }

                writer.WriteEndElement();
                break;
            case string text:
                writer.WriteElementString("string", text);
                break;
            case bool boolean:
                writer.WriteStartElement(boolean ? "true" : "false");
                writer.WriteEndElement();
                break;
            case byte[] bytes:
                writer.WriteElementString("data", Convert.ToBase64String(bytes));
                break;
            case DateTimeOffset date:
                writer.WriteElementString("date", date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                break;
            case double or float or decimal:
                writer.WriteElementString("real", Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
                break;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                writer.WriteElementString("integer", Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            case System.Collections.IEnumerable items:
                writer.WriteStartElement("array");
                foreach (var item in items)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndElement();
                break;
            default:
                writer.WriteElementString("string", Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                break;
        }
    }
}
