namespace Meziantou.Framework.Yaml;

/// <summary>Specifies how a nested block collection is emitted when it appears as an item in a block sequence.</summary>
public enum YamlSequenceItemStyle
{
    /// <summary>Use the current serializer default for the nested collection kind.</summary>
    Default,

    /// <summary>Emit the nested collection on lines following the sequence dash.</summary>
    Expanded,

    /// <summary>Emit the nested collection's first item on the same line as the sequence dash when possible.</summary>
    Compact,
}
