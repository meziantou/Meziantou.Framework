#pragma warning disable MA0028 // Optimize StringBuilder would make the code harder to read
#pragma warning disable MA0101 // String contains an implicit end of line character
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.ResxSourceGenerator
{
    [Generator]
    public sealed class ResxGenerator : ISourceGenerator
    {
        private static readonly DiagnosticDescriptor s_invalidResx = new(
            id: "MFRG0001",
            title: "Couldn't parse Resx file",
            messageFormat: "Couldn't parse Resx file '{0}'.",
            category: "ResxGenerator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor s_invalidPropertiesForNamespace = new(
            id: "MFRG0002",
            title: "Couldn't compute namespace",
            messageFormat: "Couldn't compute namespace for file '{0}'.",
            category: "ResxGenerator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor s_invalidPropertiesForResourceName = new(
            id: "MFRG0003",
            title: "Couldn't compute resource name",
            messageFormat: "Couldn't compute resource name for file '{0}'.",
            category: "ResxGenerator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor s_inconsistentProperties = new(
            id: "MFRG0004",
            title: "Inconsistent properties",
            messageFormat: "Property '{0}' values for '{1}' are inconsistent.",
            category: "ResxGenerator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);


        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            // Group additional file by resource kind ((a.resx, a.en.resx, a.en-us.resx), (b.resx, b.en-us.resx))
            var resxGroups = context.AdditionalFiles
                .Where(file => file.Path.EndsWith(".resx", StringComparison.OrdinalIgnoreCase))
                .GroupBy(file => GetResourceName(file.Path), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var resxGroug in resxGroups)
            {
                var rootNamespace = GetMetadataValue(context, "RootNamespace", resxGroug) ?? context.Compilation.AssemblyName ?? "";
                var projectDir = GetMetadataValue(context, "ProjectDir", resxGroug) ?? context.Compilation.AssemblyName ?? "";

                var defaultResourceName = ComputeResourceName(rootNamespace, projectDir, resxGroug.Key);
                var defaultNamespace = ComputeNamespace(rootNamespace, projectDir, resxGroug.Key);

                var ns = GetMetadataValue(context, "Namespace", "DefaultResourcesNamespace", resxGroug) ?? defaultNamespace;
                var resourceName = GetMetadataValue(context, "ResourceName", globalName: null, resxGroug) ?? defaultResourceName;
                var className = GetMetadataValue(context, "ClassName", globalName: null, resxGroug) ?? ToCSharpNameIdentifier(Path.GetFileName(resxGroug.Key));

                if (ns == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(s_invalidPropertiesForNamespace, location: null, resxGroug.First().Path));
                    continue;
                }

                if (ns == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(s_invalidPropertiesForResourceName, location: null, resxGroug.First().Path));
                    continue;
                }

                var entries = LoadResourceFiles(context, resxGroug);
                if (entries == null)
                    continue;

                var content = GenerateCode(ns, className, entries);

                content += $@"
// Debug info:
// RootNamespace: {rootNamespace}
// ProjectDir: {projectDir}
// defaultNamespace: {defaultNamespace}
// defaultResourceName: {defaultResourceName}
// key: {resxGroug.Key}
// files: {string.Join(", ", resxGroug.Select(f => f.Path))}
";
                context.AddSource($"{Path.GetFileName(resxGroug.Key)}.resx.cs", SourceText.From(content, Encoding.UTF8));
            }
        }

        private static string GenerateCode(string? ns, string className, List<ResxEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine();

            if (ns != null)
            {
                sb.AppendLine("namespace " + ns);
                sb.AppendLine("{");
            }

            sb.AppendLine("    internal partial class " + className);
            sb.AppendLine("    {");
            sb.AppendLine("        private static System.Resources.ResourceManager resourceMan;");
            sb.AppendLine();
            sb.AppendLine("        public " + className + "() { }");
            sb.AppendLine(@"
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static System.Resources.ResourceManager ResourceManager
        {
            get
            {
                if (resourceMan is null) 
                {
                    resourceMan = new System.Resources.ResourceManager(""" + ns + "." + className + @""", typeof(" + className + @").Assembly);
                }

                return resourceMan;
            }
        }

        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Advanced)]
        public static System.Globalization.CultureInfo Culture { get; set; }

        public static object GetObject(System.Globalization.CultureInfo culture, string name, object defaultValue)
        {
            object obj = ResourceManager.GetObject(name, culture);
            if (obj == null)
            {
                return defaultValue;
            }

            return obj;
        }
        
        public static object GetObject(string name, object defaultValue)
        {
            return GetObject(null, name, defaultValue);
        }
        
        public static System.IO.Stream GetStream(string name)
        {
            return ResourceManager.GetStream(name);
        }

        public static System.IO.Stream GetStream(System.Globalization.CultureInfo culture, string name)
        {
            return ResourceManager.GetStream(name, culture);
        }

        public static string GetString(System.Globalization.CultureInfo culture, string name)
        {
            return GetString(culture, name, null);
        }

        public static string GetString(System.Globalization.CultureInfo culture, string name, params object[] args)
        {
            string str = ResourceManager.GetString(name, culture);
            if (str == null)
            {
                return name;
            }

            if (args != null && args.Length > 0)
            {
                return string.Format(culture, str, args);
            }
            else
            {
                return str;
            }
        }
        
        public static string GetString(string name, params object[] args)
        {
            return GetString(null, name, args);
        }
        
        public static string GetString(string name, string defaultValue)
        {
            return GetStringWithDefault(null, name, defaultValue, null);
        }
        
        public static string GetString(string name)
        {
            return GetStringWithDefault(null, name, name, null);
        }
        
        public static string GetStringWithDefault(System.Globalization.CultureInfo culture, string name, string defaultValue)
        {
            return GetStringWithDefault(culture, name, defaultValue, null);
        }

        public static string GetStringWithDefault(System.Globalization.CultureInfo culture, string name, string defaultValue, params object[] args)
        {
            string str = ResourceManager.GetString(name, culture);
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

            if (args != null && args.Length > 0)
            {
                return string.Format(culture, str, args);
            }
            else
            {
                return str;
            }
        }
        
        public static string GetStringWithDefault(string name, string defaultValue, params object[] args)
        {
            return GetStringWithDefault(null, name, defaultValue, args);
        }
        
        public static string GetStringWithDefault(string name, string defaultValue)
        {
            return GetStringWithDefault(null, name, defaultValue, null);
        }
