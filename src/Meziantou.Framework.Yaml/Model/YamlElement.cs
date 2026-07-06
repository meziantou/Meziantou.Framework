namespace Meziantou.Framework.Yaml.Model;

/// <summary>Represents the Yaml Element.</summary>
public abstract class YamlElement : YamlNode
{
    /// <summary>Gets or sets anchor.</summary>
    public abstract string? Anchor { get; set; }
    /// <summary>Gets or sets tag.</summary>
    public abstract string? Tag { get; set; }
    /// <summary>Gets is Canonical.</summary>
    public abstract bool IsCanonical { get; }
}