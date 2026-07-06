using Meziantou.Framework.DependencyScanning.Internals;
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
            string text;
            Encoding encoding;
            using (var textReader = await StreamUtilities.CreateReaderAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                text = await textReader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                encoding = textReader.CurrentEncoding;
            }

            var syntaxTree = JsonSyntaxTree.ParseText(text);
            var updatedRoot = ReplaceValue(syntaxTree, oldValue, newValue);
            var updatedContent = updatedRoot.ToFullString();

            stream.SetLength(0);
            stream.Seek(0, SeekOrigin.Begin);

            await using var textWriter = StreamUtilities.CreateWriter(stream, encoding);
            await textWriter.WriteAsync(updatedContent.AsMemory(), cancellationToken).ConfigureAwait(false);
            await textWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
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

        if (oldValue is not null)
        {
            if (currentValue is null)
                throw new DependencyScannerException("Current value is null. The file was probably modified since last scan.");

            if (TryReplaceAtFixedPosition(currentValue, oldValue, newValue, out var replacedValue))
                return replacedValue;

            var index = currentValue.IndexOf(oldValue, StringComparison.Ordinal);
            if (index >= 0)
                return currentValue.Remove(index, oldValue.Length).Insert(index, newValue);

            throw new DependencyScannerException($"Expected value '{oldValue}' was not found in the current value '{currentValue}'. The file was probably modified since last scan.");
        }

        if (currentValue is null)
            throw new DependencyScannerException("Current value is null. The file was probably modified since last scan.");

        return currentValue.Remove(StartPosition, Length).Insert(StartPosition, newValue);

        bool TryReplaceAtFixedPosition(string sourceValue, string expectedValue, string replacement, out string result)
        {
            result = default!;
            if (StartPosition < 0 || Length < 0)
                return false;

            if (StartPosition > sourceValue.Length || Length > sourceValue.Length - StartPosition)
                return false;

            var slicedCurrentValue = sourceValue.AsSpan().Slice(StartPosition, Length);
            if (!slicedCurrentValue.Equals(expectedValue, StringComparison.Ordinal))
                return false;

            result = sourceValue.Remove(StartPosition, Length).Insert(StartPosition, replacement);
            return true;
        }
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
            return syntaxTree.Root;

        var updatedToken = SyntaxFactory.StringToken(updatedValue)
            .WithLeadingTrivia(stringNode.StringToken.LeadingTrivia)
            .WithTrailingTrivia(stringNode.StringToken.TrailingTrivia);

        return syntaxTree.Root.ReplaceNode(stringNode, new JsonStringSyntax(updatedToken));
    }
}
