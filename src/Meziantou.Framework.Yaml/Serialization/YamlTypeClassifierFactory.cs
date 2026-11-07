namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Creates the <see cref="YamlTypeClassifier"/> used to select a union case from a YAML payload.</summary>
/// <remarks>
/// Register a factory through <see cref="YamlSerializerOptions.TypeClassifiers"/>. Without one, union cases are
/// selected by YAML shape alone and a payload matching several cases fails to deserialize.
/// </remarks>
public abstract class YamlTypeClassifierFactory
{
    /// <summary>Determines whether this factory can classify the type described by <paramref name="context"/>.</summary>
    /// <param name="context">The type being classified.</param>
    /// <returns><see langword="true"/> when the factory can classify the type; otherwise <see langword="false"/>.</returns>
    public abstract bool CanClassify(YamlTypeClassifierContext context);

    /// <summary>Creates the classifier for the type described by <paramref name="context"/>.</summary>
    /// <param name="context">The type being classified.</param>
    /// <param name="options">The options the classifier is created for.</param>
    /// <returns>The classifier to select a case with.</returns>
    public abstract YamlTypeClassifier CreateYamlClassifier(YamlTypeClassifierContext context, YamlSerializerOptions options);
}
