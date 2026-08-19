namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Selects the case type matching the YAML value <paramref name="reader"/> is positioned on.</summary>
/// <param name="reader">
/// A reader positioned at the start of the value to classify. The reader reads a private copy of the value, so a
/// classifier may consume it; doing so does not affect the reader the value is deserialized from.
/// </param>
/// <returns>The selected case type, or <see langword="null"/> when the value cannot be classified.</returns>
public delegate Type? YamlTypeClassifier(YamlReader reader);
