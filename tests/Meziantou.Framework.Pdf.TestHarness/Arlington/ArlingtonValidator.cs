using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.TestHarness.Arlington;

/// <summary>
/// Checks the objects of a document against the Arlington PDF Model, from the trailer through the links of the model:
/// the type of each value, the keys that are required, the values a key may have, and whether a value must be an
/// indirect reference. The predicates of the model (<c>fn:IsRequired(...)</c>, <c>fn:Eval(...)</c>) are not
/// evaluated: a key whose requirement is a predicate is optional, and a value checked by a predicate is accepted.
/// </summary>
public sealed partial class ArlingtonValidator
{
    private const int MaxDepth = 64;

    private readonly ArlingtonModel _model;
    private readonly HashSet<(PdfObject, string)> _visited = new(new VisitedComparer());

    private ArlingtonValidator(ArlingtonModel model)
    {
        _model = model;
    }

    /// <summary>Validates a document from its trailer.</summary>
    /// <param name="document">The document.</param>
    /// <param name="model">The model, or <see langword="null"/> for the vendored one.</param>
    /// <returns>The problems: errors and unknown keys.</returns>
    public static IReadOnlyList<ArlingtonIssue> Validate(PdfDocument document, ArlingtonModel? model = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var validator = new ArlingtonValidator(model ?? ArlingtonModel.Default);
        var issues = new List<ArlingtonIssue>();
        validator.ValidateObject(document.Trailer, "FileTrailer", "trailer", issues, depth: 0);
        return issues;
    }

    private void ValidateObject(PdfObject value, string definitionName, string path, List<ArlingtonIssue> issues, int depth)
    {
        if (depth > MaxDepth || !_visited.Add((value, definitionName)))
            return;

        var definition = _model.Get(definitionName);
        if (definition is null)
            return;

        switch (value)
        {
            case PdfArray array when definition.IsArray:
                ValidateArray(array, definition, path, issues, depth);
                break;
            case PdfDictionary dictionary:
                ValidateDictionary(dictionary, definition, path, issues, depth);
                break;
            case PdfStream stream:
                ValidateDictionary(stream.Dictionary, definition, path, issues, depth);
                break;
        }
    }

    private void ValidateDictionary(PdfDictionary dictionary, ArlingtonObjectDefinition definition, string path, List<ArlingtonIssue> issues, int depth)
    {
        var wildcard = definition.Keys.FirstOrDefault(k => k.IsWildcard);
        foreach (var key in definition.Keys)
        {
            if (key.IsWildcard)
                continue;

            var raw = dictionary[PdfName.Create(key.Name)];
            if (raw is null || raw is PdfNull)
            {
                if (key.Required == true && !key.Inheritable)
                {
                    issues.Add(new ArlingtonIssue(ArlingtonIssueKind.MissingKey, path, $"{definition.Name}: the required key /{key.Name} is missing"));
                }

                continue;
            }

            ValidateValue(raw, key, definition, path + "/" + key.Name, issues, depth);
        }

        foreach (var entry in dictionary)
        {
            if (definition.Keys.Any(k => !k.IsWildcard && k.Name == entry.Key.Value))
                continue;

            if (wildcard is not null)
            {
                ValidateValue(entry.Value, wildcard, definition, path + "/" + entry.Key.Value, issues, depth);
            }
            else
            {
                issues.Add(new ArlingtonIssue(ArlingtonIssueKind.UnknownKey, path, $"{definition.Name}: the key /{entry.Key.Value} is not in the model"));
            }
        }
    }

    private void ValidateArray(PdfArray array, ArlingtonObjectDefinition definition, string path, List<ArlingtonIssue> issues, int depth)
    {
        var repeating = definition.Keys.Where(k => k.IsRepeating).ToList();
        var wildcard = definition.Keys.FirstOrDefault(k => k.IsWildcard);
        for (var i = 0; i < array.Count; i++)
        {
            var index = i.ToString(CultureInfo.InvariantCulture);
            var key = definition.Keys.FirstOrDefault(k => k.Name == index)
                ?? (repeating.Count > 0 ? repeating[i % repeating.Count] : null)
                ?? wildcard;
            if (key is null)
            {
                issues.Add(new ArlingtonIssue(ArlingtonIssueKind.UnknownKey, path, $"{definition.Name}: the array has more elements than the model ({array.Count})"));
                break;
            }

            ValidateValue(array[i], key, definition, path + "[" + index + "]", issues, depth);
        }

        foreach (var key in definition.Keys)
        {
            if (key.Required == true && int.TryParse(key.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var required) && required >= array.Count)
            {
                issues.Add(new ArlingtonIssue(ArlingtonIssueKind.MissingKey, path, $"{definition.Name}: the required element {key.Name} is missing"));
            }
        }
    }

