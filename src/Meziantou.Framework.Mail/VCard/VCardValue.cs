namespace Meziantou.Framework.Mail;

/// <summary>A value of a vCard with its types: an email address, a telephone number, a URL...</summary>
public sealed class VCardValue
{
    /// <summary>Creates a value.</summary>
    /// <param name="value">The value.</param>
    /// <param name="types">The types, such as <c>work</c>, <c>home</c>, <c>cell</c>, <c>voice</c> or <c>pref</c>.</param>
    public VCardValue(string value, params IEnumerable<string> types)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
        Types = [.. types.Select(type => type.ToLowerInvariant())];
    }

    /// <summary>Gets or sets the value.</summary>
    public string Value { get; set; }

    /// <summary>Gets the types, in lower case, such as <c>work</c>, <c>home</c>, <c>cell</c>, <c>fax</c> or <c>pref</c>.</summary>
    public IList<string> Types { get; }

    /// <summary>Gets whether the value has a type.</summary>
    /// <param name="type">The type, without case.</param>
    /// <returns><see langword="true"/> when the value has the type.</returns>
    public bool HasType(string type) => Types.Contains(type, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the value.</summary>
    /// <returns>The value.</returns>
    public override string ToString() => Value;
}
