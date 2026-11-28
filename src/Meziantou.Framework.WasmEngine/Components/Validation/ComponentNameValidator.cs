using System.Globalization;
using Meziantou.Framework.WasmEngine.Components.Binary;

namespace Meziantou.Framework.WasmEngine.Components.Validation;

/// <summary>The kinds of the names of the imports and the exports.</summary>
internal enum ComponentNameKind
{
    Label,
    Constructor,
    Method,
    Static,
    Interface,
}

/// <summary>A valid name of an import or an export.</summary>
/// <param name="Kind">The kind of the name.</param>
/// <param name="Resource">The resource of an annotated name (<c>[constructor]R</c>, <c>[method]R.f</c>, <c>[static]R.f</c>), or null.</param>
/// <param name="Key">The name compared by the uniqueness rule (<c>strongly-unique</c>).</param>
internal readonly record struct ComponentName(ComponentNameKind Kind, string? Resource, string Key);

/// <summary>
/// The names of the component model (https://github.com/WebAssembly/component-model/blob/main/design/mvp/Explainer.md#import-and-export-definitions):
/// kebab-case labels, annotated names, interface names, versions, and the uniqueness of the names.
/// </summary>
internal static class ComponentNameValidator
{
    /// <summary>Validates a name and its attributes.</summary>
    /// <param name="name">The name.</param>
    /// <param name="attributes">The attributes of the name.</param>
    /// <param name="kind">The kind of the definition.</param>
    public static ComponentName Validate(string name, ComponentNameAttributes? attributes, ComponentExternKind kind)
    {
        var parsed = Parse(name, out var version);
        if (attributes is not null)
        {
            ValidateAttributes(name, parsed, version, attributes, kind);
        }
        else if (version is not null && !SemanticVersion.TryParse(version, out _, out var error))
        {
            throw Error("`" + version + "` is not valid: " + error);
        }

        if (parsed.Kind is ComponentNameKind.Constructor or ComponentNameKind.Method or ComponentNameKind.Static && kind != ComponentExternKind.Function)
            throw Error("`" + name + "` is not a func");

        return parsed;
    }

    /// <summary>Validates a label: a record field, a variant case, a flag, a parameter.</summary>
    public static void ValidateLabel(string label, string description)
    {
        if (label.Length == 0)
            throw Error(description + " name cannot be empty");

        if (!IsLabel(label))
            throw Error(description + " name `" + label + "` is not in kebab case");
    }

    /// <summary>Whether a text is a label (kebab case): fragments separated by <c>-</c>, in lower case or upper case.</summary>
    public static bool IsLabel(ReadOnlySpan<char> text) => IsKebab(text, lowercaseOnly: false);

    /// <summary>Whether a text is a sequence of lowercase words: a namespace or a package.</summary>
    public static bool IsWords(ReadOnlySpan<char> text) => IsKebab(text, lowercaseOnly: true);

    /// <summary>The key of a label compared by the uniqueness rule: without the hyphens, in lower case.</summary>
    public static string GetKey(string label) => label.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();

    private static bool IsKebab(ReadOnlySpan<char> text, bool lowercaseOnly)
    {
        var first = true;
        while (true)
        {
            var end = text.IndexOf('-');
            var fragment = end < 0 ? text : text[..end];
            if (!IsFragment(fragment, first, lowercaseOnly))
                return false;

            if (end < 0)
                return true;

            text = text[(end + 1)..];
            first = false;
        }
    }

    private static bool IsFragment(ReadOnlySpan<char> fragment, bool first, bool lowercaseOnly)
    {
        if (fragment.IsEmpty || (first && !char.IsAsciiLetter(fragment[0])))
            return false;

        var lower = false;
        var upper = false;
        foreach (var c in fragment)
        {
            if (char.IsAsciiLetterLower(c))
            {
                lower = true;
            }
            else if (char.IsAsciiLetterUpper(c))
            {
                upper = true;
            }
            else if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return !(lower && upper) && !(upper && lowercaseOnly);
    }

    private static ComponentName Parse(string name, out string? version)
    {
        version = null;
        if (name.StartsWith('[', StringComparison.Ordinal))
            return ParseAnnotated(name);

        var colon = name.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            if (!IsLabel(name))
                throw Error("`" + name + "` is not in kebab case");

            return new ComponentName(ComponentNameKind.Label, null, GetKey(name));
        }

        return ParseInterface(name, out version);
    }

