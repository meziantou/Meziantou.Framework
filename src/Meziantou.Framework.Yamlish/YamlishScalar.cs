namespace Meziantou.Framework.Yamlish;

public sealed class YamlishScalar : YamlishNode
{
    public YamlishScalar(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override YamlishNodeKind Kind => YamlishNodeKind.Scalar;

    public string Value { get; }
}
