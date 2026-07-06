namespace Meziantou.Framework.Yaml.Model;

/// <summary>Represents the Yaml Container.</summary>
public abstract class YamlContainer : YamlElement
{
    /// <summary>Gets or sets style.</summary>
    public abstract YamlStyle Style { get; set; }
    /// <summary>Gets or sets is Implicit.</summary>
    public abstract bool IsImplicit { get; set; }
}