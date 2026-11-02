using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>
/// The optional content of a document (ISO 32000-2, 8.11): the groups the configuration of the rendering turns off, and
/// the marked-content sequences of the content that belong to them.
/// </summary>
internal sealed class OptionalContent
{
    // The nesting of membership dictionaries and visibility expressions
    private const int MaxDepth = 16;

    // The groups and usage application dictionaries read from a configuration
    private const int MaxGroups = 65536;

    private readonly HashSet<PdfDictionary> _off = new(ReferenceEqualityComparer.Instance);

    // The groups whose intent is not an intent of the configuration: they do not hide anything (8.11.2.1)
    private readonly HashSet<PdfDictionary> _ignored = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<bool> _sequences = new();
    private int _hidden;

    // The number of sequences open when the content stream being run started
    private int _streamStart;

    /// <summary>Initializes the state of the groups: the default configuration, the configuration and the intent of the options, then the states of the options.</summary>
    /// <param name="document">The document.</param>
    /// <param name="options">The options of the rendering.</param>
    /// <param name="zoom">The magnification of the rendering, in percent: 100 is one pixel per point (the Zoom usage of the groups).</param>
    public OptionalContent(PdfDocument document, PdfRenderOptions options, double zoom = 100)
    {
        var properties = document.Catalog.Get<PdfDictionary>(PdfName.Create("OCProperties"));
        var all = Groups(properties?.Get<PdfArray>(PdfName.Create("OCGs")));
        var defaultConfiguration = properties?.Get<PdfDictionary>(PdfNames.D);
        if (defaultConfiguration is not null)
        {
            Apply(defaultConfiguration, all);
        }

        // An alternate configuration applies over the default one: its BaseState Unchanged keeps its states (8.11.4.3)
        var configuration = defaultConfiguration;
        if (options.OptionalContentConfiguration is { } name && properties?.Get<PdfArray>(PdfName.Create("Configs")) is { } configurations)
        {
            foreach (var item in configurations.Take(MaxGroups))
            {
                if (PdfReference.ResolveIfReference(item) is PdfDictionary candidate && candidate.Get<PdfString>(PdfNames.Name)?.ToText() == name)
                {
                    Apply(candidate, all);
                    configuration = candidate;
                    break;
                }
            }
        }

        if (configuration is not null)
        {
            ApplyIntent(configuration, all);
            if (defaultConfiguration is not null)
            {
                ApplyUsage(defaultConfiguration, options.Intent, zoom);
            }

            if (!ReferenceEquals(configuration, defaultConfiguration))
            {
                ApplyUsage(configuration, options.Intent, zoom);
            }
        }

        if (options.OptionalContentGroupStates is { Count: > 0 } states)
        {
            foreach (var group in all)
            {
                if (group.Get<PdfString>(PdfNames.Name)?.ToText() is { } groupName && states.TryGetValue(groupName, out var isOn))
                {
                    SetState(group, isOn);
                }
            }
        }
    }

    private void SetState(PdfDictionary group, bool isOn)
    {
        if (isOn)
        {
            _off.Remove(group);
        }
        else
        {
            _off.Add(group);
        }
    }

    /// <summary>Applies the states of a configuration dictionary: its base state, then its /ON and /OFF arrays (8.11.4.3).</summary>
    private void Apply(PdfDictionary configuration, List<PdfDictionary> all)
    {
        switch (configuration.GetName(PdfName.Create("BaseState"))?.Value)
        {
            case "OFF":
                foreach (var group in all)
                {
                    _off.Add(group);
                }

                break;
            case "Unchanged":
                break;
            default:
                _off.Clear();
                break;
        }

        foreach (var group in Groups(configuration.Get<PdfArray>(PdfName.Create("ON"))))
        {
            _off.Remove(group);
        }

        foreach (var group in Groups(configuration.Get<PdfArray>(PdfName.Create("OFF"))))
        {
            _off.Add(group);
        }
    }

    /// <summary>Ignores the groups whose intent is not an intent of the configuration (8.11.2.1, 8.11.4.3): View by default, All for every intent.</summary>
    private void ApplyIntent(PdfDictionary configuration, List<PdfDictionary> all)
    {
        var intents = ReadNames(configuration.GetResolved(PdfName.Create("Intent")));
        if (intents.Contains("All"))
            return;

        foreach (var group in all)
        {
            var groupIntents = ReadNames(group.GetResolved(PdfName.Create("Intent")));
            if (!groupIntents.Contains("All") && !groupIntents.Overlaps(intents))
            {
                _ignored.Add(group);
            }
        }
    }

