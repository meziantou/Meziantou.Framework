using System.Runtime.InteropServices;

namespace Meziantou.Framework.Yaml.Syntax;

/// <summary>Represents a span in YAML source text.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct YamlSourceSpan
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlSourceSpan"/> struct.
    /// </summary>
    /// <param name="start">The start mark.</param>
    /// <param name="end">The end mark.</param>
    public YamlSourceSpan(Mark start, Mark end)
    {
        Start = start;
        End = end;
    }

    /// <summary>Gets the start mark.</summary>
    public Mark Start { get; }

    /// <summary>Gets the end mark.</summary>
    public Mark End { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Start.Index}:{End.Index}";
    }
}
