using System.Text;
using Meziantou.Framework.WebPlatform.Encodings;

namespace Meziantou.Framework.WebPlatform.Urls;

/// <summary>application/x-www-form-urlencoded: https://url.spec.whatwg.org/#application/x-www-form-urlencoded</summary>
internal static class FormUrlEncoded
{
    /// <summary>https://url.spec.whatwg.org/#concept-urlencoded-string-parser</summary>
    public static List<KeyValuePair<string, string>> ParseString(string input) => Parse(Encoding.UTF8.GetBytes(input));

    /// <summary>https://url.spec.whatwg.org/#concept-urlencoded-parser</summary>
    public static List<KeyValuePair<string, string>> Parse(ReadOnlySpan<byte> input)
    {
        var output = new List<KeyValuePair<string, string>>();
        foreach (var range in input.Split((byte)'&'))
        {
            var bytes = input[range];
            if (bytes.IsEmpty)
                continue;

            ReadOnlySpan<byte> name;
            ReadOnlySpan<byte> value;
            var equals = bytes.IndexOf((byte)'=');
            if (equals >= 0)
            {
                name = bytes[..equals];
                value = bytes[(equals + 1)..];
            }
            else
            {
                name = bytes;
                value = default;
            }

            output.Add(new(Decode(name), Decode(value)));
        }

        return output;

        static string Decode(ReadOnlySpan<byte> value)
        {
            var copy = value.ToArray();
            copy.AsSpan().Replace((byte)'+', (byte)' ');
            return WebEncodingRegistry.CreateDecoder(WebEncodingId.Utf8).DecodeToString(PercentEncoding.PercentDecode(copy));
        }
    }

    /// <summary>https://url.spec.whatwg.org/#concept-urlencoded-serializer</summary>
    public static string Serialize(IEnumerable<KeyValuePair<string, string>> tuples, WebEncodingId encoding = WebEncodingId.Utf8)
    {
        encoding = WebEncodingRegistry.GetOutputEncoding(encoding);
        var output = new StringBuilder();
        foreach (var (name, value) in tuples)
        {
            if (output.Length > 0)
            {
                output.Append('&');
            }

            PercentEncoding.AppendPercentEncodeAfterEncoding(output, encoding, name, PercentEncodeSet.FormUrlEncoded);
            output.Append('=');
            PercentEncoding.AppendPercentEncodeAfterEncoding(output, encoding, value, PercentEncodeSet.FormUrlEncoded);
        }

        return output.ToString();
    }
}
