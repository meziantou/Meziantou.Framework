namespace Meziantou.Framework.JavaScriptEngine.Modules;

/// <summary>
/// An ImportedNames Record (https://tc39.es/proposal-deferred-reexports/#importednames-record): the names imported from a
/// module through a module request, used to load, link and evaluate only the <c>export defer</c> declarations that are
/// needed. A null list is ~all~ (every name).
/// </summary>
internal sealed class ImportedNames(List<string>? direct, List<string>? @namespace)
{
    /// <summary>[[Direct]]: the names imported as bindings (<c>import { a } from</c>), or null for all of them.</summary>
    public List<string>? Direct { get; set; } = direct;

    /// <summary>[[Namespace]]: the names imported through a namespace object (<c>import * as ns from</c>), or null for all of them.</summary>
    public List<string>? Namespace { get; set; } = @namespace;

    public static ImportedNames Empty() => new([], []);

    /// <summary>AllImportedNames(moduleRequest)</summary>
    public List<string>? All => Merge(Direct, Namespace);

    public ImportedNames Copy() => new(Direct is null ? null : [.. Direct], Namespace is null ? null : [.. Namespace]);

    /// <summary>MergeImportedNames(a, b)</summary>
    public static List<string>? Merge(List<string>? a, List<string>? b)
    {
        if (a is null || b is null)
            return null;

        var merged = new List<string>(a);
        foreach (var name in b)
        {
            if (!merged.Contains(name, StringComparer.Ordinal))
            {
                merged.Add(name);
            }
        }

        return merged;
    }

    /// <summary>ExcludeImportedNames(a, b): the names of <paramref name="a"/> that are not in <paramref name="b"/>.</summary>
    public static List<string>? Exclude(List<string>? a, List<string>? b)
    {
        if (b is null)
            return [];

        if (a is null)
            return null;

        return a.FindAll(name => !b.Contains(name, StringComparer.Ordinal));
    }

    /// <summary>Gets whether the names contain <paramref name="name"/> (always true for all the names).</summary>
    public static bool Contains(List<string>? names, string name) => names is null || names.Contains(name, StringComparer.Ordinal);
}

/// <summary>
/// The names already requested from each module while loading, linking or evaluating a graph (the [[Visited]] of a
/// GraphLoadingState and the previouslyImportedNames lists of https://tc39.es/proposal-deferred-reexports/).
/// </summary>
internal sealed class PreviouslyImportedNames
{
    private readonly Dictionary<JsSourceTextModule, ImportedNames> _names = [];
    private readonly List<JsSourceTextModule> _order = [];

    public IReadOnlyList<JsSourceTextModule> Modules => _order;

    public bool Contains(JsSourceTextModule module) => _names.ContainsKey(module);

    /// <summary>Adds the module with no names, if it is not already in the list.</summary>
    public bool TryAdd(JsSourceTextModule module)
    {
        if (!_names.TryAdd(module, ImportedNames.Empty()))
            return false;

        _order.Add(module);
        return true;
    }

    public ImportedNames Get(JsSourceTextModule module) => _names[module];

    /// <summary>CopyPreviouslyImportedNames(previouslyImportedNames)</summary>
    public PreviouslyImportedNames Copy()
    {
        var copy = new PreviouslyImportedNames();
        foreach (var module in _order)
        {
            copy._names.Add(module, _names[module].Copy());
            copy._order.Add(module);
        }

        return copy;
    }
}
