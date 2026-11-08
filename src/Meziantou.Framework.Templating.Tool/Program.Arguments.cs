using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;

namespace Meziantou.Framework.Templating.Tool;

internal static partial class Program
{
    /// <summary>
    /// Reads the arguments of the templates: the properties of the root object of <c>--arguments-file</c>, then the
    /// <c>--arg</c> values, in order. A later value replaces an earlier one with the same name.
    /// </summary>
    private static async Task<IReadOnlyList<TemplateArgumentValue>?> ReadArgumentsAsync(RenderOptions options, List<string> errors, CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, TemplateArgumentValue>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(options.ArgumentsFile))
        {
            var path = FullPath.FromPath(options.ArgumentsFile);
            if (!File.Exists(path))
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"The arguments file '{path}' does not exist"));
                return null;
            }

            JsonDocument document;
            try
            {
                var stream = File.OpenRead(path);
                await using (stream.ConfigureAwait(false))
                {
                    document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (JsonException ex)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"{path}: error: The arguments file is not valid JSON: {ex.Message}"));
                return null;
            }

            using (document)
            {
                if (document.RootElement.ValueKind is not JsonValueKind.Object)
                {
                    errors.Add(string.Create(CultureInfo.InvariantCulture, $"{path}: error: The root of the arguments file must be an object"));
                    return null;
                }

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!TryValidateArgumentName(property.Name, errors))
                        continue;

                    if (!TryConvertJsonValue(property.Value, out var type, out var value))
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{path}: error: The number of the argument '{property.Name}' is out of range"));
                        continue;
                    }

                    arguments[property.Name] = new TemplateArgumentValue(property.Name, type, value);
                }
            }
        }

        foreach (var argument in options.Arguments)
        {
            var separatorIndex = argument.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"The argument '{argument}' must be written as name=value"));
                continue;
            }

            var name = argument[..separatorIndex];
            if (!TryValidateArgumentName(name, errors))
                continue;

            arguments[name] = new TemplateArgumentValue(name, typeof(string), argument[(separatorIndex + 1)..]);
        }

        if (errors.Count > 0)
            return null;

        return [.. arguments.Values];
    }

    private static bool TryValidateArgumentName(string name, List<string> errors)
    {
        // The name becomes a parameter of the generated C# method
        if (SyntaxFacts.IsValidIdentifier(name) && SyntaxFacts.GetKeywordKind(name) is SyntaxKind.None)
            return true;

        errors.Add(string.Create(CultureInfo.InvariantCulture, $"The argument name '{name}' is not a valid C# identifier"));
        return false;
    }

    /// <summary>
    /// Converts a JSON value to the value of a typed argument: a string is a <see cref="string"/>, a boolean a
    /// <see cref="bool"/>, an integer a <see cref="long"/>, another number a <see cref="decimal"/> (or a <see cref="double"/>
    /// when it does not fit), <c>null</c> an <see cref="object"/>, and an object or an array a <see cref="JsonElement"/>.
    /// </summary>
    private static bool TryConvertJsonValue(JsonElement element, [NotNullWhen(true)] out Type? type, out object? value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                (type, value) = (typeof(string), element.GetString());
                return true;

            case JsonValueKind.True or JsonValueKind.False:
                (type, value) = (typeof(bool), element.GetBoolean());
                return true;

            case JsonValueKind.Number when element.TryGetInt64(out var int64Value):
                (type, value) = (typeof(long), int64Value);
                return true;

            case JsonValueKind.Number when element.TryGetDecimal(out var decimalValue):
                (type, value) = (typeof(decimal), decimalValue);
                return true;

            case JsonValueKind.Number when element.TryGetDouble(out var doubleValue) && double.IsFinite(doubleValue):
                (type, value) = (typeof(double), doubleValue);
                return true;

            case JsonValueKind.Null:
                (type, value) = (typeof(object), null);
                return true;

            case JsonValueKind.Object or JsonValueKind.Array:
                (type, value) = (typeof(JsonElement), element.Clone());
                return true;

            default:
                (type, value) = (null, null);
                return false;
        }
    }

    private sealed record TemplateArgumentValue(string Name, Type Type, object? Value);
}
