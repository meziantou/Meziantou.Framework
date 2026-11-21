using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email;

/// <summary>An email address and its display name, such as <c>"Alice" &lt;alice@example.com&gt;</c> ([RFC 5322] 3.4).</summary>
public sealed class MailboxAddress : IEquatable<MailboxAddress>
{
    /// <summary>Initializes a new instance of the <see cref="MailboxAddress"/> class.</summary>
    /// <param name="name">The display name, or <see langword="null"/>.</param>
    /// <param name="address">The address, such as <c>alice@example.com</c>.</param>
    public MailboxAddress(string? name, string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Name = string.IsNullOrEmpty(name) ? null : name;
        Address = address;
    }

    /// <summary>Gets the display name, or <see langword="null"/>.</summary>
    public string? Name { get; }

    /// <summary>Gets the address.</summary>
    public string Address { get; }

    /// <summary>Parses a list of addresses, such as the value of a To header. The groups are flattened, and invalid entries are skipped.</summary>
    /// <param name="value">The value, with its encoded words.</param>
    /// <returns>The addresses.</returns>
    public static IReadOnlyList<MailboxAddress> ParseList(string? value)
    {
        var result = new List<MailboxAddress>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return result;
        }

        var tokenizer = new MimeTokenizer(value);
        while (!tokenizer.IsAtEnd)
        {
            tokenizer.SkipCfws();
            if (tokenizer.IsAtEnd)
            {
                break;
            }

            if (tokenizer.Current is ',' or ';')
            {
                tokenizer.Position++;
                continue;
            }

            var start = tokenizer.Position;
            var phrase = tokenizer.ReadPhrase("<,:;");
            if (tokenizer.IsAtEnd || tokenizer.Current is ',' or ';')
            {
                // A bare address: local@domain, possibly with spaces in it
                var address = phrase.Replace(" ", "", StringComparison.Ordinal);
                if (address.Length > 0)
                {
                    // A comment after a bare address is used as its display name, as most clients do: alice@example.com (Alice)
                    result.Add(new MailboxAddress(GetLastComment(value.AsSpan(start, tokenizer.Position - start)), address));
                }

                continue;
            }

            if (tokenizer.Current == ':')
            {
                // A group: the phrase is its name, the mailboxes follow until ';'
                tokenizer.Position++;
                continue;
            }

            // name <address>
            tokenizer.Position++;
            var end = tokenizer.Remaining.IndexOf('>');
            var angle = end < 0 ? tokenizer.Remaining.ToString() : tokenizer.Remaining[..end].ToString();
            tokenizer.Position += end < 0 ? tokenizer.Remaining.Length : end + 1;

            // An obsolete route (@a,@b:local@domain) precedes the address
            var colon = angle.LastIndexOf(':', StringComparison.Ordinal);
            if (colon >= 0 && angle.StartsWith('@', StringComparison.Ordinal))
            {
                angle = angle[(colon + 1)..];
            }

            angle = angle.Trim();
            if (angle.Length > 0 || tokenizer.Position > start)
            {
                result.Add(new MailboxAddress(MimeEncoding.DecodeHeaderValue(phrase), angle));
            }
        }

        return result;
    }

    private static string? GetLastComment(ReadOnlySpan<char> text)
    {
        var end = text.LastIndexOf(')');
        var start = end < 0 ? -1 : text[..end].LastIndexOf('(');
        if (start < 0)
        {
            return null;
        }

        var comment = text[(start + 1)..end].Trim();
        return comment.IsEmpty ? null : MimeEncoding.DecodeHeaderValue(comment.ToString());
    }

    /// <summary>Parses an address.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The address, or <see langword="null"/> when the value has none.</returns>
    public static MailboxAddress? Parse(string? value) => ParseList(value) is [var first, ..] ? first : null;

    /// <summary>Formats a list of addresses as the value of a header, with the display names encoded when they are not ASCII.</summary>
    /// <param name="addresses">The addresses.</param>
    /// <returns>The value of the header.</returns>
    public static string FormatList(IEnumerable<MailboxAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        return string.Join(", ", addresses.Where(address => address.CanBeWritten).Select(address => address.ToHeaderValue()));
    }

    /// <summary>Returns the address as the value of a header: the display name, encoded when it is not ASCII, and the address.</summary>
    /// <returns>The value of the header.</returns>
    public string ToHeaderValue()
    {
        // An address cannot contain angle brackets or line breaks: they are removed
        var address = GetWritableAddress();
        var needsAngleBrackets = address.AsSpan().IndexOfAny(" \t,;:()\"[]") >= 0;
        if (Name is null)
        {
            return needsAngleBrackets ? "<" + address + ">" : address;
        }

        var name = MimeEncoding.IsPrintableAscii(Name) && !Name.Contains("=?", StringComparison.Ordinal) ? MimeTokenizer.Quote(Name, "()<>[]:;@\\,.\"") : MimeEncoding.EncodeHeaderText(Name);
        return name + " <" + address + ">";
    }

    /// <summary>Gets whether the address is not empty once the characters a header cannot contain are removed; a header cannot contain an empty address.</summary>
    internal bool CanBeWritten => GetWritableAddress().Length > 0;

    private string GetWritableAddress()
    {
        var address = Address;
        if (address.AsSpan().IndexOfAny("<>\r\n") >= 0)
        {
            address = string.Concat(address.Where(c => c is not ('<' or '>' or '\r' or '\n')));
        }

        return address.Trim();
    }

    /// <summary>Returns the display name and the address.</summary>
    /// <returns>The display name and the address.</returns>
    public override string ToString() => Name is null ? Address : Name + " <" + Address + ">";

    /// <inheritdoc />
    public bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] MailboxAddress? other) => other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal) && string.Equals(Address, other.Address, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] object? obj) => Equals(obj as MailboxAddress);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name is null ? 0 : StringComparer.Ordinal.GetHashCode(Name), StringComparer.OrdinalIgnoreCase.GetHashCode(Address));
}
