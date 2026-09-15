using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Json;
using JsonPathExpression = Meziantou.Framework.Json.JsonPath;

namespace Meziantou.Framework.DependencyScanning;

internal sealed class JsonLocation : Location
{
    internal JsonLocation(ScanFileContext context, string jsonPath)
        : this(context.FileSystem, context.FullPath, jsonPath, -1, -1)
    {
    }

    internal JsonLocation(ScanFileContext context, string jsonPath, int column, int length)
        : this(context.FileSystem, context.FullPath, jsonPath, column, length)
    {

    }

    internal JsonLocation(IFileSystem fileSystem, string filePath, string jsonPath, int column, int length)
        : base(fileSystem, filePath)
    {
        JsonPath = jsonPath;
        StartPosition = column;
        Length = length;
    }

    public string JsonPath { get; }
    public int StartPosition { get; set; }
    public int Length { get; }

    public override bool IsUpdatable => true;

    protected internal override async Task UpdateCoreAsync(string? oldValue, string newValue, CancellationToken cancellationToken)
    {
        var stream = FileSystem.OpenReadWrite(FilePath);
        try
        {
            var file = await StreamUtilities.ReadForUpdateAsync(stream, isXml: false, cancellationToken).ConfigureAwait(false);
            var syntaxTree = JsonSyntaxTree.ParseText(file.Text);
            var updatedRoot = ReplaceValue(syntaxTree, oldValue, newValue);
            var updatedContent = updatedRoot.ToFullString();
            await StreamUtilities.WriteForUpdateAsync(stream, file, updatedContent, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{FilePath}:{JsonPath}");
    }

    private string UpdateTextValue(string? currentValue, string? oldValue, string newValue)
    {
        if (StartPosition < 0)
        {
            if (oldValue is not null && !string.Equals(currentValue, oldValue, StringComparison.Ordinal))
                throw new DependencyScannerException($"Expected value '{oldValue}' does not match the current value '{currentValue}'. The file was probably modified since last scan.");

            return newValue;
        }

        if (currentValue is null)
            throw new DependencyScannerException("Current value is null. The file was probably modified since last scan.");

        var index = oldValue is null ? GetRecordedIndex(currentValue) : FindOldValue(currentValue, oldValue);
        var length = oldValue?.Length ?? Length;
        return string.Concat(currentValue.AsSpan(0, index), newValue, currentValue.AsSpan(index + length));
    }

    private int GetRecordedIndex(string currentValue)
    {
        if (Length < 0 || StartPosition > currentValue.Length || Length > currentValue.Length - StartPosition)
            throw new DependencyScannerException($"The recorded location does not fit in the current value '{currentValue}'. The file was probably modified since last scan.");

        return StartPosition;
    }

    private int FindOldValue(string currentValue, string oldValue)
    {
        if (StartPosition <= currentValue.Length && oldValue.Length <= currentValue.Length - StartPosition &&
            currentValue.AsSpan(StartPosition, oldValue.Length).Equals(oldValue, StringComparison.Ordinal))
        {
            return StartPosition;
        }

        // Several locations can share one string, such as "name#version". Updating one of them moves the ones after it,
        // so the value is searched again, but only where it can have moved to: anywhere after the recorded start when
        // the string grew, or at its end when the string shrank. Anything before that window belongs to another part
        // of the string, and the value is only replaced when it occurs exactly once in the window.
        var searchStart = Math.Max(0, Math.Min(StartPosition, currentValue.Length - oldValue.Length));
        var index = currentValue.IndexOf(oldValue, searchStart, StringComparison.Ordinal);
        if (index >= 0 && (oldValue.Length == 0 || currentValue.IndexOf(oldValue, index + 1, StringComparison.Ordinal) < 0))
            return index;

        throw new DependencyScannerException($"Expected value '{oldValue}' was not found at the recorded location in the current value '{currentValue}'. The file was probably modified since last scan.");
    }

    private JsonDocumentSyntax ReplaceValue(JsonSyntaxTree syntaxTree, string? oldValue, string newValue)
    {
        if (!JsonPathExpression.TryParse(JsonPath, out var path))
            throw new DependencyScannerException("Dependency not found. File was probably modified since last scan.");

        var node = path.EvaluateValue(syntaxTree);
        if (node is not JsonStringSyntax stringNode)
            throw new DependencyScannerException("Dependency not found. File was probably modified since last scan.");

        var updatedValue = UpdateTextValue(stringNode.Value, oldValue, newValue);
        if (string.Equals(updatedValue, stringNode.Value, StringComparison.Ordinal))
            return syntaxTree.GetRoot();

        // Replacing the token rebuilds the nodes above it and leaves the rest of the document as it was, so the file
        // comes back byte for byte the same apart from this one value.
        var updatedToken = SyntaxFactory.Literal(updatedValue).WithTriviaFrom(stringNode.StringToken);

        return syntaxTree.GetRoot().ReplaceToken(stringNode.StringToken, updatedToken);
    }
}
