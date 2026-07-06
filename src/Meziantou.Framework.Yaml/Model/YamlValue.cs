using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Schemas;

namespace Meziantou.Framework.Yaml.Model;

/// <summary>Represents the Yaml Value.</summary>
public class YamlValue : YamlElement
{
    private Scalar _scalar;

    private YamlValue(Scalar scalar)
    {
        Scalar = scalar ?? throw new ArgumentNullException(nameof(scalar));
    }

    /// <summary>Initializes a new instance of this type.</summary>
    public YamlValue(object value, IYamlSchema? schema = null)
    {
        ArgumentNullException.ThrowIfNull(value);

        var valueString = ConvertValue(value);
        if (schema == null)
            schema = CoreSchema.Instance;

        Scalar = new Scalar(schema.GetDefaultTag(value.GetType()), valueString);
    }

    private static string ConvertValue(object value)
    {
        if (value is string str)
        {
            return str;
        }

        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        if (value is char ch)
        {
            return ch.ToString(CultureInfo.InvariantCulture);
        }

        if (value is Enum)
        {
            return value.ToString() ?? string.Empty;
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(format: null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }

    internal Scalar Scalar
    {
        get { return _scalar; }
        [MemberNotNull(nameof(_scalar))]
        set
        {
            _scalar = value;
        }
    }

    /// <summary>Loads data.</summary>
    public static YamlValue Load(EventReader eventReader)
    {
        var scalar = eventReader.Expect<Scalar>();

        return new YamlValue(scalar);
    }

    /// <summary>Creates a deep clone of the current value.</summary>
    public override YamlNode DeepClone()
    {
        return new YamlValue(_scalar);
    }

    /// <summary>Gets anchor.</summary>
    public override string? Anchor
    {
        get { return _scalar.Anchor; }
        set
        {
            Scalar = new Scalar(value,
                _scalar.Tag,
                _scalar.Value,
                _scalar.Style,
                _scalar.IsPlainImplicit,
                _scalar.IsQuotedImplicit,
                _scalar.Start,
                _scalar.End);
        }
    }

    /// <summary>Gets tag.</summary>
    public override string? Tag
    {
        get { return _scalar.Tag; }
        set
        {
            Scalar = new Scalar(_scalar.Anchor,
                value,
                _scalar.Value,
                _scalar.Style,
                _scalar.IsPlainImplicit,
                _scalar.IsQuotedImplicit,
                _scalar.Start,
                _scalar.End);
        }
    }

    /// <summary>Gets style.</summary>
    public ScalarStyle Style
    {
        get { return _scalar.Style; }
        set
        {
            Scalar = new Scalar(_scalar.Anchor,
                _scalar.Tag,
                _scalar.Value,
                value,
                _scalar.IsPlainImplicit,
                _scalar.IsQuotedImplicit,
                _scalar.Start,
                _scalar.End);
        }
    }

    /// <summary>Gets a value indicating whether is Canonical.</summary>
    public override bool IsCanonical { get { return _scalar.IsCanonical; } }

    /// <summary>Gets a value indicating whether is Plain Implicit.</summary>
    public bool IsPlainImplicit
    {
        get { return _scalar.IsPlainImplicit; }
        set
        {
            Scalar = new Scalar(_scalar.Anchor,
                _scalar.Tag,
                _scalar.Value,
                _scalar.Style,
                value,
                _scalar.IsQuotedImplicit,
                _scalar.Start,
                _scalar.End);
        }
    }

    /// <summary>Gets a value indicating whether is Quoted Implicit.</summary>
    public bool IsQuotedImplicit
    {
        get { return _scalar.IsQuotedImplicit; }
        set
        {
            Scalar = new Scalar(_scalar.Anchor,
                _scalar.Tag,
                _scalar.Value,
                _scalar.Style,
                _scalar.IsPlainImplicit,
                value,
                _scalar.Start,
                _scalar.End);
        }
    }

    /// <summary>Gets value.</summary>
    public string Value
    {
        get { return _scalar.Value; }
        set
        {
            Scalar = new Scalar(_scalar.Anchor,
                _scalar.Tag,
                value,
                _scalar.Style,
                _scalar.IsPlainImplicit,
                _scalar.IsQuotedImplicit,
                _scalar.Start,
                _scalar.End);
        }
    }
}
