using System.Runtime.InteropServices;

namespace Meziantou.Framework.Yaml;

/// <summary>Options of <see cref="Utf8YamlWriter"/>.</summary>
[StructLayout(LayoutKind.Auto)]
public struct Utf8YamlWriterOptions
{
    private int _indentSize;
    private int _maxDepth;

    /// <summary>Gets or sets the number of spaces of each indentation level of the block collections. 0 uses the default, 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than 16.</exception>
    public int IndentSize
    {
        readonly get => _indentSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 16);
            _indentSize = value;
        }
    }

    /// <summary>Gets or sets the maximum nesting depth of the collections. 0 uses the default, 64.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int MaxDepth
    {
        readonly get => _maxDepth;
        set
        {
            YamlDepthHelper.ValidateMaxDepth(value, nameof(value));
            _maxDepth = value;
        }
    }

    internal readonly int EffectiveIndentSize => _indentSize == 0 ? 2 : _indentSize;

    internal readonly int EffectiveMaxDepth => YamlDepthHelper.GetEffectiveMaxDepth(_maxDepth);
}
