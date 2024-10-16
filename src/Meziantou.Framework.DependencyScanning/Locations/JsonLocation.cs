using System.Globalization;
using Meziantou.Framework.DependencyScanning.Internals;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meziantou.Framework.DependencyScanning;

internal sealed class JsonLocation : Location, ILocationLineInfo
{
    private readonly LineInfo _lineInfo;

    internal JsonLocation(ScanFileContext context, JToken token)
        : this(context.FileSystem, context.FullPath, LineInfo.FromJToken(token), token.Path, -1, -1)
    {
    }

    internal JsonLocation(ScanFileContext context, JToken token, int column, int length)
        : this(context.FileSystem, context.FullPath, LineInfo.FromJToken(token), token.Path, column, length)
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

            var jobject = JObject.Parse(text);
            if (jobject.SelectToken(JsonPath) is JValue token)
            {
                if (token.Value is not string tokenValue)
                    throw new DependencyScannerException("Expected value not found at the location. File was probably modified since last scan.");

                token.Value = UpdateTextValue(tokenValue, oldValue, newValue);

                stream.SetLength(0);

                var textWriter = StreamUtilities.CreateWriter(stream, encoding);
                try
                {
                    using var jsonWriter = new JsonTextWriter(textWriter)
                    {
                        Formatting = Formatting.Indented,
                    };

                    await jobject.WriteToAsync(jsonWriter, cancellationToken).ConfigureAwait(false);

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
                throw new DependencyScannerException($"Expected value '{oldValue}' does not match the current value '{slicedCurrentValue.ToString()}'. The file was probably modified since last scan.");
        }

        return currentValue
            .Remove(StartPosition, Length)
            .Insert(StartPosition, newValue);
    }
}
