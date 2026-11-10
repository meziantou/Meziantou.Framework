using System.Text;

namespace Meziantou.Framework.WebPlatform.Infra;

// The algorithms of atob and btoa (https://html.spec.whatwg.org/multipage/webappapis.html#atob), without the bindings: the
// global scope of WebPlatform and the web APIs of Meziantou.Framework.JavaScriptEngine.WebApis (which links this file)
// wrap them. The failures are reported to the caller, which throws its own exception type.
internal static class ForgivingBase64
{
    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#dom-btoa: returns null when the string contains a character
    /// outside of the Latin-1 range.
    /// </summary>
    public static string? Btoa(string data)
    {
        var bytes = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] > 0xFF)
                return null;

            bytes[i] = (byte)data[i];
        }

        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#dom-atob: returns null when the string is not valid
    /// forgiving-base64.
    /// </summary>
    public static string? Atob(string data)
    {
        var bytes = Decode(data);
        if (bytes is null)
            return null;

        return string.Create(bytes.Length, bytes, (span, bytes) =>
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                span[i] = (char)bytes[i];
            }
        });
    }

    /// <summary>https://infra.spec.whatwg.org/#forgiving-base64-decode</summary>
    public static byte[]? Decode(string data)
    {
        var builder = new StringBuilder(data.Length);
        foreach (var c in data)
        {
            if (!InfraAscii.IsAsciiWhitespace(c))
            {
                builder.Append(c);
            }
        }

        if (builder.Length % 4 == 0 && builder.Length > 0)
        {
            if (builder[^1] == '=')
            {
                builder.Length--;
                if (builder[^1] == '=')
                {
                    builder.Length--;
                }
            }
        }

        if (builder.Length % 4 == 1)
            return null;

        var bits = 0;
        var bitCount = 0;
        var output = new List<byte>(builder.Length * 3 / 4);
        for (var i = 0; i < builder.Length; i++)
        {
            var c = builder[i];
            int value;
            if (c is >= 'A' and <= 'Z')
            {
                value = c - 'A';
            }
            else if (c is >= 'a' and <= 'z')
            {
                value = c - 'a' + 26;
            }
            else if (c is >= '0' and <= '9')
            {
                value = c - '0' + 52;
            }
            else if (c == '+')
            {
                value = 62;
            }
            else if (c == '/')
            {
                value = 63;
            }
            else
            {
                return null;
            }

            bits = (bits << 6) | value;
            bitCount += 6;
            if (bitCount >= 8)
            {
                bitCount -= 8;
                output.Add((byte)((bits >> bitCount) & 0xFF));
            }
        }

        return [.. output];
    }
}