    private void ValidateValue(PdfObject raw, ArlingtonKey key, ArlingtonObjectDefinition definition, string path, List<ArlingtonIssue> issues, int depth)
    {
        var value = raw is PdfReference reference ? reference.Resolve() : raw;
        var type = key.Types.FirstOrDefault(t => Matches(t.Name, value));
        if (type is null)
        {
            if (value is not PdfNull)
            {
                issues.Add(new ArlingtonIssue(ArlingtonIssueKind.WrongType, path, $"{definition.Name}: /{key.Name} is a {value.Kind}, the model allows {string.Join(", ", key.Types.Select(t => t.Name))}"));
            }

            return;
        }

        if (type.Indirect == ArlingtonIndirect.MustBeIndirect && raw is not PdfReference)
        {
            issues.Add(new ArlingtonIssue(ArlingtonIssueKind.WrongIndirection, path, $"{definition.Name}: /{key.Name} must be an indirect reference"));
        }
        else if (type.Indirect == ArlingtonIndirect.MustBeDirect && raw is PdfReference)
        {
            issues.Add(new ArlingtonIssue(ArlingtonIssueKind.WrongIndirection, path, $"{definition.Name}: /{key.Name} must be a direct object"));
        }

        if (type.PossibleValues.Count > 0 && !type.PossibleValues.Any(v => v.StartsWith("fn:", StringComparison.Ordinal)) && GetComparableValue(value) is { } comparable && !type.PossibleValues.Contains(comparable, StringComparer.Ordinal))
        {
            issues.Add(new ArlingtonIssue(ArlingtonIssueKind.WrongValue, path, $"{definition.Name}: /{key.Name} is {comparable}, the model allows {string.Join(", ", type.PossibleValues)}"));
        }

        if (type.Links.Count == 0 || type.Name is "name-tree" or "number-tree")
            return;

        // The value must match one of the linked definitions: the one without problem, else the one with the fewest
        List<ArlingtonIssue>? best = null;
        foreach (var link in type.Links)
        {
            var candidate = new List<ArlingtonIssue>();
            ValidateObject(value, link, path, candidate, depth + 1);
            if (candidate.Count == 0)
                return;

            if (best is null || candidate.Count < best.Count)
            {
                best = candidate;
            }
        }

        if (best is not null)
        {
            issues.AddRange(best);
        }
    }

    private static bool Matches(string type, PdfObject value)
    {
        return type switch
        {
            "integer" or "bitmask" => value is PdfInteger,
            "number" => value is PdfNumber,
            "boolean" => value is PdfBoolean,
            "name" => value is PdfName,
            "string" or "string-byte" or "string-text" or "string-ascii" => value is PdfString,
            "date" => value is PdfString date && DateRegex().IsMatch(System.Text.Encoding.Latin1.GetString(date.Bytes.Span)),
            "array" => value is PdfArray,
            "rectangle" => value is PdfArray { Count: 4 } rectangle && rectangle.All(item => PdfReferenceResolve(item) is PdfNumber),
            "matrix" => value is PdfArray { Count: 6 } matrix && matrix.All(item => PdfReferenceResolve(item) is PdfNumber),
            "dictionary" or "name-tree" or "number-tree" => value is PdfDictionary,
            "stream" => value is PdfStream,
            "null" => value is PdfNull,
            _ => false,
        };
    }

    private static PdfObject PdfReferenceResolve(PdfObject value) => value is PdfReference reference ? reference.Resolve() : value;

    private static string? GetComparableValue(PdfObject value)
    {
        return value switch
        {
            PdfName name => name.Value,
            PdfInteger integer => integer.Value.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    [GeneratedRegex(@"^(D:)?\d{4}", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DateRegex();

    private sealed class VisitedComparer : IEqualityComparer<(PdfObject Object, string Definition)>
    {
        public bool Equals((PdfObject Object, string Definition) x, (PdfObject Object, string Definition) y) => ReferenceEquals(x.Object, y.Object) && string.Equals(x.Definition, y.Definition, StringComparison.Ordinal);

        public int GetHashCode((PdfObject Object, string Definition) obj) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Object), StringComparer.Ordinal.GetHashCode(obj.Definition));
    }
}
