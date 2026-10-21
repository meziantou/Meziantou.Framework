using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.JavaScriptEngine.Modules;

/// <summary>
/// A ModuleRequest Record (https://tc39.es/ecma262/#sec-modulerequest-record): a specifier, its import attributes (sorted
/// by key) and its phase (https://tc39.es/proposal-defer-import-eval/).
/// </summary>
internal sealed class ModuleRequest(string specifier, KeyValuePair<string, string>[] attributes, ModulePhase phase = ModulePhase.Evaluation) : IEquatable<ModuleRequest>
{
    public string Specifier { get; } = specifier;

    public KeyValuePair<string, string>[] Attributes { get; } = attributes;

    /// <summary>[[Phase]]: whether the module is evaluated with its importer, or deferred (<c>import defer</c>).</summary>
    public ModulePhase Phase { get; } = phase;

    /// <summary>Creates a request; the attributes are sorted by key.</summary>
    public static ModuleRequest Create(string specifier, IEnumerable<KeyValuePair<string, string>> attributes, ModulePhase phase = ModulePhase.Evaluation)
    {
        var sorted = attributes.ToArray();
        Array.Sort(sorted, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return new ModuleRequest(specifier, sorted, phase);
    }

    /// <summary>ModuleRequestsEqual(left, right): the phase is not compared (the same module is loaded for each phase).</summary>
    public bool Equals([NotNullWhen(true)] ModuleRequest? other)
    {
        if (other is null || !string.Equals(Specifier, other.Specifier, StringComparison.Ordinal) || Attributes.Length != other.Attributes.Length)
            return false;

        for (var i = 0; i < Attributes.Length; i++)
        {
            if (!string.Equals(Attributes[i].Key, other.Attributes[i].Key, StringComparison.Ordinal) || !string.Equals(Attributes[i].Value, other.Attributes[i].Value, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as ModuleRequest);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Specifier);

    /// <summary>Gets the value of the <c>type</c> attribute, if any.</summary>
    public string? Type
    {
        get
        {
            foreach (var attribute in Attributes)
            {
                if (attribute.Key == "type")
                    return attribute.Value;
            }

            return null;
        }
    }
}

/// <summary>The [[Phase]] of a module request or of a namespace object (https://tc39.es/proposal-defer-import-eval/).</summary>
internal enum ModulePhase
{
    /// <summary>The module is evaluated before its importer.</summary>
    Evaluation,

    /// <summary>The module is evaluated on the first access to an export of its deferred namespace object.</summary>
    Defer,

    /// <summary>The module is loaded, not linked nor evaluated: the import is the source of the module (https://tc39.es/proposal-source-phase-imports/).</summary>
    Source,
}
