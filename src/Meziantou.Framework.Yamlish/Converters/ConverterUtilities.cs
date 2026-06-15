namespace Meziantou.Framework.Yamlish;

internal static class ConverterUtilities
{
    public static string GetScalarValue(YamlishNode node, Type type)
    {
        return node is YamlishScalar scalar ? scalar.Value : throw new FormatException($"Cannot convert a {node.Kind} node to '{type}'.");
    }
}
