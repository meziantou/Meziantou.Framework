namespace Meziantou.Framework.SnapshotTesting.Roslyn;

/// <summary>
/// Provides extension methods for <see cref="SnapshotSettings"/> to enable snapshot testing of Roslyn objects.
/// </summary>
public static class SnapshotSettingsRoslynExtensions
{
    extension(SnapshotSettings snapshotSettings)
    {
        /// <summary>
        /// Registers the serializers for Roslyn objects on the <see cref="SnapshotSettings"/>.
        /// <para>
        /// A <see cref="Microsoft.CodeAnalysis.GeneratorDriverRunResult"/> is stored as one source file per generated source,
        /// followed by a text file containing the diagnostics reported by the generators.
        /// A <see cref="Microsoft.CodeAnalysis.SyntaxTree"/> or a <see cref="Microsoft.CodeAnalysis.SyntaxNode"/> is stored as a single source file.
        /// </para>
        /// </summary>
        public void AddRoslyn()
        {
            if (snapshotSettings.Serializers.Any(serializer => serializer is GeneratedSourcesSnapshotSerializer))
                return;

            snapshotSettings.Serializers.Add(GeneratedSourcesSnapshotSerializer.Instance);
            snapshotSettings.Serializers.Add(SyntaxSnapshotSerializer.Instance);
        }
    }
}
