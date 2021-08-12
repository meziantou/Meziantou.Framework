using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Meziantou.Framework.DependencyScanning.Internals;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meziantou.Framework.DependencyScanning;

internal sealed class JsonLocation : Location, ILocationLineInfo
{
    private readonly LineInfo _lineInfo;

    internal JsonLocation(string filePath, LineInfo lineInfo, string jsonPath)
        : base(filePath)
    {
        _lineInfo = lineInfo;
        JsonPath = jsonPath;
    }


    public string JsonPath { get; }

    public override bool IsUpdatable => true;
    int ILocationLineInfo.LineNumber => _lineInfo.LineNumber;
    int ILocationLineInfo.LinePosition => _lineInfo.LinePosition;

    internal protected override async Task UpdateAsync(Stream stream, string newVersion, CancellationToken cancellationToken)
    {
        var encoding = await StreamUtilities.GetEncodingAsync(stream, cancellationToken).ConfigureAwait(false);
        stream.Seek(0, SeekOrigin.Begin);

        string text;
        using (var textReader = StreamUtilities.CreateReader(stream, encoding))
        {
            text = await textReader.ReadToEndAsync().ConfigureAwait(false);
        }

        var jobject = JObject.Parse(text);
        if (jobject.SelectToken(JsonPath) is JValue token)
        {
            token.Value = newVersion;

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
            throw new DependencyScannerException("Dependency not found");
        }
    }

    public override string ToString()
    {
        return FormattableString.Invariant($"{FilePath}:{JsonPath}:{_lineInfo}");
    }
}
