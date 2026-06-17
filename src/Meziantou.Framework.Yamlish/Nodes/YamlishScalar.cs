namespace Meziantou.Framework.Yamlish.Nodes;

/// <summary>Represents a Yamlish scalar node.</summary>
public sealed class YamlishScalar : YamlishNode
{
    /// <summary>Initializes a new instance of the <see cref="YamlishScalar" /> class.</summary>
    /// <param name="value">The scalar value.</param>
    public YamlishScalar(string value)
        : this(value, YamlishScalarKind.String)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="YamlishScalar" /> class.</summary>
    /// <param name="value">The scalar value.</param>
    /// <param name="scalarKind">The scalar kind.</param>
    public YamlishScalar(string value, YamlishScalarKind scalarKind)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        if (!Enum.IsDefined(scalarKind))
            throw new ArgumentOutOfRangeException(nameof(scalarKind));

        if (scalarKind is YamlishScalarKind.Null && value is not "null")
            throw new ArgumentException("A null scalar must have the value 'null'.", nameof(value));

        ScalarKind = scalarKind;
    }

    /// <summary>Creates a null scalar.</summary>
    public static YamlishScalar CreateNull()
    {
        return new YamlishScalar("null", YamlishScalarKind.Null);
    }

    /// <inheritdoc />
    public override YamlishNodeKind Kind => YamlishNodeKind.Scalar;

    /// <summary>Gets the scalar value.</summary>
    public string Value { get; }

    /// <summary>Gets the scalar kind.</summary>
    public YamlishScalarKind ScalarKind { get; }

    /// <summary>Gets a value indicating whether this scalar represents a null value.</summary>
    public bool IsNull => ScalarKind is YamlishScalarKind.Null;

    /// <summary>Gets or sets the style used when writing the scalar value.</summary>
    public YamlishScalarStyle Style { get; set; }

    /// <summary>Gets or sets the chomping behavior used when writing a block scalar value.</summary>
    public YamlishScalarChomping Chomping { get; set; } = YamlishScalarChomping.Clip;
}
