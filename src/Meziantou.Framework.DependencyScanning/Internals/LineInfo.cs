using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meziantou.Framework.DependencyScanning.Internals;

[StructLayout(LayoutKind.Auto)]
internal readonly struct LineInfo : IEquatable<LineInfo>
{
    public LineInfo(int lineNumber, int linePosition)
    {
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    public int LineNumber { get; }
    public int LinePosition { get; }

    public static LineInfo FromJToken(JToken token)
    {
        var propertyLineInfo = (IJsonLineInfo)token;
        if (propertyLineInfo.HasLineInfo())
        {
            if (token.Type == JTokenType.String)
            {
                return new LineInfo(propertyLineInfo.LineNumber, propertyLineInfo.LinePosition - ((string)token!).Length);
            }

            return new LineInfo(propertyLineInfo.LineNumber, propertyLineInfo.LinePosition);
        }

        return default;
    }

    public static LineInfo FromXObject(XObject obj)
    {
        var lineInfo = (IXmlLineInfo)obj;
        if (lineInfo.HasLineInfo())
            return new LineInfo(lineInfo.LineNumber, lineInfo.LinePosition);

        return default;
    }

    public override bool Equals(object? obj)
    {
        return obj is LineInfo info && Equals(info);
    }

    public bool Equals(LineInfo other)
    {
        return LineNumber == other.LineNumber &&
               LinePosition == other.LinePosition;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(LineNumber, LinePosition);
    }

    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{LineNumber},{LinePosition}");
    }

    public static bool operator ==(LineInfo left, LineInfo right) => left.Equals(right);
    public static bool operator !=(LineInfo left, LineInfo right) => !(left == right);
}
