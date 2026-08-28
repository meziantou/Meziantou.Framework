#pragma warning disable MA0028 // Optimize StringBuilder would make the code harder to read
#pragma warning disable MA0101 // String contains an implicit end of line character
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.ResxSourceGenerator;

[Generator]
public sealed partial class ResxGenerator : IIncrementalGenerator
{
    private const string ResxGeneratorNamespace = "https://meziantou.net/meziantou.framework/resxgenerator";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var compilationProvider = context.CompilationProvider.Select(static (compilation, cancellationToken) =>
                    (compilation.AssemblyName, SupportNullableReferenceTypes: compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute") is not null));

        var resxProvider = context.AdditionalTextsProvider.Where(text => text.Path.EndsWith(".resx", StringComparison.OrdinalIgnoreCase)).Collect();

        context.RegisterSourceOutput(
            source: context.AnalyzerConfigOptionsProvider.Combine(compilationProvider.Combine(resxProvider)),
            action: (ctx, source) => Execute(ctx, source.Left, source.Right.Left.AssemblyName, source.Right.Left.SupportNullableReferenceTypes, source.Right.Right));
    }

    private static void Execute(SourceProductionContext context, AnalyzerConfigOptionsProvider options, string? assemblyName, bool supportNullableReferenceTypes, ImmutableArray<AdditionalText> files)
    {
        // Group additional file by resource kind ((a.resx, a.en.resx, a.en-us.resx), (b.resx, b.en-us.resx))
        var resxGroups = ResxGeneratorCommon.GetResxGroups(files);
        var hintNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resxGroup in resxGroups)
        {
            var hasError = false;

            string? GetMetadataValue(string name, string? globalName)
            {
                var result = ResxGeneratorCommon.GetMetadataValue(options, name, globalName, resxGroup, out var inconsistentFilePath);
                hasError |= inconsistentFilePath is not null;
                return result;
            }

            // Keep in sync with the build/Meziantou.Framework.ResxSourceGenerator.props file
            var rootNamespaceConfiguration = GetMetadataValue("RootNamespace", "RootNamespace");
            var projectDirConfiguration = GetMetadataValue("ProjectDir", "ProjectDir");
            var namespaceConfiguration = GetMetadataValue("Namespace", "DefaultResourcesNamespace");
            var defaultResourceNameConfiguration = GetMetadataValue("DefaultResourceName", globalName: null);
            var resourceNameConfiguration = GetMetadataValue("ResourceName", globalName: null);
            var classNameConfiguration = GetMetadataValue("ClassName", globalName: null);
            var visibilityConfiguration = GetMetadataValue("Visibility", globalName: "DefaultResourcesVisibility");
            var generateKeyNamesTypeConfiguration = GetMetadataValue("GenerateKeyNamesType", globalName: null);
            var generateResourcesTypeConfiguration = GetMetadataValue("GenerateResourcesType", globalName: null);

            var rootNamespace = rootNamespaceConfiguration ?? assemblyName ?? "";
            var projectDir = projectDirConfiguration ?? assemblyName ?? "";
            var defaultResourceName = defaultResourceNameConfiguration ?? ResxGeneratorCommon.ComputeResourceName(rootNamespace, projectDir, resxGroup.Key);
            var defaultNamespace = ResxGeneratorCommon.ComputeNamespace(rootNamespace, projectDir, resxGroup.Key);

            var ns = namespaceConfiguration ?? defaultNamespace ?? rootNamespace;
            var resourceName = resourceNameConfiguration ?? defaultResourceName;
            var className = classNameConfiguration ?? ToCSharpNameIdentifier(Path.GetFileName(resxGroup.Key));
            var visibility = string.Equals(visibilityConfiguration, "public", StringComparison.OrdinalIgnoreCase) ? "public" : "internal";
            var generateKeyNamesType = ResxGeneratorCommon.ParseBoolean(generateKeyNamesTypeConfiguration, defaultValue: true);
            var generateResourcesType = ResxGeneratorCommon.ParseBoolean(generateResourcesTypeConfiguration, defaultValue: true);

            if (resourceName is null && generateResourcesType)
            {
                hasError = true;
            }

            var entries = LoadResourceFiles(context, resxGroup);
            hasError |= entries is null;

            if (hasError)
            {
                continue;
            }

            var content = $@"
// Debug info:
// key: {resxGroup.Key}
// files: {string.Join(", ", resxGroup.Select(f => f.Path))}
// RootNamespace (metadata): {rootNamespaceConfiguration}
// ProjectDir (metadata): {projectDirConfiguration}
// Namespace / DefaultResourcesNamespace (metadata): {namespaceConfiguration}
// DefaultResourceName (metadata): {defaultResourceNameConfiguration}
// ResourceName (metadata): {resourceNameConfiguration}
// ClassName (metadata): {classNameConfiguration}
// Visibility (metadata): {visibilityConfiguration}
// GenerateKeyNames (metadata): {generateKeyNamesTypeConfiguration}
// GenerateResources (metadata): {generateResourcesTypeConfiguration}
// AssemblyName: {assemblyName}
// RootNamespace (computed): {rootNamespace}
// ProjectDir (computed): {projectDir}
// defaultNamespace: {defaultNamespace}
// defaultResourceName: {defaultResourceName}
// Namespace: {ns}
// ResourceName: {resourceName}
// ClassName: {className}
// visibility: {visibility}
// generateKeyNames: {generateKeyNamesType}
// generateResources: {generateResourcesType}
";
            content += GenerateCode(ns, className, resourceName, visibility, generateResourcesType, generateKeyNamesType, entries!, supportNullableReferenceTypes);

            context.AddSource(GetHintName(hintNames, projectDir, resxGroup.Key), SourceText.From(content, Encoding.UTF8));
        }
    }

