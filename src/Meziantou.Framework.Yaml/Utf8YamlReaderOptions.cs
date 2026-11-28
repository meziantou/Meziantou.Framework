using System.Runtime.InteropServices;

namespace Meziantou.Framework.Yaml;

/// <summary>Options of <see cref="Utf8YamlReader"/>.</summary>
[StructLayout(LayoutKind.Auto)]
public struct Utf8YamlReaderOptions
{
    private int _maxDepth;

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

    internal readonly int EffectiveMaxDepth => YamlDepthHelper.GetEffectiveMaxDepth(_maxDepth);
}