    private static ComponentName ParseAnnotated(string name)
    {
        var close = name.IndexOf(']', StringComparison.Ordinal);
        var annotation = close < 0 ? name : name[..(close + 1)];
        var rest = close < 0 ? "" : name[(close + 1)..];
        switch (annotation)
        {
            case "[constructor]":
                if (!IsLabel(rest))
                    throw Error("`" + rest + "` is not in kebab case");

                return new ComponentName(ComponentNameKind.Constructor, rest, "[constructor]" + GetKey(rest));

            case "[method]" or "[static]":
                var dot = rest.IndexOf('.', StringComparison.Ordinal);
                if (dot < 0)
                    throw Error("failed to find `.` character");

                var resource = rest[..dot];
                var function = rest[(dot + 1)..];
                if (!IsLabel(resource))
                    throw Error("`" + resource + "` is not in kebab case");

                if (!IsLabel(function))
                    throw Error("`" + function + "` is not in kebab case");

                // [method]a.a is compared as a, and [method]a.b as a.b
                var resourceKey = GetKey(resource);
                var functionKey = GetKey(function);
                var key = string.Equals(resourceKey, functionKey, StringComparison.Ordinal) ? functionKey : resourceKey + "." + functionKey;
                return new ComponentName(annotation == "[method]" ? ComponentNameKind.Method : ComponentNameKind.Static, resource, key);

            default:
                throw Error("`" + name + "` is not in kebab case");
        }
    }

    private static ComponentName ParseInterface(string name, out string? version)
    {
        version = null;
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        var slash = name.IndexOf('/', StringComparison.Ordinal);
        var ns = name[..colon];
        if (!IsLabel(ns))
            throw Error("`" + ns + "` is not in kebab case");

        if (!IsWords(ns))
            throw Error("`" + name + "` is not a valid extern name");

        if (slash < 0)
            throw Error((name.IndexOf(':', colon + 1, StringComparison.Ordinal) >= 0 ? "expected `/` after package name" : "`" + name[(colon + 1)..] + "` is not in kebab case") + " in `" + name + "`");

        var package = name[(colon + 1)..slash];
        if (package.Contains(':', StringComparison.Ordinal))
            throw Error("expected `/` after package name in `" + name + "`");

        if (!IsLabel(package))
            throw Error("`" + package + "` is not in kebab case");

        if (!IsWords(package))
            throw Error("`" + name + "` is not a valid extern name");

        var rest = name[(slash + 1)..];
        var at = rest.IndexOf('@', StringComparison.Ordinal);
        var interfaceName = at < 0 ? rest : rest[..at];
        var trailing = interfaceName.IndexOf('/', StringComparison.Ordinal);
        if (trailing >= 0)
            throw Error("trailing characters found: `" + interfaceName[trailing..] + "`");

        if (!IsLabel(interfaceName))
            throw Error("`" + interfaceName + "` is not in kebab case");

        if (at >= 0)
        {
            version = rest[(at + 1)..];
        }

        return new ComponentName(ComponentNameKind.Interface, null, name.ToLowerInvariant());
    }

    private static void ValidateAttributes(string name, ComponentName parsed, string? version, ComponentNameAttributes attributes, ComponentExternKind kind)
    {
        if (attributes.Implements is { } implements)
        {
            if (kind != ComponentExternKind.Instance)
                throw Error("only instances can have an `implements`");

            if (parsed.Kind != ComponentNameKind.Label)
                throw Error("name `" + name + "` is not valid with `implements`");

            if (implements.Length == 0)
                throw Error("`` is not a valid name");

            if (!implements.Contains(':', StringComparison.Ordinal))
                throw Error("`" + implements + "` must be an interface");

            _ = ParseInterface(implements, out version);
        }

        if (attributes.VersionSuffix is not { } suffix)
        {
            if (version is not null && !SemanticVersion.TryParse(version, out _, out var error))
                throw Error("invalid interface version `" + version + "`: " + error);

            return;
        }

        if (suffix.Length == 0)
            throw Error("version suffix cannot be empty");

        if (kind != ComponentExternKind.Instance)
            throw Error("only instances can have a `version-suffix`");

        if (parsed.Kind != ComponentNameKind.Interface && attributes.Implements is null)
            throw Error("`version-suffix` requires an interface name or `implements`");

        if (version is null)
            throw Error("a version suffix requires the name to have a version");

        var full = version + suffix;
        if (!SemanticVersion.TryParse(full, out var semver, out var fullError))
            throw Error((attributes.Implements is null ? "" : "invalid interface version: ") + "`" + full + "` is not valid: " + fullError);

        if (!string.Equals(semver.GetCanonicalVersion(), version, StringComparison.Ordinal))
            throw Error("`" + version + "` is not the canonical version of `" + full + "`");
    }

