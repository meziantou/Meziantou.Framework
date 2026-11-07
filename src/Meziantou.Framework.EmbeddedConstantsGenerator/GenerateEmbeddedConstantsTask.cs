#nullable enable
using Microsoft.Build.Framework;

namespace Meziantou.Framework.EmbeddedConstantsGenerator;

public sealed class GenerateEmbeddedConstantsTask : Microsoft.Build.Utilities.Task
{
    private const string UnexpectedFailureCode = "MFECG0011";

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
        EmbeddedConstantsGeneratorTask.Result result;
        try
        {
            var options = new EmbeddedConstantsGeneratorTask.GeneratorOptions(Namespace, ClassName, ClassVisibility, MemberVisibility, ProjectDirectory);
            var files = EmbeddedConstants
                .Select(item => new EmbeddedConstantsGeneratorTask.InputFile(
                    item.ItemSpec,
                    GetMetadata(item, "Meziantou_EmbeddedConstantKind", "EmbeddedConstantKind", "Kind"),
                    GetMetadata(item, "Meziantou_EmbeddedConstantName", "EmbeddedConstantName", "Name"),
                    ProjectDirectory))
                .ToArray();

            result = EmbeddedConstantsGeneratorTask.Create(options, files);
        }
        catch (Exception ex)
        {
            // Without this the failure surfaces as MSB4018 with a stack trace instead of a documented diagnostic
            LogError(UnexpectedFailureCode, filePath: null, string.Create(CultureInfo.InvariantCulture, $"The embedded constants could not be computed: {ex.Message}"));
            return false;
        }

        foreach (var error in result.Errors)
        {
            LogValidationError(error);
        }

        if (result.HasErrors)
            return false;

        var source = EmbeddedConstantsGeneratorTask.GenerateSource(result.Options, result.Entries);
        try
        {
            WriteFileIfChanged(OutputPath, source);
        }
        catch (Exception ex)
        {
            LogError(UnexpectedFailureCode, OutputPath, string.Create(CultureInfo.InvariantCulture, $"The generated file could not be written: {ex.Message}"));
            return false;
        }

        return !Log.HasLoggedErrors;
    }

    private void LogValidationError(EmbeddedConstantsGeneratorTask.ValidationError error)
    {
        LogError(error.Code, error.FilePath, error.Message);
    }

    private void LogError(string code, string? filePath, string message)
    {
        Log.LogError(
            subcategory: string.Empty,
            errorCode: code,
            helpKeyword: string.Empty,
            file: filePath ?? string.Empty,
            lineNumber: 0,
            columnNumber: 0,
            endLineNumber: 0,
            endColumnNumber: 0,
            message: message);
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
