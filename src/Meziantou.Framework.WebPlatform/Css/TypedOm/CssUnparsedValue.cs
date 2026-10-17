using System.Collections;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.TypedOm;

/// <summary>The CSSUnparsedSegment typedef: (USVString or CSSVariableReferenceValue) (https://drafts.css-houdini.org/css-typed-om-1/#typedefdef-cssunparsedsegment).</summary>
public readonly struct CssUnparsedSegment : IUnionValue, IEquatable<CssUnparsedSegment>
{
    private CssUnparsedSegment(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="string"/> or a <see cref="CssVariableReferenceValue"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the segment as a string, or null when it is a variable reference.</summary>
    [NotInIdl]
    public string? StringValue => Value as string;

    /// <summary>Gets the segment as a variable reference, or null when it is a string.</summary>
    [NotInIdl]
    public CssVariableReferenceValue? VariableReference => Value as CssVariableReferenceValue;

    /// <summary>Converts a <c>string</c> to a <c>CssUnparsedSegment</c>.</summary>
    [NotInIdl]
    public static implicit operator CssUnparsedSegment(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>CssVariableReferenceValue</c> to a <c>CssUnparsedSegment</c>.</summary>
    [NotInIdl]
    public static implicit operator CssUnparsedSegment(CssVariableReferenceValue value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a <c>CssUnparsedSegment</c> from a <c>string</c>.</summary>
    [NotInIdl]
    public static CssUnparsedSegment FromString(string value) => value;

    /// <summary>Creates a <c>CssUnparsedSegment</c> from a <c>CssVariableReferenceValue</c>.</summary>
    [NotInIdl]
    public static CssUnparsedSegment FromCssVariableReferenceValue(CssVariableReferenceValue value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(CssUnparsedSegment other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is CssUnparsedSegment other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two <c>CssUnparsedSegment</c> values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(CssUnparsedSegment left, CssUnparsedSegment right) => left.Equals(right);

    /// <summary>Determines whether two <c>CssUnparsedSegment</c> values are not equal.</summary>
    [NotInIdl]
    public static bool operator !=(CssUnparsedSegment left, CssUnparsedSegment right) => !left.Equals(right);
}

/// <summary>
/// A value that references custom properties (https://drafts.css-houdini.org/css-typed-om-1/#cssunparsedvalue): a list of
/// strings and <see cref="CssVariableReferenceValue"/> objects, e.g. calc(42px + var(--foo)) is the string "calc(42px + ",
/// a reference to --foo and the string ")".
/// </summary>
[IdlInterface("CSSUnparsedValue", "https://drafts.css-houdini.org/css-typed-om-1/#cssunparsedvalue")]
public sealed class CssUnparsedValue : CssStyleValue, IReadOnlyList<CssUnparsedSegment>
{
    private readonly List<CssUnparsedSegment> _tokens;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssunparsedvalue-cssunparsedvalue</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssunparsedvalue-cssunparsedvalue")]
    public CssUnparsedValue(IEnumerable<CssUnparsedSegment> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        _tokens = [.. members];
    }

    /// <summary>Gets the number of segments.</summary>
    [IdlMember("length", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssunparsedvalue-length")]
    public uint Length => (uint)_tokens.Count;

    /// <summary>
    /// Gets or sets a segment (https://drafts.css-houdini.org/css-typed-om-1/#cssunparsedvalue-indexed-property): setting the
    /// index that follows the last segment appends a segment, a greater index throws a RangeError.
    /// </summary>
    [IdlMember("getter", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssunparsedvalue-__getter__-index-index")]
    [IdlMember("setter", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssunparsedvalue-__setter__-index-val-val")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public CssUnparsedSegment this[uint index]
    {
        get => _tokens[(int)index];
        set
        {
            if (index < (uint)_tokens.Count)
            {
                _tokens[(int)index] = value;
            }
            else if (index == (uint)_tokens.Count)
            {
                _tokens.Add(value);
            }
            else
            {
                throw new RangeErrorException("The index is greater than the number of segments");
            }
        }
    }

    /// <summary>Gets the segment at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public CssUnparsedSegment this[int index] => _tokens[index];

    /// <summary>Gets the number of segments.</summary>
    [NotInIdl]
    public int Count => _tokens.Count;

    /// <inheritdoc/>
    [NotInIdl]
    public IEnumerator<CssUnparsedSegment> GetEnumerator() => _tokens.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// https://drafts.css-houdini.org/css-typed-om-1/#unparsedvalue-serialization: the concatenation of the segments; an empty
    /// comment separates two tokens that would otherwise be parsed as one (e.g. two identifiers).
    /// </summary>
    [NotInIdl("The stringifier of CSSStyleValue")]
    public override string ToString()
    {
        var builder = new StringBuilder();
        Serialize(builder);
        return builder.ToString();
    }

    internal void Serialize(StringBuilder builder)
    {
        foreach (var token in _tokens)
        {
            switch (token.Value)
            {
                case string text:
                    CssSubstitution.AppendSeparated(builder, text);
                    break;

                case CssVariableReferenceValue reference:
                    CssSubstitution.AppendSeparated(builder, "var(");
                    builder.Append(CssSerializer.SerializeIdentifier(reference.Variable));
                    if (reference.Fallback is { } fallback)
                    {
                        builder.Append(',');
                        fallback.Serialize(builder);
                    }

                    builder.Append(')');
                    break;
            }
        }
    }
}

/// <summary>A var() reference (https://drafts.css-houdini.org/css-typed-om-1/#cssvariablereferencevalue).</summary>
[IdlInterface("CSSVariableReferenceValue", "https://drafts.css-houdini.org/css-typed-om-1/#cssvariablereferencevalue")]
public sealed class CssVariableReferenceValue
{
    private string _variable;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssvariablereferencevalue-cssvariablereferencevalue</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssvariablereferencevalue-cssvariablereferencevalue")]
    public CssVariableReferenceValue(string variable, CssUnparsedValue? fallback = null)
    {
        _variable = Validate(variable);
        Fallback = fallback;
    }

    /// <summary>The name of the custom property (e.g. "--foo").</summary>
    [IdlMember("variable", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssvariablereferencevalue-variable")]
    public string Variable
    {
        get => _variable;
        set => _variable = Validate(value);
    }

    /// <summary>
    /// Gets the fallback value used when the custom property is not defined, or null when there is none.
    /// </summary>
    [IdlMember("fallback", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssvariablereferencevalue-fallback")]
    public CssUnparsedValue? Fallback { get; }

    private static string Validate(string variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        if (!CssPropertyRegistry.IsCustomProperty(variable))
            throw new TypeErrorException($"'{variable}' is not a custom property name");

        return variable;
    }

    /// <inheritdoc/>
    [NotInIdl]
    public override string ToString()
    {
        var builder = new StringBuilder();
        new CssUnparsedValue([this]).Serialize(builder);
        return builder.ToString();
    }
}
