using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Internals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.IconifySourceGeneration;

[Generator]
public sealed class IconifySourceGenerator : IIncrementalGenerator
{
    private const string Category = "Meziantou.Framework.Iconify.SourceGeneration";

    internal static readonly DiagnosticDescriptor InvalidIconSet = new(
        id: "MFICONIFY001",
        title: "Invalid icon set",
        messageFormat: "The file '{0}' is not a valid Iconify icon set: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor CannotReadIconSet = new(
        id: "MFICONIFY002",
        title: "Cannot read the icon set",
        messageFormat: "The file '{0}' cannot be read",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateClassName = new(
        id: "MFICONIFY003",
        title: "Duplicate icon set name",
        messageFormat: "The icon set '{0}' has the same name '{1}' as the icon set '{2}'. Set the ClassName metadata of one of the IconifyIconSet items.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor FilterMatchesNoIcon = new(
        id: "MFICONIFY004",
        title: "The icon filter matches no icon",
        messageFormat: "The pattern '{0}' of the Icons metadata matches no icon of the icon set '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // Members of the Iconify class and of object, which an extension property cannot hide
    private static readonly string[] ReservedClassNames = ["Load", "LoadAsync", "Parse", "Equals", "ReferenceEquals"];

    // Members of object, which an icon property cannot hide
    private static readonly string[] ReservedIconNames = ["Equals", "GetHashCode", "GetType", "ToString", "MemberwiseClone", "Finalize"];

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var visibility = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue("build_property.IconifyVisibility", out var value) && string.Equals(value?.Trim(), "public", StringComparison.OrdinalIgnoreCase) ? "public" : "internal");

        var inputs = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, cancellationToken) => CreateInput(pair.Left, pair.Right, cancellationToken))
            .Where(static input => input is not null)
            .Combine(visibility)
            .Select(static (pair, cancellationToken) => Generate(pair.Left!, pair.Right, cancellationToken));

        context.RegisterSourceOutput(inputs.Collect(), static (context, results) =>
        {
            var classNames = new Dictionary<string, GenerationResult>(StringComparer.Ordinal);
            foreach (var result in results.OrderBy(result => result.FilePath, StringComparer.Ordinal))
            {
                foreach (var diagnostic in result.Diagnostics)
                {
                    context.ReportDiagnostic(diagnostic.ToDiagnostic());
                }

                if (result.ClassName is null || result.Source is null)
                    continue;

                if (classNames.TryGetValue(result.ClassName, out var existing))
                {
                    context.ReportDiagnostic(Diagnostic.Create(DuplicateClassName, CreateLocation(result.FilePath), result.FilePath, result.ClassName, existing.FilePath));
                    continue;
                }

                classNames.Add(result.ClassName, result);
                context.AddSource("Iconify." + result.ClassName + ".g.cs", SourceText.From(result.Source, Encoding.UTF8));
            }
        });
    }

    private static GenerationInput? CreateInput(AdditionalText text, AnalyzerConfigOptionsProvider optionsProvider, CancellationToken cancellationToken)
    {
        var options = optionsProvider.GetOptions(text);
        if (!options.TryGetValue("build_metadata.AdditionalFiles.IconifyIconSet", out var isIconSet) || !string.Equals(isIconSet, "true", StringComparison.OrdinalIgnoreCase))
            return null;

        options.TryGetValue("build_metadata.AdditionalFiles.IconifyClassName", out var className);
        options.TryGetValue("build_metadata.AdditionalFiles.IconifyIcons", out var icons);
        var content = text.GetText(cancellationToken)?.ToString();
        return new GenerationInput(text.Path, content, NullIfEmpty(className), NullIfEmpty(icons));
    }

    private static string? NullIfEmpty(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static GenerationResult Generate(GenerationInput input, string visibility, CancellationToken cancellationToken)
    {
        if (input.Content is null)
            return new GenerationResult(input.FilePath, ClassName: null, Source: null, new[] { new DiagnosticInfo(CannotReadIconSet, input.FilePath, input.FilePath) });

        IconifyIconSetData data;
        try
        {
            data = IconifyIconSetReader.Read(input.Content);
        }
        catch (FormatException ex)
        {
            return new GenerationResult(input.FilePath, ClassName: null, Source: null, new[] { new DiagnosticInfo(InvalidIconSet, input.FilePath, input.FilePath, ex.Message) });
        }

        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new List<DiagnosticInfo>();
        var icons = data.Icons;
        if (input.Icons is not null)
        {
            var patterns = input.Icons.Split([';', ','], StringSplitOptions.RemoveEmptyEntries).Select(pattern => pattern.Trim()).Where(pattern => pattern.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            var matched = new HashSet<string>(StringComparer.Ordinal);
            icons = data.Icons.Where(icon =>
            {
                var isMatch = false;
                foreach (var pattern in patterns)
                {
                    if (IsMatch(icon.Name, pattern))
                    {
                        matched.Add(pattern);
                        isMatch = true;
                    }
                }

                return isMatch;
            }).ToList();

            foreach (var pattern in patterns)
            {
                if (!matched.Contains(pattern))
                {
                    diagnostics.Add(new DiagnosticInfo(FilterMatchesNoIcon, input.FilePath, pattern, data.Prefix));
                }
            }
        }

        var className = IconifyNaming.ToIdentifier(input.ClassName ?? data.Prefix);
        if (ReservedClassNames.Contains(className, StringComparer.Ordinal))
        {
            className += "IconSet";
        }

        var source = GenerateSource(data, icons, className, visibility);
        return new GenerationResult(input.FilePath, className, source, diagnostics.ToArray());
    }

    private static string GenerateSource(IconifyIconSetData data, IReadOnlyList<IconifyIconData> icons, string className, string visibility)
    {
        var extensionsTypeName = "Iconify" + className + "Extensions";
        var iconSetTypeName = "Iconify" + className + "IconSet";
        var iconsTypeName = "Iconify" + className + "Icons";
        var displayName = data.Info?.Name ?? data.Prefix;
        var identifiers = IconifyNaming.CreateUniqueIdentifiers(icons.Select(icon => icon.Name), [.. ReservedIconNames, iconsTypeName]);

        var sb = new StringBuilder(icons.Sum(icon => icon.Body.Length + icon.Name.Length + 200) + 2048);
        sb.Append("// <auto-generated/>\n");
        sb.Append("#nullable enable\n");
        sb.Append('\n');
        sb.Append("namespace Meziantou.Framework\n");
        sb.Append("{\n");

        sb.Append("    /// <summary>Provides the ").Append(EscapeXml(displayName)).Append(" icon set (<c>").Append(EscapeXml(data.Prefix)).Append("</c>).</summary>\n");
        AppendGeneratedCodeAttribute(sb);
        sb.Append("    ").Append(visibility).Append(" static class ").Append(extensionsTypeName).Append('\n');
        sb.Append("    {\n");
        sb.Append("        extension(global::Meziantou.Framework.Iconify)\n");
        sb.Append("        {\n");
        sb.Append("            /// <summary>Gets the ").Append(EscapeXml(displayName)).Append(" icon set (<c>").Append(EscapeXml(data.Prefix)).Append("</c>).</summary>\n");
        sb.Append("            public static global::Meziantou.Framework.").Append(iconSetTypeName).Append(' ').Append(className).Append(" => default;\n");
        sb.Append("        }\n");
        sb.Append("    }\n");
        sb.Append('\n');

        sb.Append("    /// <summary>Represents the ").Append(EscapeXml(displayName)).Append(" icon set (<c>").Append(EscapeXml(data.Prefix)).Append("</c>).</summary>\n");
        AppendGeneratedCodeAttribute(sb);
        sb.Append("    ").Append(visibility).Append(" readonly struct ").Append(iconSetTypeName).Append('\n');
        sb.Append("    {\n");
        sb.Append("        /// <summary>Gets the prefix of the icon set.</summary>\n");
        sb.Append("        public string Prefix => ").Append(ToLiteral(data.Prefix)).Append(";\n");
        sb.Append('\n');
        sb.Append("        /// <summary>Gets the description of the icon set: its name, author and license.</summary>\n");
        sb.Append("        public global::Meziantou.Framework.IconifyIconSetInfo? Info => ");
        AppendInfo(sb, data.Info);
        sb.Append(";\n");
        sb.Append('\n');
        sb.Append("        /// <summary>Gets the icons of the icon set.</summary>\n");
        sb.Append("        public global::Meziantou.Framework.").Append(iconsTypeName).Append(" Icons => default;\n");
        sb.Append("    }\n");
        sb.Append('\n');

        sb.Append("    /// <summary>Provides the icons of the ").Append(EscapeXml(displayName)).Append(" icon set (<c>").Append(EscapeXml(data.Prefix)).Append("</c>).</summary>\n");
        AppendGeneratedCodeAttribute(sb);
        sb.Append("    ").Append(visibility).Append(" readonly struct ").Append(iconsTypeName).Append('\n');
        sb.Append("    {\n");

        var first = true;
        foreach (var icon in icons.OrderBy(icon => identifiers[icon.Name], StringComparer.Ordinal))
        {
            if (!first)
            {
                sb.Append('\n');
            }

            first = false;
            sb.Append("        /// <summary>Gets the icon <c>").Append(EscapeXml(data.Prefix)).Append(':').Append(EscapeXml(icon.Name)).Append("</c>.</summary>\n");
            if (icon.Hidden)
            {
                sb.Append("        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]\n");
            }

            sb.Append("        public global::Meziantou.Framework.IconifyIcon ").Append(identifiers[icon.Name]).Append(" => new(")
                .Append(ToLiteral(data.Prefix)).Append(", ")
                .Append(ToLiteral(icon.Name)).Append(", ")
                .Append(ToBodyExpression(icon.Body)).Append(", ")
                .Append(ToNumberLiteral(icon.Left)).Append(", ")
                .Append(ToNumberLiteral(icon.Top)).Append(", ")
                .Append(ToNumberLiteral(icon.Width)).Append(", ")
                .Append(ToNumberLiteral(icon.Height));
            if (icon.Hidden)
            {
                sb.Append(", isHidden: true");
            }

            sb.Append(");\n");
        }

        sb.Append("    }\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    private static void AppendGeneratedCodeAttribute(StringBuilder sb)
    {
        sb.Append("    [global::System.CodeDom.Compiler.GeneratedCode(\"Meziantou.Framework.Iconify.SourceGenerator\", \"1.0.0\")]\n");
    }

    private static void AppendInfo(StringBuilder sb, IconifyIconSetInfoData? info)
    {
        if (info is null)
        {
            sb.Append("null");
            return;
        }

        sb.Append("new global::Meziantou.Framework.IconifyIconSetInfo { ");
        AppendProperty(sb, "Name", info.Name);
        AppendProperty(sb, "AuthorName", info.AuthorName);
        AppendProperty(sb, "AuthorUrl", info.AuthorUrl);
        AppendProperty(sb, "LicenseTitle", info.LicenseTitle);
        AppendProperty(sb, "LicenseSpdx", info.LicenseSpdx);
        AppendProperty(sb, "LicenseUrl", info.LicenseUrl);
        if (info.Total is { } total)
        {
            sb.Append("Total = ").Append(total.ToString(CultureInfo.InvariantCulture)).Append(", ");
        }

        AppendProperty(sb, "Version", info.Version);
        AppendProperty(sb, "Category", info.Category);
        if (info.Palette is { } palette)
        {
            sb.Append("Palette = ").Append(palette ? "true" : "false").Append(", ");
        }

        sb.Append('}');

        static void AppendProperty(StringBuilder sb, string name, string? value)
        {
            if (value is not null)
            {
                sb.Append(name).Append(" = ").Append(ToLiteral(value)).Append(", ");
            }
        }
    }

    private static bool IsMatch(string name, string pattern)
    {
        // Simple glob: '*' matches any sequence of characters
        var nameIndex = 0;
        var patternIndex = 0;
        var starIndex = -1;
        var matchIndex = 0;
        while (nameIndex < name.Length)
        {
            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                matchIndex = nameIndex;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == name[nameIndex])
            {
                patternIndex++;
                nameIndex++;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                nameIndex = ++matchIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    // The bodies are UTF-8 literals: they are stored as data in the assembly, while string literals share a heap limited to 16 MB
    // that a single large icon set fills. A body with a lone surrogate cannot be encoded in UTF-8, so it stays a string literal.
    private static string ToBodyExpression(string body)
    {
        if (HasLoneSurrogate(body))
            return ToLiteral(body);

        return "global::System.Text.Encoding.UTF8.GetString(" + ToLiteral(body) + "u8)";
    }

    private static bool HasLoneSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(value[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string ToNumberLiteral(double value) => IconifySvgBuilder.FormatNumber(value);

    internal static string ToLiteral(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    // Control characters, line separators (not allowed in a literal) and surrogates (a lone surrogate cannot be encoded in UTF-8)
                    if (c < 0x20 || c is '\u007F' or '\u0085' or '\u2028' or '\u2029' || char.IsSurrogate(c))
                    {
                        sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    private static string EscapeXml(string value)
    {
        return value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static Location CreateLocation(string filePath) => Location.Create(filePath, default, default);

    private sealed record GenerationInput(string FilePath, string? Content, string? ClassName, string? Icons);

    private sealed record GenerationResult(string FilePath, string? ClassName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics);

    private sealed record DiagnosticInfo
    {
        public DiagnosticInfo(DiagnosticDescriptor descriptor, string filePath, params string[] arguments)
        {
            Id = descriptor.Id;
            FilePath = filePath;
            Arguments = arguments;
        }

        public string Id { get; }
        public string FilePath { get; }
        public EquatableArray<string> Arguments { get; }

        public Diagnostic ToDiagnostic()
        {
            var descriptor = Id switch
            {
                "MFICONIFY001" => InvalidIconSet,
                "MFICONIFY002" => CannotReadIconSet,
                "MFICONIFY003" => DuplicateClassName,
                _ => FilterMatchesNoIcon,
            };

            return Diagnostic.Create(descriptor, CreateLocation(FilePath), [.. Arguments]);
        }
    }

    private readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
        where T : IEquatable<T>
    {
        private readonly T[]? _values;

        public EquatableArray(T[] values) => _values = values;

        public static implicit operator EquatableArray<T>(T[] values) => new(values);

        public bool Equals(EquatableArray<T> other) => ((ReadOnlySpan<T>)(_values ?? [])).SequenceEqual(other._values ?? []);

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is EquatableArray<T> other && Equals(other);

        public override int GetHashCode()
        {
            var hash = 0;
            foreach (var value in _values ?? [])
            {
                hash = (hash * 31) + (value?.GetHashCode() ?? 0);
            }

            return hash;
        }

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_values ?? [])).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
