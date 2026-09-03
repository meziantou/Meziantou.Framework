namespace Meziantou.Framework.SnapshotTesting.Roslyn;

/// <summary>
/// Provides extension methods for <see cref="SnapshotSettings"/> to enable snapshot testing of Roslyn objects.
/// </summary>
public static class SnapshotSettingsRoslynExtensions
{
    extension(SnapshotSettings snapshotSettings)
    {
        /// <summary>
        /// Registers the serializers and converters for Roslyn objects on the <see cref="SnapshotSettings"/>.
        /// <para>
        /// A <see cref="Microsoft.CodeAnalysis.GeneratorDriverRunResult"/> is stored as one source file per generated source,
        /// followed by a text file containing the diagnostics reported by the generators.
        /// A <see cref="Microsoft.CodeAnalysis.SyntaxTree"/>, a <see cref="Microsoft.CodeAnalysis.SyntaxNode"/>, one of the
        /// token or trivia types, or a <see cref="Microsoft.CodeAnalysis.Text.SourceText"/> is stored as a single source file.
        /// A <see cref="Microsoft.CodeAnalysis.Diagnostic"/>, or a collection of them, is stored as a text file with one diagnostic per line.
        /// </para>
        /// </summary>
        public void AddRoslyn()
        {
            if (snapshotSettings.Serializers.Any(serializer => serializer is GeneratedSourcesSnapshotSerializer))
                return;

            snapshotSettings.Serializers.Add(GeneratedSourcesSnapshotSerializer.Instance);
            snapshotSettings.Serializers.Add(SyntaxSnapshotSerializer.Instance);
            snapshotSettings.Serializers.Add(DiagnosticSnapshotSerializer.Instance);

            // Roslyn values nested inside another snapshot are written by the human-readable serializer.
            // They are registered in a single call so the human-readable serializer is cloned only once.
            snapshotSettings.ConfigureHumanReadableSerializer(options =>
            {
                options.Converters.Add(new DiagnosticHumanReadableConverter());
                options.Converters.Add(new LocationHumanReadableConverter());
                options.Converters.Add(new TextSpanHumanReadableConverter());
                options.Converters.Add(new LinePositionHumanReadableConverter());
                options.Converters.Add(new LinePositionSpanHumanReadableConverter());
            });
        }
    }
}
