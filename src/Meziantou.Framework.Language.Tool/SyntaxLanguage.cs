using Meziantou.Framework.Language.Json;
using Meziantou.Framework.Language.Regex;
using Meziantou.Framework.Language.Shell;
using Meziantou.Framework.Language.Xml;

namespace Meziantou.Framework.Language.Tool;

/// <summary>A language the tool can parse, together with the dialect of the languages that have one.</summary>
internal sealed class SyntaxLanguage
{
    private const string RegexPrefix = "regex";

    private SyntaxLanguage(SyntaxLanguageFamily family, RegexDialect? regexDialect = null, ShellDialect? shellDialect = null)
    {
        Family = family;
        RegexDialect = regexDialect;
        ShellDialect = shellDialect;
    }

    public SyntaxLanguageFamily Family { get; }
    public RegexDialect? RegexDialect { get; }
    public ShellDialect? ShellDialect { get; }

    public static SyntaxLanguage Json { get; } = new(SyntaxLanguageFamily.Json);
    public static SyntaxLanguage Xml { get; } = new(SyntaxLanguageFamily.Xml);

    /// <summary>The values <c>--language</c> documents, in the order the help text lists them.</summary>
    public static string SupportedValues { get; } = "json, xml, regex, regex-dotnet, regex-javascript, regex-pcre, regex-ere, regex-bre, sh, bash, zsh, powershell, pwsh, cmd";

    /// <summary>The canonical name of the dialect, or <see langword="null"/> for the languages that have none.</summary>
    public string? DialectName => RegexDialect?.Name ?? ShellDialect?.Name;

    public SyntaxTree ParseText(string text) => Family switch
    {
        SyntaxLanguageFamily.Json => JsonSyntaxTree.ParseText(text),
        SyntaxLanguageFamily.Xml => XmlSyntaxTree.ParseText(text),
        SyntaxLanguageFamily.Regex => RegexSyntaxTree.ParseText(text, RegexDialect!),
        SyntaxLanguageFamily.Shell => ShellSyntaxTree.ParseText(text, ShellDialect!),
        _ => throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Unsupported language family '{Family}'")),
    };

    /// <summary>
    /// Names a raw kind. Every language declares its own <c>SyntaxKind</c> enum, all four under the same simple name,
    /// so each is named in full here rather than imported.
    /// </summary>
    public string GetKindName(int rawKind) => Family switch
    {
        SyntaxLanguageFamily.Json => ((Language.Json.SyntaxKind)rawKind).ToString(),
        SyntaxLanguageFamily.Xml => ((Language.Xml.SyntaxKind)rawKind).ToString(),
        SyntaxLanguageFamily.Regex => ((Language.Regex.SyntaxKind)rawKind).ToString(),
        SyntaxLanguageFamily.Shell => ((Language.Shell.SyntaxKind)rawKind).ToString(),
        _ => rawKind.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Resolves a <c>--language</c> value. The order matters: <c>posix</c> is an alias of both the POSIX shell and
    /// POSIX extended regular expressions, and the bare name is the shell, the <c>regex-</c> prefixed one the regex.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out SyntaxLanguage? language)
    {
        var name = value?.Trim().ToLowerInvariant();
        switch (name)
        {
            case "json":
                language = Json;
                return true;

            case "xml":
                language = Xml;
                return true;

            case RegexPrefix:
                language = new SyntaxLanguage(SyntaxLanguageFamily.Regex, regexDialect: Language.Regex.RegexDialect.Net);
                return true;
        }

        if (name is not null && name.StartsWith(RegexPrefix + "-", StringComparison.Ordinal))
        {
            if (Language.Regex.RegexDialect.TryParse(name[(RegexPrefix.Length + 1)..], out var regexDialect))
            {
                language = new SyntaxLanguage(SyntaxLanguageFamily.Regex, regexDialect: regexDialect);
                return true;
            }

            language = null;
            return false;
        }

        if (Language.Shell.ShellDialect.TryParse(name, out var shellDialect))
        {
            language = new SyntaxLanguage(SyntaxLanguageFamily.Shell, shellDialect: shellDialect);
            return true;
        }

        language = null;
        return false;
    }

    /// <summary>
    /// Resolves the language from the file extension. Regular expressions are never detected: a pattern has no file
    /// form, so <c>--language</c> is the only way to ask for one.
    /// </summary>
    public static bool TryDetectFromExtension(FullPath path, [NotNullWhen(true)] out SyntaxLanguage? language)
    {
        var name = path.Extension.ToLowerInvariant() switch
        {
            ".json" or ".jsonc" or ".json5" or ".webmanifest" => "json",
            ".xml" or ".xsd" or ".xsl" or ".xslt" or ".svg" or ".rss" or ".atom" or ".config" or ".csproj"
                or ".vbproj" or ".fsproj" or ".props" or ".targets" or ".nuspec" or ".resx" or ".plist" or ".xaml" => "xml",
            ".sh" => "sh",
            ".bash" => "bash",
            ".zsh" => "zsh",
            ".ps1" or ".psm1" or ".psd1" => "pwsh",
            ".bat" or ".cmd" => "cmd",
            _ => null,
        };

        if (name is null)
        {
            language = null;
            return false;
        }

        return TryParse(name, out language);
    }
}
