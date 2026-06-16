namespace Meziantou.Framework.Yamlish.Nodes;

public sealed class YamlishScalar : YamlishNode
{
    public YamlishScalar(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override YamlishNodeKind Kind => YamlishNodeKind.Scalar;

    public string Value { get; }

    public YamlishScalarStyle Style { get; set; }

    public YamlishScalarChomping Chomping { get; set; } = YamlishScalarChomping.Clip;
}
