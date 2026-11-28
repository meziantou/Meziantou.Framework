namespace Meziantou.Framework.Yaml.Nodes;

/// <summary>Represents the Yaml Container.</summary>
public abstract class YamlContainer : YamlNode
{
    /// <summary>Gets or sets style.</summary>
    public abstract YamlStyle Style { get; set; }
    /// <summary>Gets or sets is Implicit.</summary>
    public abstract bool IsImplicit { get; set; }
}