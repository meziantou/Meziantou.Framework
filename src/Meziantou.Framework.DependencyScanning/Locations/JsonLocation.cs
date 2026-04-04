using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.DependencyScanning;

internal sealed class JsonLocation : Location, ILocationLineInfo
{
    private readonly LineInfo _lineInfo;

    internal JsonLocation(ScanFileContext context, string jsonPath, LineInfo lineInfo)
        : this(context.FileSystem, context.FullPath, lineInfo, jsonPath, -1, -1)
    {
    }

    internal JsonLocation(ScanFileContext context, string jsonPath, LineInfo lineInfo, int column, int length)
        : this(context.FileSystem, context.FullPath, lineInfo, jsonPath, column, length)
    {

    }

    internal JsonLocation(IFileSystem fileSystem, string filePath, LineInfo lineInfo, string jsonPath, int column, int length)
        : base(fileSystem, filePath)
    {
        _lineInfo = lineInfo;
        JsonPath = jsonPath;
        StartPosition = column;
        Length = length;
    }

    public string JsonPath { get; }
    public int StartPosition { get; set; }
    public int Length { get; }

    public override bool IsUpdatable => true;
    int ILocationLineInfo.LineNumber => _lineInfo.LineNumber;
    int ILocationLineInfo.LinePosition => _lineInfo.LinePosition + Math.Clamp(StartPosition, 0, int.MaxValue);

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
            var matches = Meziantou.Framework.Json.JsonPath.Parse(JsonPath).Evaluate(root);
            if (matches.Count > 0 && matches[0].Value is JsonValue token && token.TryGetValue<string>(out var tokenValue))
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
        return string.Create(CultureInfo.InvariantCulture, $"{FilePath}:{JsonPath}:{_lineInfo}");
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
            var slicedCurrentValue = currentValue.AsSpan().Slice(StartPosition, Length);
            if (!slicedCurrentValue.Equals(oldValue, StringComparison.Ordinal))
                throw new DependencyScannerException($"Expected value '{oldValue}' does not match the current value '{slicedCurrentValue}'. The file was probably modified since last scan.");
        }

        if (currentValue is null)
            throw new DependencyScannerException("Current value is null. The file was probably modified since last scan.");

        return currentValue.Remove(StartPosition, Length).Insert(StartPosition, newValue);
    }
}
