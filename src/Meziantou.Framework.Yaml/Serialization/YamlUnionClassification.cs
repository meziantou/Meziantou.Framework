using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Applies the <see cref="YamlSerializerOptions.TypeClassifiers"/> of a reader to a C# union payload.</summary>
/// <remarks>This type supports the serializer infrastructure and is not intended to be used directly from your code.</remarks>
public static class YamlUnionClassification
{
    private static readonly ConditionalWeakTable<YamlSerializerOptions, ConcurrentDictionary<Type, YamlTypeClassifier?>> Cache = new();

    /// <summary>
    /// Selects the union case matching the value <paramref name="reader"/> is positioned on, when a classifier is
    /// registered for the union type.
    /// </summary>
    /// <param name="reader">The reader positioned at the start of the value.</param>
    /// <param name="context">The union type and the cases to select from.</param>
    /// <param name="bufferedNode">
    /// Receives the buffered YAML of the value when a classifier ran, or <see langword="null"/> when no classifier is
    /// registered. Classification reads the value, so the caller must deserialize the selected case from a reader
    /// created over the buffered YAML rather than from <paramref name="reader"/>.
    /// </param>
    /// <returns>The selected case type, or <see langword="null"/> when no classifier is registered or the value cannot be classified.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    public static Type? Classify(YamlReader reader, YamlTypeClassifierContext context, out string? bufferedNode)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(context);

        bufferedNode = null;

        var classifier = GetClassifier(reader.Options, context);
        if (classifier is null)
        {
            return null;
        }

        // Classification reads the value, so it runs over a private copy and leaves the buffered YAML to the caller.
        bufferedNode = YamlReader.BufferCurrentNodeToString(reader);
        var classifierReader = reader.CreateReader(bufferedNode);
        return classifierReader.Read() ? classifier(classifierReader) : null;
    }

    private static YamlTypeClassifier? GetClassifier(YamlSerializerOptions options, YamlTypeClassifierContext context)
    {
        var classifiersByType = Cache.GetValue(options, static _ => new ConcurrentDictionary<Type, YamlTypeClassifier?>());
        if (classifiersByType.TryGetValue(context.DeclaringType, out var cached))
        {
            return cached;
        }

        YamlTypeClassifier? classifier = null;
        foreach (var factory in options.TypeClassifiers)
        {
            if (factory.CanClassify(context))
            {
                classifier = factory.CreateYamlClassifier(context, options);
                break;
            }
        }

        return classifiersByType.GetOrAdd(context.DeclaringType, classifier);
    }
}
