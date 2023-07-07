using System.Globalization;
using Meziantou.Framework.DependencyScanning.Internals;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meziantou.Framework.DependencyScanning;

internal sealed class JsonLocation : Location, ILocationLineInfo
{
    private readonly LineInfo _lineInfo;

    internal JsonLocation(ScanFileContext context, JToken token)
        : this(context.FileSystem, context.FullPath, LineInfo.FromJToken(token), token.Path)
    {

    }

    internal JsonLocation(IFileSystem fileSystem, string filePath, LineInfo lineInfo, string jsonPath)
        : base(fileSystem, filePath)
    {
        _lineInfo = lineInfo;
        JsonPath = jsonPath;
    }

    public string JsonPath { get; }

    public override bool IsUpdatable => true;
    int ILocationLineInfo.LineNumber => _lineInfo.LineNumber;
    int ILocationLineInfo.LinePosition => _lineInfo.LinePosition;

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
                if (oldValue != null)
                {
                    if (token.Value is not string existingValue || existingValue != oldValue)
                        throw new DependencyScannerException("Expected value not found at the location. File was probably modified since last scan.");
                }

                token.Value = newValue;

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
}
