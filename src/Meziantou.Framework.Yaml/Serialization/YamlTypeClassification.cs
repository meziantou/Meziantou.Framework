using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Applies the <see cref="YamlSerializerOptions.TypeClassifiers"/> of a reader to a YAML payload.</summary>
/// <remarks>This type supports the serializer infrastructure and is not intended to be used directly from your code.</remarks>
public static class YamlTypeClassification
{
    private static readonly ConditionalWeakTable<YamlSerializerOptions, ConcurrentDictionary<Type, YamlTypeClassifier?>> Cache = new();

    /// <summary>
    /// Selects the type matching the value <paramref name="reader"/> is positioned on, when a classifier is registered
    /// for the type described by <paramref name="context"/>.
    /// </summary>
    /// <param name="reader">The reader positioned at the start of the value.</param>
    /// <param name="context">The type to classify and the candidates to select from.</param>
    /// <param name="bufferedNode">
    /// Receives the buffered YAML of the value when a classifier ran, or <see langword="null"/> when no classifier is
    /// registered. Classification reads the value, so the caller must deserialize from a reader created over the
    /// buffered YAML rather than from <paramref name="reader"/>.
    /// </param>
    /// <returns>The selected type, or <see langword="null"/> when no classifier is registered or the value cannot be classified.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    public static Type? Classify(YamlReader reader, YamlTypeClassifierContext context, out string? bufferedNode)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(context);

        bufferedNode = null;

        if (GetClassifier(reader.Options, context) is null)
        {
            return null;
        }

        // Classification reads the value, so it runs over a private copy and leaves the buffered YAML to the caller.
        bufferedNode = YamlReader.BufferCurrentNodeToString(reader);
        return ClassifyBufferedNode(reader, bufferedNode, context);
    }

    /// <summary>
    /// Selects the type matching an already buffered value, when a classifier is registered for the type described by
    /// <paramref name="context"/>.
    /// </summary>
    /// <param name="reader">The reader the value was buffered from, used for its options.</param>
    /// <param name="bufferedNode">The buffered YAML of the value to classify.</param>
    /// <param name="context">The type to classify and the candidates to select from.</param>
    /// <returns>The selected type, or <see langword="null"/> when no classifier is registered or the value cannot be classified.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/>, <paramref name="bufferedNode"/>, or <paramref name="context"/> is <see langword="null"/>.</exception>
    public static Type? ClassifyBufferedNode(YamlReader reader, string bufferedNode, YamlTypeClassifierContext context)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(bufferedNode);
        ArgumentNullException.ThrowIfNull(context);

        var classifier = GetClassifier(reader.Options, context);
        if (classifier is null)
        {
            return null;
        }

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