    private static HashSet<string> ReadNames(PdfObject? value) => value switch
    {
        PdfName name => new(StringComparer.Ordinal) { name.Value },
        PdfArray array => new(array.Take(MaxDepth).Select(PdfReference.ResolveIfReference).OfType<PdfName>().Select(name => name.Value), StringComparer.Ordinal),
        _ => new(StringComparer.Ordinal) { "View" },
    };

    /// <summary>
    /// Applies the usage application dictionaries (<c>/AS</c>) of the event of the intent (8.11.4.4): the groups they list
    /// take the state their usage dictionary gives for each category; a group is off when one of them is off.
    /// </summary>
    private void ApplyUsage(PdfDictionary configuration, PdfRenderIntent intent, double zoom)
    {
        if (configuration.Get<PdfArray>(PdfName.Create("AS")) is not { } applications)
            return;

        var eventName = intent switch
        {
            PdfRenderIntent.Print => "Print",
            PdfRenderIntent.Export => "Export",
            _ => "View",
        };
        foreach (var item in applications.Take(MaxGroups))
        {
            if (PdfReference.ResolveIfReference(item) is not PdfDictionary application || application.GetName(PdfName.Create("Event"))?.Value != eventName)
                continue;

            var categories = ReadNames(application.GetResolved(PdfName.Create("Category")));
            foreach (var group in Groups(application.Get<PdfArray>(PdfName.Create("OCGs"))))
            {
                if (group.Get<PdfDictionary>(PdfName.Create("Usage")) is not { } usage)
                    continue;

                bool? state = null;
                foreach (var category in categories)
                {
                    var value = category switch
                    {
                        "View" => ReadUsageState(usage, "View", "ViewState"),
                        "Print" => ReadUsageState(usage, "Print", "PrintState"),
                        "Export" => ReadUsageState(usage, "Export", "ExportState"),
                        "Zoom" when usage.Get<PdfDictionary>(PdfName.Create("Zoom")) is { } range => zoom >= range.GetNumber(PdfName.Create("min"), 0) && zoom < range.GetNumber(PdfName.Create("max"), double.PositiveInfinity),
                        _ => null,
                    };
                    if (value is { } known)
                    {
                        state = (state ?? true) && known;
                    }
                }

                if (state is { } isOn)
                {
                    SetState(group, isOn);
                }
            }
        }
    }

    private static bool? ReadUsageState(PdfDictionary usage, string category, string key) => usage.Get<PdfDictionary>(PdfName.Create(category))?.GetName(PdfName.Create(key))?.Value switch
    {
        "ON" => true,
        "OFF" => false,
        _ => null,
    };

    private bool IsOff(PdfDictionary group) => _off.Contains(group) && !_ignored.Contains(group);

    /// <summary>Gets whether the content is visible: not inside a marked-content sequence of a hidden group.</summary>
    public bool IsVisible => _hidden == 0;

    /// <summary>Indicates whether an optional content group or membership dictionary is visible (8.11.2).</summary>
    public bool IsVisibleGroup(PdfDictionary? dictionary, int depth = 0)
    {
        if (dictionary is null || depth > MaxDepth)
            return true;

        if (dictionary.GetName(PdfNames.Type)?.Value != "OCMD")
            return !IsOff(dictionary);

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
            "AllOn" => groups.All(group => !IsOff(group)),
            "AllOff" => groups.All(IsOff),
            "AnyOff" => groups.Any(IsOff),
            _ => groups.Any(group => !IsOff(group)),
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
        // An EMC without its BMC or BDC in the same content stream does not end a sequence of the content that runs it
        if (_sequences.Count > _streamStart && _sequences.Pop())
        {
            _hidden--;
        }
    }

    /// <summary>
    /// Starts a content stream: its marked-content sequences are its own (14.6). Returns the value <see cref="EndStream"/>
    /// takes, which closes the sequences the stream leaves open.
    /// </summary>
    public int BeginStream()
    {
        var previous = _streamStart;
        _streamStart = _sequences.Count;
        return previous;
    }

    /// <summary>Ends a content stream: the sequences it did not close are closed, and the sequences of the content that runs it apply again.</summary>
    public void EndStream(int previous)
    {
        while (_sequences.Count > _streamStart)
        {
            if (_sequences.Pop())
            {
                _hidden--;
            }
        }

        _streamStart = previous;
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
                PdfDictionary group => !IsOff(group),
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

    private static List<PdfDictionary> Groups(PdfArray? array) => array is null ? [] : [.. array.Take(MaxGroups).Select(PdfReference.ResolveIfReference).OfType<PdfDictionary>()];
}
