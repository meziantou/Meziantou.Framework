using System.Text.Json;

namespace Meziantou.Framework.Diagrams;

/// <summary>A table of a use case diagram that shows JSON data: a row per value, with its path, such as <c>items[0].name</c>.</summary>
public sealed class UseCaseJsonTable : UseCaseDiagramElement
{
    private string _json;

    /// <summary>Initializes a new instance of the <see cref="UseCaseJsonTable"/> class.</summary>
    /// <param name="id">The identifier, shown as the title of the table.</param>
    /// <param name="json">The data: a JSON object.</param>
    /// <exception cref="ArgumentException"><paramref name="json"/> is not a JSON object.</exception>
    public UseCaseJsonTable(string id, string json)
        : base(id)
    {
        Validate(json);
        _json = json;
    }

    /// <summary>Gets or sets the data: a JSON object, whose properties are shown in order.</summary>
    /// <exception cref="ArgumentException">The value is not a JSON object.</exception>
    public string Json
    {
        get => _json;
        set
        {
            Validate(value);
            _json = value;
        }
    }

    private static void Validate(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonValueKind kind;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            kind = document.RootElement.ValueKind;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            // A text with a lone surrogate cannot be transcoded to UTF-8: it is not valid JSON either
            throw new ArgumentException("The data is not valid JSON: " + exception.Message, nameof(json), exception);
        }

        if (kind is not JsonValueKind.Object)
            throw new ArgumentException("The data must be a JSON object.", nameof(json));
    }
}
