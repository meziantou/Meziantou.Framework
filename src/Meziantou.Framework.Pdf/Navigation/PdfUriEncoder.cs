using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Writes a URI as the 7-bit ASCII text of a URI action (ISO 32000-2, 12.6.4.8): an internationalized host is written in
/// its ASCII form (the <c>xn--</c> labels of IDNA, RFC 3492 Punycode), and the other non-ASCII characters are
/// percent-encoded as UTF-8 (RFC 3987, 3.1). The conversion does not depend on the globalization data of the system.
/// </summary>
internal static class PdfUriEncoder
{
    private const int Base = 36;
    private const int TMin = 1;
    private const int TMax = 26;
    private const int Skew = 38;
    private const int Damp = 700;
    private const int InitialBias = 72;
    private const int InitialN = 128;

    public static string ToAscii(Uri uri)
    {
        var text = uri.AbsoluteUri;
        if (Ascii.IsValid(text))
            return text;

        var result = new StringBuilder(text.Length + 16);
        var host = uri.Host;
        if (uri.HostNameType == UriHostNameType.Dns && !Ascii.IsValid(host) && text.Contains("://", StringComparison.Ordinal))
        {
            result.Append(uri.Scheme).Append("://");
            if (uri.UserInfo.Length > 0)
            {
                result.Append(uri.GetComponents(UriComponents.UserInfo, UriFormat.UriEscaped)).Append('@');
            }

            result.Append(ToAsciiHost(host));
            if (!uri.IsDefaultPort)
            {
                result.Append(':').Append(uri.Port.ToString(CultureInfo.InvariantCulture));
            }

            text = uri.GetComponents(UriComponents.PathAndQuery | UriComponents.Fragment, UriFormat.UriEscaped);
        }

        // What is left (a path, the address of a mailto URI) is percent-encoded
        Span<byte> utf8 = stackalloc byte[4];
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.IsAscii)
            {
                result.Append((char)rune.Value);
                continue;
            }

            var length = rune.EncodeToUtf8(utf8);
            for (var i = 0; i < length; i++)
            {
                result.Append('%').Append(utf8[i].ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return result.ToString();
    }

    /// <summary>Converts the labels of a host that have non-ASCII characters to their ASCII form, <c>xn--</c> followed by their Punycode encoding.</summary>
    internal static string ToAsciiHost(string host)
    {
        var labels = host.Split('.');
        for (var i = 0; i < labels.Length; i++)
        {
            if (!Ascii.IsValid(labels[i]))
            {
                labels[i] = "xn--" + EncodePunycode(labels[i].ToLowerInvariant());
            }
        }

        return string.Join('.', labels);
    }

    /// <summary>Encodes a label with the Punycode algorithm of RFC 3492 (6.3).</summary>
    internal static string EncodePunycode(string input)
    {
        var codePoints = new List<int>(input.Length);
        foreach (var rune in input.EnumerateRunes())
        {
            codePoints.Add(rune.Value);
        }

        var output = new StringBuilder();
        foreach (var codePoint in codePoints)
        {
            if (codePoint < 0x80)
            {
                output.Append((char)codePoint);
            }
        }

        var basicCount = output.Length;
        var handled = basicCount;
        if (basicCount > 0)
        {
            output.Append('-');
        }

        var n = InitialN;
        var delta = 0L;
        var bias = InitialBias;
        while (handled < codePoints.Count)
        {
            var m = int.MaxValue;
            foreach (var codePoint in codePoints)
            {
                if (codePoint >= n && codePoint < m)
                {
                    m = codePoint;
                }
            }

            delta += (long)(m - n) * (handled + 1);
            n = m;
            foreach (var codePoint in codePoints)
            {
                if (codePoint < n)
                {
                    delta++;
                }

                if (codePoint == n)
                {
                    var q = delta;
                    for (var k = Base; ; k += Base)
                    {
                        var t = k <= bias ? TMin : k >= bias + TMax ? TMax : k - bias;
                        if (q < t)
                            break;

                        output.Append(Digit((int)(t + ((q - t) % (Base - t)))));
                        q = (q - t) / (Base - t);
                    }

                    output.Append(Digit((int)q));
                    bias = Adapt(delta, handled + 1, handled == basicCount);
                    delta = 0;
                    handled++;
                }
            }

            delta++;
            n++;
        }

        return output.ToString();
    }

    private static char Digit(int value) => (char)(value < 26 ? 'a' + value : '0' + value - 26);

    private static int Adapt(long delta, int count, bool first)
    {
        delta = first ? delta / Damp : delta / 2;
        delta += delta / count;
        var k = 0;
        while (delta > ((Base - TMin) * TMax) / 2)
        {
            delta /= Base - TMin;
            k += Base;
        }

        return (int)(k + (((Base - TMin + 1) * delta) / (delta + Skew)));
    }
}
