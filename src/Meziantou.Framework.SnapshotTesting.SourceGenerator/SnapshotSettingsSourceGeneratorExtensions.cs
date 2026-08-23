namespace Meziantou.Framework.SnapshotTesting.SourceGenerator;

/// <summary>
/// Provides extension methods for <see cref="SnapshotSettings"/> to enable snapshot testing of Roslyn source generators.
/// </summary>
public static class SnapshotSettingsSourceGeneratorExtensions
{
    extension(SnapshotSettings snapshotSettings)
    {
        /// <summary>
        /// Registers the serializer for <see cref="Microsoft.CodeAnalysis.GeneratorDriverRunResult"/> values on the <see cref="SnapshotSettings"/>.
        /// Each generated source is stored as its own <c>.cs</c> snapshot file, followed by a <c>.txt</c> file
        /// containing the diagnostics reported by the generators.
        /// </summary>
        public void AddSourceGenerator()
        {
            if (snapshotSettings.Serializers.Any(serializer => serializer is GeneratedSourcesSnapshotSerializer))
                return;

            snapshotSettings.Serializers.Add(GeneratedSourcesSnapshotSerializer.Instance);
        }
    }
}
