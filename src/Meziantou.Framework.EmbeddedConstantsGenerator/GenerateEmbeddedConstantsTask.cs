#nullable enable
using System.Text;
using Microsoft.Build.Framework;

namespace Meziantou.Framework.EmbeddedConstantsGenerator;

public sealed class GenerateEmbeddedConstantsTask : Microsoft.Build.Utilities.Task
{
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "MSBuild task item parameters are represented as arrays.")]
    [Required]
    public ITaskItem[] EmbeddedConstants { get; set; } = [];

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    public string Namespace { get; set; } = "Generated";

    public string ClassName { get; set; } = "EmbeddedConstants";

    public string ClassVisibility { get; set; } = "internal";

    public string MemberVisibility { get; set; } = "public";

    public string? ProjectDirectory { get; set; }

    public override bool Execute()
    {
        var options = new EmbeddedConstantsGeneratorTask.GeneratorOptions(Namespace, ClassName, ClassVisibility, MemberVisibility, ProjectDirectory);
        var files = EmbeddedConstants
            .Select(item => new EmbeddedConstantsGeneratorTask.InputFile(
                item.ItemSpec,
                GetMetadata(item, "Meziantou_EmbeddedConstantKind", "EmbeddedConstantKind", "Kind"),
                GetMetadata(item, "Meziantou_EmbeddedConstantName", "EmbeddedConstantName", "Name"),
                ProjectDirectory))
            .ToArray();

        var result = EmbeddedConstantsGeneratorTask.Create(options, files);
        foreach (var error in result.Errors)
        {
            LogValidationError(error);
        }

        if (result.HasErrors)
            return false;

        var source = EmbeddedConstantsGeneratorTask.GenerateSource(result.Options, result.Entries);
        WriteFileIfChanged(OutputPath, source);
        return !Log.HasLoggedErrors;
    }

    private void LogValidationError(EmbeddedConstantsGeneratorTask.ValidationError error)
    {
        Log.LogError(
            subcategory: string.Empty,
            errorCode: error.Code,
            helpKeyword: string.Empty,
            file: error.FilePath ?? string.Empty,
            lineNumber: 0,
            columnNumber: 0,
            endLineNumber: 0,
            endColumnNumber: 0,
            message: error.Message);
    }

    private static string? GetMetadata(ITaskItem item, params string[] names)
    {
        foreach (var name in names)
        {
            var value = item.GetMetadata(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static void WriteFileIfChanged(string path, string content)
    {
        var directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
            return;

        File.WriteAllText(path, content, Encoding.UTF8);
    }
}
