using System.Runtime.InteropServices;

namespace Meziantou.Framework.DnsClient.Protocol;

/// <summary>
/// Conversions between the DNS wire format for domain names and the RFC 1035 section 5.1 presentation format.
/// </summary>
/// <remarks>
/// Labels may contain arbitrary octets, including <c>.</c> and bytes outside the printable ASCII range. Escaping them
/// keeps distinct wire names distinct as strings: a single label <c>evil.com</c> becomes <c>evil\.com</c> and cannot be
/// confused with the two-label name <c>evil.com</c>.
/// </remarks>
internal static class DnsName
{
    /// <summary>The maximum length of a domain name in wire format, including length octets and the root label (RFC 1035 section 2.3.4).</summary>
    public const int MaxLength = 255;

    /// <summary>The maximum length of a single label in wire format (RFC 1035 section 2.3.4).</summary>
    public const int MaxLabelLength = 63;

    /// <summary>The maximum number of labels a name can have, given <see cref="MaxLength"/>.</summary>
    public const int MaxLabels = MaxLength / 2;

    /// <summary>Appends a wire-format label to <paramref name="builder"/> in presentation format.</summary>
    public static void AppendLabel(StringBuilder builder, ReadOnlySpan<byte> label)
    {
        foreach (var b in label)
        {
            switch (b)
            {
                case (byte)'.':
                    builder.Append("\\.");
                    break;

                case (byte)'\\':
                    builder.Append("\\\\");
                    break;

                case >= 0x21 and <= 0x7E:
                    builder.Append((char)b);
                    break;

                default:
                    builder.Append('\\')
                        .Append((char)('0' + (b / 100)))
                        .Append((char)('0' + (b / 10 % 10)))
                        .Append((char)('0' + (b % 10)));
                    break;
            }
        }
    }

    /// <summary>Decodes a presentation-format label into wire-format octets, resolving <c>\\</c> escapes.</summary>
    /// <returns>The number of octets written to <paramref name="destination"/>.</returns>
    public static int DecodeLabel(ReadOnlySpan<char> label, Span<byte> destination)
    {
        var count = 0;
        for (var i = 0; i < label.Length; i++)
        {
            var c = label[i];
            byte value;

            if (c is '\\')
            {
                i++;
                if (i >= label.Length)
                    throw new DnsProtocolException("Invalid escape sequence at the end of a domain name label.");

                if (char.IsAsciiDigit(label[i]))
                {
                    if (i + 2 >= label.Length || !char.IsAsciiDigit(label[i + 1]) || !char.IsAsciiDigit(label[i + 2]))
                        throw new DnsProtocolException("A decimal escape sequence in a domain name label must have exactly three digits.");

                    var number = ((label[i] - '0') * 100) + ((label[i + 1] - '0') * 10) + (label[i + 2] - '0');
                    if (number > 255)
                        throw new DnsProtocolException($"Invalid decimal escape sequence '\\{number}' in a domain name label.");

                    value = (byte)number;
                    i += 2;
                }
                else if (label[i] > 0x7F)
                {
                    throw new DnsProtocolException($"Domain name label contains the non-ASCII character '{label[i]}'. Convert the name to punycode first.");
                }
                else
                {
                    value = (byte)label[i];
                }
            }
            else if (c > 0x7F)
            {
                throw new DnsProtocolException($"Domain name label contains the non-ASCII character '{c}'. Convert the name to punycode first.");
            }
            else
            {
                value = (byte)c;
            }

            if (count >= destination.Length)
                throw new DnsProtocolException($"Domain name label exceeds the maximum length of {MaxLabelLength} bytes.");

            destination[count++] = value;
        }

        return count;
    }

    /// <summary>Returns the index of the first <c>.</c> that is not part of an escape sequence, or -1.</summary>
    public static int IndexOfUnescapedDot(ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] is '\\')
            {
                i++;
                continue;
            }

            if (value[i] is '.')
                return i;
        }

        return -1;
    }

    /// <summary>Returns <see langword="true"/> when the name ends with a dot that is not part of an escape sequence.</summary>
    public static bool EndsWithUnescapedDot(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || value[^1] is not '.')
            return false;

        // The dot is escaped when it is preceded by an odd number of backslashes.
        var backslashes = 0;
        for (var i = value.Length - 2; i >= 0 && value[i] is '\\'; i--)
        {
            backslashes++;
        }

        return backslashes % 2 is 0;
    }

    /// <summary>Splits a presentation-format name into its labels, ignoring escaped dots.</summary>
    public static LabelEnumerator EnumerateLabels(ReadOnlySpan<char> name) => new(name);

    [StructLayout(LayoutKind.Auto)]
    public ref struct LabelEnumerator
    {
        private ReadOnlySpan<char> _remaining;

        public LabelEnumerator(ReadOnlySpan<char> name)
        {
            _remaining = name;
            Current = default;
        }

        public ReadOnlySpan<char> Current { get; private set; }

        public readonly LabelEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_remaining.IsEmpty)
                return false;

            var index = IndexOfUnescapedDot(_remaining);
            if (index == -1)
            {
                Current = _remaining;
                _remaining = [];
            }
            else
            {
                Current = _remaining[..index];
                _remaining = _remaining[(index + 1)..];
            }

            return true;
        }
    }
}
