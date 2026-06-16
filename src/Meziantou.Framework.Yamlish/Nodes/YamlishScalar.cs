namespace Meziantou.Framework.Yamlish.Nodes;

/// <summary>Represents a Yamlish scalar node.</summary>
public sealed class YamlishScalar : YamlishNode
{
    /// <summary>Initializes a new instance of the <see cref="YamlishScalar" /> class.</summary>
    /// <param name="value">The scalar value.</param>
    public YamlishScalar(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <inheritdoc />
    public override YamlishNodeKind Kind => YamlishNodeKind.Scalar;

    /// <summary>Gets the scalar value.</summary>
    public string Value { get; }

    /// <summary>Gets or sets the style used when writing the scalar value.</summary>
    public YamlishScalarStyle Style { get; set; }

    /// <summary>Gets or sets the chomping behavior used when writing a block scalar value.</summary>
    public YamlishScalarChomping Chomping { get; set; } = YamlishScalarChomping.Clip;
}
