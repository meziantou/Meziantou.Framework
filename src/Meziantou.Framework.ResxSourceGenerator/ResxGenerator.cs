#pragma warning disable MA0028 // Optimize StringBuilder would make the code harder to read
#pragma warning disable MA0101 // String contains an implicit end of line character
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.ResxSourceGenerator;

[Generator]
public sealed class ResxGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InvalidResx = new(
        id: "MFRG0001",
        title: "Couldn't parse Resx file",
        messageFormat: "Couldn't parse Resx file '{0}'",
        category: "ResxGenerator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidPropertiesForResourceName = new(
        id: "MFRG0003",
        title: "Couldn't compute resource name",
        messageFormat: "Couldn't compute resource name for file '{0}'",
        category: "ResxGenerator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InconsistentProperties = new(
        id: "MFRG0004",
        title: "Inconsistent properties",
        messageFormat: "Property '{0}' values for '{1}' are inconsistent",
        category: "ResxGenerator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var compilationProvider = context.CompilationProvider.Select(static (compilation, cancellationToken) =>
                    (compilation.AssemblyName, SupportNullableReferenceTypes: compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute") is not null));

        var resxProvider = context.AdditionalTextsProvider.Where(text => text.Path.EndsWith(".resx", StringComparison.OrdinalIgnoreCase)).Collect();

        context.RegisterSourceOutput(
            source: context.AnalyzerConfigOptionsProvider.Combine(compilationProvider.Combine(resxProvider)),
            action: (ctx, source) => Execute(ctx, source.Left, source.Right.Left.AssemblyName, source.Right.Left.SupportNullableReferenceTypes, source.Right.Right));
    }

    private static bool ParseBoolean(string? value, bool defaultValue)
    {
        if (bool.TryParse(value, out var result))
            return result;
        return defaultValue;
    }

    private static void Execute(SourceProductionContext context, AnalyzerConfigOptionsProvider options, string? assemblyName, bool supportNullableReferenceTypes, ImmutableArray<AdditionalText> files)
    {
        // Group additional file by resource kind ((a.resx, a.en.resx, a.en-us.resx), (b.resx, b.en-us.resx))
        var resxGroups = files
            .GroupBy(file => GetResourceName(file.Path), StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var resxGroup in resxGroups)
        {
            // Keep in sync with the build/Meziantou.Framework.ResxSourceGenerator.props file
            var rootNamespaceConfiguration = GetMetadataValue(context, options, "RootNamespace", resxGroup);
            var projectDirConfiguration = GetMetadataValue(context, options, "ProjectDir", resxGroup);
            var namespaceConfiguration = GetMetadataValue(context, options, "Namespace", "DefaultResourcesNamespace", resxGroup);
            var defaultResourceNameConfiguration = GetMetadataValue(context, options, "DefaultResourceName", globalName: null, resxGroup);
            var resourceNameConfiguration = GetMetadataValue(context, options, "ResourceName", globalName: null, resxGroup);
            var classNameConfiguration = GetMetadataValue(context, options, "ClassName", globalName: null, resxGroup);
            var visibilityConfiguration = GetMetadataValue(context, options, "Visibility", globalName: "DefaultResourcesVisibility", resxGroup);
            var generateKeyNamesConfiguration = GetMetadataValue(context, options, "GenerateKeyNamesType", globalName: null, resxGroup);
            var generateResourcesConfiguration = GetMetadataValue(context, options, "GenerateResourcesType", globalName: null, resxGroup);

            var rootNamespace = rootNamespaceConfiguration ?? assemblyName ?? "";
            var projectDir = projectDirConfiguration ?? assemblyName ?? "";
            var defaultResourceName = defaultResourceNameConfiguration ?? ComputeResourceName(rootNamespace, projectDir, resxGroup.Key);
            var defaultNamespace = ComputeNamespace(rootNamespace, projectDir, resxGroup.Key);

            var ns = namespaceConfiguration ?? defaultNamespace ?? rootNamespace;
            var resourceName = resourceNameConfiguration ?? defaultResourceName;
            var className = classNameConfiguration ?? ToCSharpNameIdentifier(Path.GetFileName(resxGroup.Key));
            var visibility = string.Equals(visibilityConfiguration, "public", StringComparison.OrdinalIgnoreCase) ? "public" : "internal";
            var generateKeyNames = ParseBoolean(generateKeyNamesConfiguration, defaultValue: true);
            var generateResources = ParseBoolean(generateResourcesConfiguration, defaultValue: true);

            if (resourceName is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidPropertiesForResourceName, location: null, resxGroup.First().Path));
            }

            var entries = LoadResourceFiles(context, resxGroup);

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
// GenerateKeyNames (metadata): {generateKeyNamesConfiguration}
// GenerateResources (metadata): {generateResourcesConfiguration}
// AssemblyName: {assemblyName}
// RootNamespace (computed): {rootNamespace}
// ProjectDir (computed): {projectDir}
// defaultNamespace: {defaultNamespace}
// defaultResourceName: {defaultResourceName}
// Namespace: {ns}
// ResourceName: {resourceName}
// ClassName: {className}
// visibility: {visibility}
// generateKeyNames: {generateKeyNames}
// generateResources: {generateResources}
";

            if (resourceName is not null && entries is not null)
            {
                content += GenerateCode(ns, className, resourceName, visibility, generateResources, generateKeyNames, entries, supportNullableReferenceTypes);
            }

            context.AddSource($"{Path.GetFileName(resxGroup.Key)}.resx.g.cs", SourceText.From(content, Encoding.UTF8));
        }
    }

    private static string GenerateCode(string? ns, string className, string resourceName, string visibility, bool generateResourcesType, bool generateKeyNamesType, List<ResxEntry> entries, bool enableNullableAttributes)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("#nullable enable");

        if (ns is not null)
        {
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
        }

        if (generateResourcesType)
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
                    resourceMan = new global::System.Resources.ResourceManager(""" + resourceName + @""", typeof(" + className + @").Assembly);
                }

                return resourceMan;
            }
        }

        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Advanced)]
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
                return GetString(""" + entry.Name + @""");
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
                            var inParams = string.Join(", ", Enumerable.Range(0, args + 1).Select(arg => "object? arg" + arg.ToString(CultureInfo.InvariantCulture)));
                            var callParams = string.Join(", ", Enumerable.Range(0, args + 1).Select(arg => "arg" + arg.ToString(CultureInfo.InvariantCulture)));

                            sb.AppendLine(@"
        /// " + comment + @"
        public static string? Format" + ToCSharpNameIdentifier(entry.Name) + "(global::System.Globalization.CultureInfo? provider, " + inParams + @")
        {
            return GetString(provider, """ + entry.Name + "\", " + callParams + @");
        }
");

                            sb.AppendLine(@"
        /// " + comment + @"
        public static string? Format" + ToCSharpNameIdentifier(entry.Name) + "(" + inParams + @")
        {
            return GetString(""" + entry.Name + "\", " + callParams + @");
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
                return (global::" + entry.FullTypeName + @"?)GetObject(""" + entry.Name + @""");
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

                sb.AppendLine("        public const string @" + ToCSharpNameIdentifier(entry.Name) + " = \"" + entry.Name + "\";");
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

    private static string? ComputeResourceName(string rootNamespace, string projectDir, string resourcePath)
    {
        var fullProjectDir = EnsureEndSeparator(Path.GetFullPath(projectDir));
        var fullResourcePath = Path.GetFullPath(resourcePath);

        if (fullProjectDir == fullResourcePath)
            return rootNamespace;

        if (fullResourcePath.StartsWith(fullProjectDir, StringComparison.Ordinal))
        {
            var relativePath = fullResourcePath[fullProjectDir.Length..];
            return rootNamespace + '.' + relativePath.Replace('/', '.').Replace('\\', '.');
        }

        return Path.GetFileNameWithoutExtension(resourcePath);
    }

    private static string? ComputeNamespace(string rootNamespace, string projectDir, string resourcePath)
    {
        var fullProjectDir = EnsureEndSeparator(Path.GetFullPath(projectDir));
        var fullResourcePath = EnsureEndSeparator(Path.GetDirectoryName(Path.GetFullPath(resourcePath))!);

        if (fullProjectDir == fullResourcePath)
            return rootNamespace;

        if (fullResourcePath.StartsWith(fullProjectDir, StringComparison.Ordinal))
        {
            var relativePath = fullResourcePath[fullProjectDir.Length..];
            return rootNamespace + '.' + relativePath.Replace('/', '.').Replace('\\', '.').TrimEnd('.');
        }

        return null;
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

                    var existingEntry = entries.Find(e => e.Name == name);
                    if (existingEntry is not null)
                    {
                        existingEntry.Comment ??= comment;
                    }
                    else
                    {
                        entries.Add(new ResxEntry { Name = name, Value = value, Comment = comment, Type = type });
                    }
                }
            }
            catch
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidResx, location: null, entry.Path));
                return null;
            }
        }

        return entries;
    }

    private static string? GetMetadataValue(SourceProductionContext context, AnalyzerConfigOptionsProvider analyzerConfigOptionsProvider, string name, IEnumerable<AdditionalText> additionalFiles)
    {
        return GetMetadataValue(context, analyzerConfigOptionsProvider, name, name, additionalFiles);
    }

    private static string? GetMetadataValue(SourceProductionContext context, AnalyzerConfigOptionsProvider analyzerConfigOptionsProvider, string name, string? globalName, IEnumerable<AdditionalText> additionalFiles)
    {
        string? result = null;
        foreach (var file in additionalFiles)
        {
            if (analyzerConfigOptionsProvider.GetOptions(file).TryGetValue("build_metadata.AdditionalFiles." + name, out var value))
            {
                if (result is not null && value != result)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InconsistentProperties, location: null, name, file.Path));
                    return null;
                }

                result = value;
            }
        }

        if (!string.IsNullOrEmpty(result))
            return result;

        if (globalName is not null && analyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property." + globalName, out var globalValue) && !string.IsNullOrEmpty(globalValue))
            return globalValue;

        return null;
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

    private static string EnsureEndSeparator(string path)
    {
        if (path[^1] == Path.DirectorySeparatorChar)
            return path;

        return path + Path.DirectorySeparatorChar;
    }

    private static string GetResourceName(string path)
    {
        var pathWithoutExtension = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        var indexOf = pathWithoutExtension.LastIndexOf('.');
        if (indexOf < 0)
            return pathWithoutExtension;

        return Regex.IsMatch(pathWithoutExtension[(indexOf + 1)..], "^[a-zA-Z]{2}(-[a-zA-Z]{2})?$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            ? pathWithoutExtension[0..indexOf]
            : pathWithoutExtension;
    }

    private sealed class ResxEntry
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
        public string? Comment { get; set; }
        public string? Type { get; set; }

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
}