    private static WasmCompileException Error(string message) => new(message);

    /// <summary>A semantic version (https://semver.org).</summary>
    private readonly record struct SemanticVersion(ulong Major, ulong Minor, ulong Patch, string? PreRelease)
    {
        /// <summary>The version that an interface name contains: the major version, or the minor or the patch version of a 0.x version.</summary>
        public string GetCanonicalVersion()
        {
            if (PreRelease is not null)
                return Format(Major) + "." + Format(Minor) + "." + Format(Patch) + "-" + PreRelease;

            if (Major > 0)
                return Format(Major);

            return Minor > 0 ? "0." + Format(Minor) : "0.0." + Format(Patch);
        }

        private static string Format(ulong value) => value.ToString(CultureInfo.InvariantCulture);

        public static bool TryParse(string text, out SemanticVersion version, out string error)
        {
            version = default;
            if (text.Length == 0)
            {
                error = "empty string";
                return false;
            }

            var position = 0;
            if (!TryParseNumber(text, ref position, out var major, out error) || !TryExpect(text, ref position, '.', out error) ||
                !TryParseNumber(text, ref position, out var minor, out error) || !TryExpect(text, ref position, '.', out error) ||
                !TryParseNumber(text, ref position, out var patch, out error))
            {
                return false;
            }

            string? preRelease = null;
            if (position < text.Length && text[position] == '-')
            {
                position++;
                if (!TryParseIdentifiers(text, ref position, numeric: true, out preRelease, out error))
                    return false;
            }

            if (position < text.Length && text[position] == '+')
            {
                position++;
                if (!TryParseIdentifiers(text, ref position, numeric: false, out _, out error))
                    return false;
            }

            if (position < text.Length)
            {
                error = "unexpected character " + Quote(text[position]);
                return false;
            }

            version = new SemanticVersion(major, minor, patch, preRelease);
            error = "";
            return true;
        }

        private static string Quote(char c) => "'" + c + "'";

        private static bool TryExpect(string text, ref int position, char expected, out string error)
        {
            if (position >= text.Length)
            {
                error = "unexpected end of input";
                return false;
            }

            if (text[position] != expected)
            {
                error = "unexpected character " + Quote(text[position]);
                return false;
            }

            position++;
            error = "";
            return true;
        }

        private static bool TryParseNumber(string text, ref int position, out ulong value, out string error)
        {
            value = 0;
            if (position >= text.Length)
            {
                error = "unexpected end of input";
                return false;
            }

            var start = position;
            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
            }

            if (position == start)
            {
                error = "unexpected character " + Quote(text[position]);
                return false;
            }

            if (position - start > 1 && text[start] == '0')
            {
                error = "invalid leading zero";
                return false;
            }

            if (!ulong.TryParse(text.AsSpan(start, position - start), NumberStyles.None, CultureInfo.InvariantCulture, out value))
            {
                error = "value out of range";
                return false;
            }

            error = "";
            return true;
        }

        private static bool TryParseIdentifiers(string text, ref int position, bool numeric, out string identifiers, out string error)
        {
            var start = position;
            while (true)
            {
                var segmentStart = position;
                while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] == '-'))
                {
                    position++;
                }

                if (position == segmentStart)
                {
                    identifiers = "";
                    error = "empty identifier segment";
                    return false;
                }

                var segment = text.AsSpan(segmentStart, position - segmentStart);
                if (numeric && segment.Length > 1 && segment[0] == '0' && !segment.ContainsAnyExcept("0123456789"))
                {
                    identifiers = "";
                    error = "invalid leading zero";
                    return false;
                }

                if (position < text.Length && text[position] == '.')
                {
                    position++;
                    continue;
                }

                identifiers = text[start..position];
                error = "";
                return true;
            }
        }
    }
}