    private static string GetHintName(HashSet<string> hintNames, string projectDir, string resourcePath)
    {
        var name = ResxGeneratorCommon.ComputeHintName(projectDir, resourcePath);
        if (hintNames.Add(name))
            return name + ".resx.g.cs";

        // Two different paths can lead to the same name once the invalid characters are replaced
        for (var i = 1; ; i++)
        {
            var candidate = name + i.ToString(CultureInfo.InvariantCulture);
            if (hintNames.Add(candidate))
                return candidate + ".resx.g.cs";
        }
    }

    private static string GenerateCode(string? ns, string className, string? resourceName, string visibility, bool generateResourcesType, bool generateKeyNamesType, List<ResxEntry> entries, bool enableNullableAttributes)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("#nullable enable");

        if (ns is not null)
        {
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
        }

        if (generateResourcesType && resourceName is not null)
        {
            sb.AppendLine($"    {visibility} partial class " + className);
            sb.AppendLine("    {");
            sb.AppendLine("        private static global::System.Resources.ResourceManager? resourceMan;");
            sb.AppendLine();
            sb.AppendLine("        public " + className + "() { }");
            sb.AppendLine(@"
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Resources.ResourceManager ResourceManager
        {
            get
            {
                if (resourceMan is null)
                {
                    resourceMan = new global::System.Resources.ResourceManager(" + ToLiteral(resourceName) + @", typeof(" + className + @").Assembly);
                }

                return resourceMan;
            }
        }

        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Globalization.CultureInfo? Culture { get; set; }

        " + AppendNotNullIfNotNull("defaultValue") + @"
        public static object? GetObject(global::System.Globalization.CultureInfo? culture, string name, object? defaultValue)
        {
            culture ??= Culture;
            object? obj = ResourceManager.GetObject(name, culture);
            if (obj == null)
            {
                return defaultValue;
            }

            return obj;
        }

        public static object? GetObject(global::System.Globalization.CultureInfo? culture, string name)
        {
            return GetObject(culture: culture, name: name, defaultValue: null);
        }

        public static object? GetObject(string name)
        {
            return GetObject(culture: null, name: name, defaultValue: null);
        }

        " + AppendNotNullIfNotNull("defaultValue") + @"
        public static object? GetObject(string name, object? defaultValue)
        {
            return GetObject(culture: null, name: name, defaultValue: defaultValue);
        }

        public static global::System.IO.Stream? GetStream(string name)
        {
            return GetStream(culture: null, name: name);
        }

        public static global::System.IO.Stream? GetStream(global::System.Globalization.CultureInfo? culture, string name)
        {
            culture ??= Culture;
            return ResourceManager.GetStream(name, culture);
        }

        public static string? GetString(global::System.Globalization.CultureInfo? culture, string name)
        {
            return GetString(culture: culture, name: name, args: null);
        }

        public static string? GetString(global::System.Globalization.CultureInfo? culture, string name, params object?[]? args)
        {
            culture ??= Culture;
            string? str = ResourceManager.GetString(name, culture);
            if (str == null)
            {
                return null;
            }

            if (args != null)
            {
                return string.Format(culture, str, args);
            }
            else
            {
                return str;
            }
        }

        public static string? GetString(string name, params object?[]? args)
        {
            return GetString(culture: null, name: name, args: args);
        }

        " + AppendNotNullIfNotNull("defaultValue") + @"
        public static string? GetString(string name, string? defaultValue)
        {
            return GetStringWithDefault(culture: null, name: name, defaultValue: defaultValue, args: null);
        }

        public static string? GetString(string name)
        {
            return GetStringWithDefault(culture: null, name: name, defaultValue: null, args: null);
        }

        " + AppendNotNullIfNotNull("defaultValue") + @"
        public static string? GetStringWithDefault(global::System.Globalization.CultureInfo? culture, string name, string? defaultValue)
        {
            return GetStringWithDefault(culture: culture, name: name, defaultValue: defaultValue, args: null);
        }

        " + AppendNotNullIfNotNull("defaultValue") + @"
        public static string? GetStringWithDefault(global::System.Globalization.CultureInfo? culture, string name, string? defaultValue, params object?[]? args)
        {
            culture ??= Culture;
            string? str = ResourceManager.GetString(name, culture);
            if (str == null)
            {
                if (defaultValue == null || args == null)
                {
                    return defaultValue;
                }
                else
                {
                    return string.Format(culture, defaultValue, args);
                }
            }

            if (args != null)
            {
                return string.Format(culture, str, args);
            }
            else
            {
                return str;
            }
        }

        " + AppendNotNullIfNotNull("defaultValue") + @"
        public static string? GetStringWithDefault(string name, string? defaultValue, params object?[]? args)
        {
            return GetStringWithDefault(culture: null, name: name, defaultValue: defaultValue, args: args);
        }

        " + AppendNotNullIfNotNull("defaultValue") + @"
        public static string? GetStringWithDefault(string name, string? defaultValue)
        {
            return GetStringWithDefault(culture: null, name: name, defaultValue: defaultValue, args: null);
        }
");

            foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                if (entry.IsText)
                {
                    var summary = new XElement("summary", new XElement("para", $"Looks up a localized string for \"{entry.Name}\"."));
                    if (!string.IsNullOrWhiteSpace(entry.Comment))
                    {
                        summary.Add(new XElement("para", entry.Comment));
                    }

                    if (!entry.IsFileRef)
                    {
                        summary.Add(new XElement("para", $"Value: \"{entry.Value}\"."));
                    }

                    var comment = summary.ToString().Replace(Environment.NewLine, Environment.NewLine + "       /// ", StringComparison.Ordinal);

                    sb.AppendLine(@"
        /// " + comment + @"
        public static string? @" + ToCSharpNameIdentifier(entry.Name) + @"
        {
            get
            {
                return GetString(" + ToLiteral(entry.Name) + @");
            }
        }
");

                    if (entry.Value is not null)
                    {
                        var args = Regex.Matches(entry.Value, "\\{(?<num>[0-9]+)(\\:[^}]*)?\\}", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                            .Cast<Match>()
                            .Select(m => int.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture))
                            .Distinct()
                            .DefaultIfEmpty(-1)
                            .Max();

                        if (args >= 0)
                        {
                            var parameters = GetFormatParameters(entry, args);
                            var inParams = string.Join(", ", parameters.Select(parameter => parameter.TypeName + " " + EscapeCSharpIdentifier(parameter.Name)));
                            var callParams = string.Join(", ", parameters.Select(parameter => EscapeCSharpIdentifier(parameter.Name)));
                            var formatComment = CreateFormatComment(comment, parameters);

                            sb.AppendLine(@"
        /// " + formatComment + @"
        public static string? Format" + ToCSharpNameIdentifier(entry.Name) + "(global::System.Globalization.CultureInfo? provider, " + inParams + @")
        {
            return GetString(culture: provider, name: " + ToLiteral(entry.Name) + @", args: new object?[] { " + callParams + @" });
        }
");

                            sb.AppendLine(@"
        /// " + formatComment + @"
        public static string? Format" + ToCSharpNameIdentifier(entry.Name) + "(" + inParams + @")
        {
            return GetString(name: " + ToLiteral(entry.Name) + @", args: new object?[] { " + callParams + @" });
        }
");
                        }
                    }
                }
                else
                {
                    sb.AppendLine(@"
        public static global::" + entry.FullTypeName + "? @" + ToCSharpNameIdentifier(entry.Name) + @"
        {
            get
            {
                return (global::" + entry.FullTypeName + @"?)GetObject(" + ToLiteral(entry.Name) + @");
            }
        }
");
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine();
        }

        if (generateKeyNamesType)
        {
            sb.AppendLine($"    {visibility} partial class {className}Names");
            sb.AppendLine("    {");
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                sb.AppendLine("        public const string @" + ToCSharpNameIdentifier(entry.Name) + " = " + ToLiteral(entry.Name) + ";");
            }

            sb.AppendLine("    }");
        }

        if (ns is not null)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();

        string? AppendNotNullIfNotNull(string paramName)
        {
            if (!enableNullableAttributes)
                return null;

            return "[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute(\"" + paramName + "\")]\n";
        }
    }

    private static List<ResxEntry>? LoadResourceFiles(SourceProductionContext context, IGrouping<string, AdditionalText> resxGroug)
    {
        var entries = new List<ResxEntry>();
        foreach (var entry in resxGroug.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            var content = entry.GetText(context.CancellationToken);
            if (content is null)
                continue;

            try
            {
                var document = XDocument.Parse(content.ToString());
                foreach (var element in document.XPathSelectElements("/root/data"))
                {
                    var name = element.Attribute("name")?.Value;
                    var type = element.Attribute("type")?.Value;
                    var comment = element.Attribute("comment")?.Value;
                    var value = element.Element("value")?.Value;
                    var parameters = element.Elements(XName.Get("parameter", ResxGeneratorNamespace))
                        .Select(parameter => new ResxParameter
                        {
                            Name = parameter.Attribute("name")?.Value,
                            TypeName = parameter.Attribute("typename")?.Value,
                            Comment = parameter.Attribute("comment")?.Value,
                        })
                        .ToList();

                    var existingEntry = entries.Find(e => e.Name == name);
                    if (existingEntry is not null)
                    {
                        existingEntry.Comment ??= comment;
                        if (existingEntry.Parameters.Count == 0)
                        {
                            existingEntry.Parameters.AddRange(parameters);
                        }
                    }
                    else
                    {
                        entries.Add(new ResxEntry { Name = name, Value = value, Comment = comment, Type = type, Parameters = parameters });
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        return entries;
    }

    private static List<FormatParameter> GetFormatParameters(ResxEntry entry, int maxArgumentIndex)
    {
        const string DefaultTypeName = "object?";
        var parameters = new List<FormatParameter>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "provider",
        };

        for (var index = 0; index <= maxArgumentIndex; index++)
        {
            var fallbackName = GetUniqueFallbackName(index, usedNames);
            var name = fallbackName;
            var typeName = DefaultTypeName;
            var comment = (string?)null;
            if (index < entry.Parameters.Count)
            {
                var parameter = entry.Parameters[index];
                comment = parameter.Comment;
                if (!string.IsNullOrWhiteSpace(parameter.TypeName))
                {
                    typeName = parameter.TypeName.Trim();
                }

                if (!string.IsNullOrWhiteSpace(parameter.Name))
                {
                    var csharpName = ToCSharpNameIdentifier(parameter.Name);
                    if (!string.IsNullOrEmpty(csharpName) && usedNames.Add(csharpName))
                    {
                        name = csharpName;
                    }
                }
            }

            if (name == fallbackName)
            {
                _ = usedNames.Add(name);
            }

            parameters.Add(new FormatParameter(name, typeName, comment));
        }

        return parameters;
    }

    private static string GetUniqueFallbackName(int index, HashSet<string> usedNames)
    {
        var name = "arg" + index.ToString(CultureInfo.InvariantCulture);
        while (usedNames.Contains(name))
        {
            name += "_";
        }

        return name;
    }

    private static string CreateFormatComment(string comment, List<FormatParameter> parameters)
    {
        var elements = new List<string>
        {
            comment,
        };

        foreach (var parameter in parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Comment))
                continue;

            var element = new XElement("param", new XAttribute("name", parameter.Name), parameter.Comment);
            elements.Add(element.ToString().Replace(Environment.NewLine, Environment.NewLine + "       /// ", StringComparison.Ordinal));
        }

        return string.Join(Environment.NewLine + "        /// ", elements);
    }

    /// <summary>
    /// Formats a value read from the resx file as a C# string literal, quotes included. Resource names and
    /// resource file names are arbitrary text, so they cannot be concatenated into the generated source as-is.
    /// </summary>
    private static string ToLiteral(string value)
    {
        return SymbolDisplay.FormatLiteral(value, quote: true);
    }

    private static string EscapeCSharpIdentifier(string name)
    {
        return IsCSharpKeyword(name) ? "@" + name : name;
    }

    private static bool IsCSharpKeyword(string name)
    {
        return name is
            "abstract" or
            "as" or
            "base" or
            "bool" or
            "break" or
            "byte" or
            "case" or
            "catch" or
            "char" or
            "checked" or
            "class" or
            "const" or
            "continue" or
            "decimal" or
            "default" or
            "delegate" or
            "do" or
            "double" or
            "else" or
            "enum" or
            "event" or
            "explicit" or
            "extern" or
            "false" or
            "finally" or
            "fixed" or
            "float" or
            "for" or
            "foreach" or
            "goto" or
            "if" or
            "implicit" or
            "in" or
            "int" or
            "interface" or
            "internal" or
            "is" or
            "lock" or
            "long" or
            "namespace" or
            "new" or
            "null" or
            "object" or
            "operator" or
            "out" or
            "override" or
            "params" or
            "private" or
            "protected" or
            "public" or
            "readonly" or
            "ref" or
            "return" or
            "sbyte" or
            "sealed" or
            "short" or
            "sizeof" or
            "stackalloc" or
            "static" or
            "string" or
            "struct" or
            "switch" or
            "this" or
            "throw" or
            "true" or
            "try" or
            "typeof" or
            "uint" or
            "ulong" or
            "unchecked" or
            "unsafe" or
            "ushort" or
            "using" or
            "virtual" or
            "void" or
            "volatile" or
            "while";
    }


    private static string ToCSharpNameIdentifier(string name)
    {
        // https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/lexical-structure#identifiers
        // https://docs.microsoft.com/en-us/dotnet/api/system.globalization.unicodecategory?view=net-5.0
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            var category = char.GetUnicodeCategory(c);
            switch (category)
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.LetterNumber:
                    sb.Append(c);
                    break;

                case UnicodeCategory.DecimalDigitNumber:
                case UnicodeCategory.ConnectorPunctuation:
                case UnicodeCategory.Format:
                    if (sb.Length == 0)
                    {
                        sb.Append('_');
                    }

                    sb.Append(c);
                    break;

                default:
                    sb.Append('_');
                    break;
            }
        }

        return sb.ToString();
    }

    private sealed class ResxEntry
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
        public string? Comment { get; set; }
        public string? Type { get; set; }
        public List<ResxParameter> Parameters { get; set; } = [];

        public bool IsText
        {
            get
            {
                if (Type is null)
                    return true;

                if (Value is not null)
                {
                    var parts = Value.Split(';');
                    if (parts.Length > 1)
                    {
                        var type = parts[1];
                        if (type.StartsWith("System.String,", StringComparison.Ordinal))
                            return true;
                    }
                }

                return false;
            }
        }

        public string? FullTypeName
        {
            get
            {
                if (IsText)
                    return "string";

                if (Value is not null)
                {
                    var parts = Value.Split(';');
                    if (parts.Length > 1)
                    {
                        var type = parts[1];
                        return type.Split(',')[0];
                    }
                }

                return null;
            }
        }

        public bool IsFileRef => Type is not null && Type.StartsWith("System.Resources.ResXFileRef,", StringComparison.Ordinal);
    }

    private sealed class ResxParameter
    {
        public string? Name { get; set; }
        public string? TypeName { get; set; }
        public string? Comment { get; set; }
    }

    private sealed record FormatParameter(string Name, string TypeName, string? Comment);
}