");

            foreach (var entry in entries.OrderBy(e => e.Name))
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
        public static string " + ToCSharpNameIdentifier(entry.Name) + @"
        {
            get
            {
                return GetString(""" + entry.Name + @""");
            }
        }
");

                    if (entry.Value != null)
                    {
                        var args = Regex.Matches(entry.Value, "\\{(?<num>[0-9]+)(\\:[^}]*)?\\}", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                            .Cast<Match>()
                            .Select(m => int.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture))
                            .Distinct()
                            .DefaultIfEmpty(-1)
                            .Max();

                        if (args >= 0)
                        {
                            var inParams = string.Join(", ", Enumerable.Range(0, args + 1).Select(arg => "object arg" + arg.ToString(CultureInfo.InvariantCulture)));
                            var callParams = string.Join(", ", Enumerable.Range(0, args + 1).Select(arg => "arg" + arg.ToString(CultureInfo.InvariantCulture)));

                            sb.AppendLine(@"
        /// " + comment + @"
        public static string Format" + ToCSharpNameIdentifier(entry.Name) + "(System.Globalization.CultureInfo provider, " + inParams + @")
        {
            return GetString(provider, """ + entry.Name + "\", " + callParams + @");
        }
");

                            sb.AppendLine(@"
        /// " + comment + @"
        public static string Format" + ToCSharpNameIdentifier(entry.Name) + "(" + inParams + @")
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
        public static " + entry.FullTypeName + " " + ToCSharpNameIdentifier(entry.Name) + @"
        {
            get
            {
                return (" + entry.FullTypeName + @")ResourceManager.GetObject(""" + entry.Name + @""");
            }
        }
");
                }
            }
            sb.AppendLine("    }");

            sb.AppendLine();

            sb.AppendLine("    internal partial class " + className + "Names");
            sb.AppendLine("    {");
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                sb.AppendLine("        public const string @" + ToCSharpNameIdentifier(entry.Name) + " = \"" + entry.Name + "\";");
            }
            sb.AppendLine("    }");

            if (ns != null)
            {
                sb.AppendLine("}");
            }
            return sb.ToString();
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

            return null;
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

        private static List<ResxEntry>? LoadResourceFiles(GeneratorExecutionContext context, IGrouping<string, AdditionalText> resxGroug)
        {
            var entries = new List<ResxEntry>();
            foreach (var entry in resxGroug.OrderBy(file => file.Path, StringComparer.Ordinal))
            {
                var content = entry.GetText(context.CancellationToken);
                if (content == null)
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
                        if (existingEntry != null)
                        {
                            if (existingEntry.Comment == null)
                            {
                                existingEntry.Comment = comment;
                            }
                        }
                        else
                        {
                            entries.Add(new ResxEntry { Name = name, Value = value, Comment = comment, Type = type });
                        }
                    }
                }
                catch
                {
                    context.ReportDiagnostic(Diagnostic.Create(s_invalidResx, location: null, entry.Path));
                    return null;
                }
            }

            return entries;
        }

        private static string? GetMetadataValue(GeneratorExecutionContext context, string name, IEnumerable<AdditionalText> additionalFiles)
        {
            return GetMetadataValue(context, name, name, additionalFiles);
        }

        private static string? GetMetadataValue(GeneratorExecutionContext context, string name, string? globalName, IEnumerable<AdditionalText> additionalFiles)
        {
            string? result = null;
            foreach (var file in additionalFiles)
            {
                if (context.AnalyzerConfigOptions.GetOptions(file).TryGetValue("build_metadata.AdditionalFiles." + name, out var value))
                {
                    if (result != null && value != result)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(s_inconsistentProperties, location: null, name, file.Path));
                        return null;
                    }

                    result = value;
                }
            }

            if (result != null)
                return result;

            if (globalName != null && context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property." + globalName, out var globalValue))
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
                    if (Type == null)
                        return true;

                    if (Value != null)
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

                    if (Value != null)
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

            public bool IsFileRef => Type != null && Type.StartsWith("System.Resources.ResXFileRef,", StringComparison.Ordinal);
        }
    }
}
