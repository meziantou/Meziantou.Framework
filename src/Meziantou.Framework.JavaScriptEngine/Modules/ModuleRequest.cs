using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.JavaScriptEngine.Modules;

/// <summary>A ModuleRequest Record (https://tc39.es/ecma262/#sec-modulerequest-record): a specifier and its import attributes (sorted by key).</summary>
internal sealed class ModuleRequest(string specifier, KeyValuePair<string, string>[] attributes) : IEquatable<ModuleRequest>
{
    public string Specifier { get; } = specifier;

    public KeyValuePair<string, string>[] Attributes { get; } = attributes;

    /// <summary>Creates a request; the attributes are sorted by key.</summary>
    public static ModuleRequest Create(string specifier, IEnumerable<KeyValuePair<string, string>> attributes)
    {
        var sorted = attributes.ToArray();
        Array.Sort(sorted, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return new ModuleRequest(specifier, sorted);
    }

    /// <summary>ModuleRequestsEqual(left, right)</summary>
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
