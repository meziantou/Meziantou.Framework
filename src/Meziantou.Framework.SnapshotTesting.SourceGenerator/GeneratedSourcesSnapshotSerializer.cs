using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.SnapshotTesting.SourceGenerator;

internal sealed class GeneratedSourcesSnapshotSerializer : ISnapshotSerializer
{
    public static ISnapshotSerializer Instance { get; } = new GeneratedSourcesSnapshotSerializer();

    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        if (value is not GeneratorDriverRunResult run)
        {
            result = null;
            return false;
        }

        var files = new List<SnapshotData>();

        // Hint names are the stable order: generators run in whatever order the driver picked,
        // and that order is not a contract.
        foreach (var generated in run.Results
            .SelectMany(generatorResult => generatorResult.GeneratedSources)
            .OrderBy(generatedSource => generatedSource.HintName, StringComparer.Ordinal))
        {
            // The hint name goes *inside* the file too: it names what the consumer sees with
            // EmitCompilerGeneratedFiles turned on, so a rename has to show up as a diff somewhere.
            var source =
                "// HintName: " + generated.HintName + "\n"
                + generated.SourceText.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);

            files.Add(new SnapshotData("cs", Encoding.UTF8.GetBytes(source)));
        }

        var report = new StringBuilder();
        foreach (var diagnostic in run.Diagnostics
            .OrderBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.ToString(), StringComparer.Ordinal))
        {
            report.Append(diagnostic.Id).Append(": ").Append(diagnostic.GetMessage(CultureInfo.InvariantCulture)).Append('\n');
        }

        // A generator that throws reports no diagnostic and produces no source, so without this the
        // snapshot would be indistinguishable from a generator that decided to generate nothing.
        foreach (var generatorResult in run.Results.OrderBy(generatorResult => generatorResult.Generator.GetGeneratorType().FullName, StringComparer.Ordinal))
        {
            if (generatorResult.Exception is not { } exception)
                continue;

            report.Append(generatorResult.Generator.GetGeneratorType().FullName)
                .Append(": ")
                .Append(exception.GetType().FullName)
                .Append(": ")
                .Append(exception.Message)
                .Append('\n');
        }

        // The engine rejects an empty snapshot, so a run that generated nothing still needs a file to compare.
        if (report.Length > 0 || files.Count == 0)
        {
            files.Add(new SnapshotData("txt", Encoding.UTF8.GetBytes(report.ToString())));
        }

        result = new SerializedSnapshot(files);
        return true;
    }
}
