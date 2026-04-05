using Meziantou.Framework.DependencyScanning.Internals;
using System.Text.Json;
using System.Text.Json.Nodes;

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
            var encoding = await StreamUtilities.GetEncodingAsync(stream, cancellationToken).ConfigureAwait(false);
            stream.Seek(0, SeekOrigin.Begin);

            string text;
            using (var textReader = StreamUtilities.CreateReader(stream, encoding))
            {
                text = await textReader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }

            var root = JsonNodeDocument.ParseNode(text);
            if (FindNodeByPath(root, JsonPath) is JsonValue token && token.TryGetValue<string>(out var tokenValue))
            {
                token.ReplaceWith(JsonValue.Create(UpdateTextValue(tokenValue, oldValue, newValue)));

                stream.SetLength(0);

                var textWriter = StreamUtilities.CreateWriter(stream, encoding);
                try
                {
                    await textWriter.WriteAsync(root.ToJsonString(new JsonSerializerOptions
                    {
                        WriteIndented = true,
                    })).ConfigureAwait(false);

                }
                finally
                {
                    await textWriter.DisposeAsync().ConfigureAwait(false);
                }
            }
            else
            {
                throw new DependencyScannerException("Dependency not found. File was probably modified since last scan.");
            }
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
            if (oldValue is not null && currentValue != oldValue)
                throw new DependencyScannerException($"Expected value '{oldValue}' does not match the current value '{currentValue}'. The file was probably modified since last scan.");

            return newValue;
        }

        if (oldValue is not null)
        {
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

            if (StartPosition > sourceValue.Length || StartPosition + Length > sourceValue.Length)
                return false;

            var slicedCurrentValue = sourceValue.AsSpan().Slice(StartPosition, Length);
            if (!slicedCurrentValue.Equals(expectedValue, StringComparison.Ordinal))
                return false;

            result = sourceValue.Remove(StartPosition, Length).Insert(StartPosition, replacement);
            return true;
        }
    }

    private static JsonNode? FindNodeByPath(JsonNode root, string path)
    {
        if (string.Equals(root.GetPath(), path, StringComparison.Ordinal))
            return root;

        if (root is JsonObject jsonObject)
        {
            foreach (var property in jsonObject)
            {
                if (property.Value is not null && FindNodeByPath(property.Value, path) is JsonNode node)
                    return node;
            }
        }
        else if (root is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null && FindNodeByPath(item, path) is JsonNode node)
                    return node;
            }
        }

        return null;
    }
}
