using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>
/// The optional content of a document (ISO 32000-2, 8.11): the groups the default configuration turns off, and the
/// marked-content sequences of the content that belong to them.
/// </summary>
internal sealed class OptionalContent
{
    // The nesting of membership dictionaries and visibility expressions
    private const int MaxDepth = 16;

    private readonly HashSet<PdfDictionary> _off = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<bool> _sequences = new();
    private int _hidden;

    public OptionalContent(PdfDocument document)
    {
        var properties = document.Catalog.Get<PdfDictionary>(PdfName.Create("OCProperties"));
        var configuration = properties?.Get<PdfDictionary>(PdfNames.D);
        if (configuration is null)
            return;

        // The groups are on, unless the base state is OFF; the /ON and /OFF arrays change them (8.11.4.3)
        var baseOff = configuration.GetName(PdfName.Create("BaseState"))?.Value == "OFF";
        var on = Groups(configuration.Get<PdfArray>(PdfName.Create("ON")));
        if (baseOff && properties!.Get<PdfArray>(PdfName.Create("OCGs")) is { } all)
        {
            foreach (var group in Groups(all))
            {
                if (!on.Contains(group))
                {
                    _off.Add(group);
                }
            }
        }

        foreach (var group in Groups(configuration.Get<PdfArray>(PdfName.Create("OFF"))))
        {
            _off.Add(group);
        }
    }

    /// <summary>Gets whether the content is visible: not inside a marked-content sequence of a hidden group.</summary>
    public bool IsVisible => _hidden == 0;

    /// <summary>Indicates whether an optional content group or membership dictionary is visible (8.11.2).</summary>
    public bool IsVisibleGroup(PdfDictionary? dictionary, int depth = 0)
    {
        if (dictionary is null || depth > MaxDepth)
            return true;

        if (dictionary.GetName(PdfNames.Type)?.Value != "OCMD")
            return !_off.Contains(dictionary);

        // A membership dictionary: a visibility expression, or a policy over its groups
        if (dictionary.Get<PdfArray>(PdfName.Create("VE")) is { } expression)
            return Evaluate(expression, depth + 1);

        var groups = dictionary.GetResolved(PdfName.Create("OCGs")) switch
        {
            PdfDictionary single => [single],
            PdfArray array => Groups(array),
            _ => [],
        };
        if (groups.Count == 0)
            return true;

        return dictionary.GetName(PdfName.Create("P"))?.Value switch
        {
            "AllOn" => groups.All(group => !_off.Contains(group)),
            "AllOff" => groups.All(_off.Contains),
            "AnyOff" => groups.Any(_off.Contains),
            _ => groups.Any(group => !_off.Contains(group)),
        };
    }

    public void BeginMarkedContent(PdfDictionary? optionalContent)
    {
        var hidden = optionalContent is not null && !IsVisibleGroup(optionalContent);
        _sequences.Push(hidden);
        if (hidden)
        {
            _hidden++;
        }
    }

    public void EndMarkedContent()
    {
        if (_sequences.Count > 0 && _sequences.Pop())
        {
            _hidden--;
        }
    }

    private bool Evaluate(PdfArray expression, int depth)
    {
        if (depth > MaxDepth || expression.Count == 0 || expression.GetResolved(0) is not PdfName op)
            return true;

        var operands = new List<bool>();
        for (var i = 1; i < expression.Count; i++)
        {
            operands.Add(expression.GetResolved(i) switch
            {
                PdfArray nested => Evaluate(nested, depth + 1),
                PdfDictionary group => !_off.Contains(group),
                _ => true,
            });
        }

        return op.Value switch
        {
            "And" => operands.All(value => value),
            "Or" => operands.Any(value => value),
            "Not" => operands.Count > 0 && !operands[0],
            _ => true,
        };
    }

    private static List<PdfDictionary> Groups(PdfArray? array) => array is null ? [] : [.. array.Select(PdfReference.ResolveIfReference).OfType<PdfDictionary>()];
}
