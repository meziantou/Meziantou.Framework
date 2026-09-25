using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Toml;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans modern Python project manifests and lockfiles for PyPI packages.</summary>
public sealed class PythonProjectDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.PyPi];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("pyproject.toml", ignoreCase: false)
            || context.HasFileName("poetry.lock", ignoreCase: false)
            || context.HasFileName("uv.lock", ignoreCase: false)
            || context.HasFileName("Pipfile", ignoreCase: false)
            || context.HasFileName("Pipfile.lock", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        switch (Path.GetFileName(context.FullPath))
        {
            case "poetry.lock" or "uv.lock":
                await ScanTomlLockAsync(context).ConfigureAwait(false);
                break;

            case "Pipfile.lock":
                await ScanPipfileLockAsync(context).ConfigureAwait(false);
                break;

            case "Pipfile":
                await ScanManifestAsync(context, isPipfile: true).ConfigureAwait(false);
                break;

            default:
                await ScanManifestAsync(context, isPipfile: false).ConfigureAwait(false);
                break;
        }
    }

    private static async ValueTask<TomlSyntaxTree> ParseTomlAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var text = await reader.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);
        return TomlSyntaxTree.ParseText(text, context.FullPath);
    }

    private async ValueTask ScanManifestAsync(ScanFileContext context, bool isPipfile)
    {
        var tree = await ParseTomlAsync(context).ConfigureAwait(false);
        IReadOnlyList<SyntaxToken> tablePath = [];
        var isArrayOfTables = false;
        foreach (var entry in tree.GetRoot().Entries)
        {
            switch (entry)
            {
                case TomlTableSyntax table:
                    tablePath = table.Key.Parts;
                    isArrayOfTables = table.IsArrayOfTables;
                    break;

                // No dependency section is an array of tables, such as [[tool.uv.index]]
                case TomlPropertySyntax property when !isArrayOfTables:
                    ScanManifestValue(context, tree, isPipfile, [.. tablePath, .. property.Key.Parts], property.Value);
                    break;
            }
        }
    }

    // The path holds the key parts from the table header and from the (dotted) keys, so that [tool.poetry.dependencies] requests = "1.0",
    // [tool.poetry] dependencies.requests = "1.0" and [tool.poetry.dependencies.requests] version = "1.0" are handled the same way.
    private void ScanManifestValue(ScanFileContext context, TomlSyntaxTree tree, bool isPipfile, SyntaxToken[] path, TomlValueSyntax value)
    {
        if (value is TomlInlineTableSyntax inlineTable)
        {
            foreach (var property in inlineTable.Properties)
            {
                ScanManifestValue(context, tree, isPipfile, [.. path, .. property.Key.Parts], property.Value);
            }

            return;
        }

        var names = Array.ConvertAll(path, token => token.ValueText);
        if (isPipfile)
        {
            // [packages] requests = "==2.31.0" or requests = { version = "==2.31.0", extras = ["socks"] }
            if (names is ["packages" or "dev-packages", _] or ["packages" or "dev-packages", _, "version"] && value is TomlStringSyntax pipfileVersion)
            {
                ReportVersionConstraint(context, tree, path[1], pipfileVersion);
            }

            return;
        }

        if (value is TomlArraySyntax array && IsRequirementArray(names))
        {
            foreach (var element in array.Elements)
            {
                // [dependency-groups] can include another group with { include-group = "name" }
                if (element is TomlStringSyntax requirement)
                {
                    ReportRequirement(context, tree, requirement);
                }
            }
        }
        else if (value is TomlStringSyntax poetryVersion && GetPoetryPackageNameIndex(names) is var nameIndex and >= 0 && !names[nameIndex].Equals("python", StringComparison.OrdinalIgnoreCase))
        {
            // The python key is the version of Python the project supports, not a package
            ReportVersionConstraint(context, tree, path[nameIndex], poetryVersion);
        }
    }

    // Arrays of PEP 508 requirements, such as dependencies = ["requests==2.31.0"]
    private static bool IsRequirementArray(string[] path) => path switch
    {
        ["project", "dependencies"] => true,
        ["project", "optional-dependencies", _] => true,
        ["dependency-groups", _] => true,
        ["build-system", "requires"] => true,
        ["tool", "uv", "dev-dependencies" or "constraint-dependencies" or "override-dependencies"] => true,
        _ => false,
    };

    // Poetry dependency tables map the package name to a version constraint, such as requests = "2.31.0" or requests = { version = "2.31.0" }
    private static int GetPoetryPackageNameIndex(string[] path) => path switch
    {
        ["tool", "poetry", "dependencies" or "dev-dependencies", _] => 3,
        ["tool", "poetry", "dependencies" or "dev-dependencies", _, "version"] => 3,
        ["tool", "poetry", "group", _, "dependencies", _] => 5,
        ["tool", "poetry", "group", _, "dependencies", _, "version"] => 5,
        _ => -1,
    };

    private void ReportRequirement(ScanFileContext context, TomlSyntaxTree tree, TomlStringSyntax requirement)
    {
        if (requirement.StringToken.ContainsDiagnostics)
            return;

        var text = requirement.Value;
        if (!TryParseRequirement(text, out var nameIndex, out var nameLength, out var versionIndex, out var versionLength))
            return;

        context.ReportDependency(this, text.Substring(nameIndex, nameLength), text.Substring(versionIndex, versionLength), DependencyType.PyPi,
            CreateStringLocation(context, tree, requirement, nameIndex, nameLength),
            CreateStringLocation(context, tree, requirement, versionIndex, versionLength));
    }

    private void ReportVersionConstraint(ScanFileContext context, TomlSyntaxTree tree, SyntaxToken name, TomlStringSyntax constraint)
    {
        if (constraint.StringToken.ContainsDiagnostics)
            return;

        var text = constraint.Value;
        if (!TryParseExactVersionConstraint(text, out var versionIndex, out var versionLength))
            return;

        context.ReportDependency(this, name.ValueText, text.Substring(versionIndex, versionLength), DependencyType.PyPi,
            CreateKeyLocation(context, tree, name),
            CreateStringLocation(context, tree, constraint, versionIndex, versionLength));
    }

    private async ValueTask ScanTomlLockAsync(ScanFileContext context)
    {
        var tree = await ParseTomlAsync(context).ConfigureAwait(false);
        var inPackage = false;
        var inPackageSource = false;
        TomlStringSyntax? name = null;
        TomlStringSyntax? version = null;
        var isLocalPackage = false;
        foreach (var entry in tree.GetRoot().Entries)
        {
            if (entry is TomlTableSyntax table)
            {
                var tableName = table.Key.Names;
                if (table.IsArrayOfTables && tableName is ["package"])
                {
                    ReportLockPackage();
                    inPackage = true;
                }
                else if (!inPackage || tableName is not ["package", ..])
                {
                    // The sub-tables of a package, such as [package.source] or [package.dependencies], belong to the current package
                    ReportLockPackage();
                    inPackage = false;
                }

                inPackageSource = inPackage && !table.IsArrayOfTables && tableName is ["package", "source"];
                continue;
            }

            if (!inPackage || entry is not TomlPropertySyntax property)
                continue;

            if (inPackageSource)
            {
                // poetry.lock lists the local projects with a [package.source] table whose type is directory or file
                if (property.Key.Names is ["type"] && IsLocalPoetrySourceType(property.Value))
                {
                    isLocalPackage = true;
                }

                continue;
            }

            switch (property.Key.Names)
            {
                case ["name"]:
                    name = property.Value as TomlStringSyntax;
                    break;

                case ["version"]:
                    version = property.Value as TomlStringSyntax;
                    break;

                // uv.lock lists the local projects, such as the project itself with source = { editable = "." }
                case ["source"] when property.Value is TomlInlineTableSyntax source:
                    isLocalPackage = source.Properties.Any(sourceProperty => sourceProperty.Key.Names is ["editable" or "virtual" or "path" or "directory"]
                        || (sourceProperty.Key.Names is ["type"] && IsLocalPoetrySourceType(sourceProperty.Value)));
                    break;
            }
        }

        ReportLockPackage();

        static bool IsLocalPoetrySourceType(TomlValueSyntax value) => value is TomlStringSyntax { Value: "directory" or "file" };

        // Reports the current package, if any, and resets the state so that it is reported only once
        void ReportLockPackage()
        {
            if (!isLocalPackage && name is not null && version is not null && !name.StringToken.ContainsDiagnostics && !version.StringToken.ContainsDiagnostics)
            {
                // The version is not updatable as the hashes of the package files would not match anymore
                context.ReportDependency(this, name.Value, version.Value, DependencyType.PyPi,
                    CreateStringLocation(context, tree, name, 0, name.Value.Length),
                    new NonUpdatableLocation(context));
            }

            name = null;
            version = null;
            isLocalPackage = false;
        }
    }

    private async ValueTask ScanPipfileLockAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not { } root)
                return;

            foreach (var sectionName in new[] { "default", "develop" })
            {
                if (!JsonNodeDocument.TryGetObject(root, sectionName, out var section))
                    continue;

                foreach (var (name, value) in JsonNodeDocument.GetProperties(section))
                {
                    if (value is not JsonObject package || !JsonNodeDocument.TryGetProperty(package, "version", out var versionNode) || !JsonNodeDocument.TryGetString(versionNode, out var version))
                        continue;

                    // Versions are exact pins, such as "==2.31.0". === is an arbitrary equality, not a version.
                    if (version.StartsWith("===", StringComparison.Ordinal))
                        continue;

                    if (version.StartsWith("==", StringComparison.Ordinal))
                    {
                        version = version[2..];
                    }

                    context.ReportDependency(this, name, version, DependencyType.PyPi,
                        new NonUpdatableLocation(context), new NonUpdatableLocation(context));
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    // PEP 508: name [extras] (== version) [; marker]. Only exact pins (==) are reported, and URL requirements (name @ url) are not.
    // https://packaging.python.org/en/latest/specifications/dependency-specifiers/
    private static bool TryParseRequirement(string text, out int nameIndex, out int nameLength, out int versionIndex, out int versionLength)
    {
        versionIndex = versionLength = 0;

        var index = SkipWhitespaces(text, 0);
        nameIndex = index;
        while (index < text.Length && IsNameChar(text[index]))
        {
            index++;
        }

        nameLength = index - nameIndex;
        if (nameLength is 0 || !char.IsAsciiLetterOrDigit(text[nameIndex]) || !char.IsAsciiLetterOrDigit(text[index - 1]))
            return false;

        index = SkipWhitespaces(text, index);
        if (index < text.Length && text[index] is '[')
        {
            var endOfExtras = text.IndexOf(']', index, StringComparison.Ordinal);
            if (endOfExtras < 0)
                return false;

            index = SkipWhitespaces(text, endOfExtras + 1);
        }

        // The version specifier can be in parentheses, such as requests (==2.31.0)
        var hasParenthesis = index < text.Length && text[index] is '(';
        if (hasParenthesis)
        {
            index = SkipWhitespaces(text, index + 1);
        }

        if (!TryParseExactVersion(text, ref index, out versionIndex, out versionLength))
            return false;

        if (hasParenthesis)
        {
            if (index >= text.Length || text[index] is not ')')
                return false;

            index = SkipWhitespaces(text, index + 1);
        }

        return index == text.Length || text[index] is ';';
    }

    // Poetry and Pipfile version constraints, such as "2.31.0" or "==2.31.0". Ranges, such as "^2.31" or ">=2.31,<3", are not exact versions.
    private static bool TryParseExactVersionConstraint(string text, out int versionIndex, out int versionLength)
    {
        var index = SkipWhitespaces(text, 0);
        if (text.AsSpan(index).StartsWith("==", StringComparison.Ordinal))
        {
            if (!TryParseExactVersion(text, ref index, out versionIndex, out versionLength))
                return false;
        }
        else if (!TryParseVersion(text, ref index, out versionIndex, out versionLength))
        {
            return false;
        }

        return index == text.Length;
    }

    // == version, followed by whitespaces. === (arbitrary equality) is not an exact pin.
    private static bool TryParseExactVersion(string text, ref int index, out int versionIndex, out int versionLength)
    {
        versionIndex = versionLength = 0;
        if (!text.AsSpan(index).StartsWith("==", StringComparison.Ordinal))
            return false;

        index += 2;
        if (index < text.Length && text[index] is '=')
            return false;

        index = SkipWhitespaces(text, index);
        return TryParseVersion(text, ref index, out versionIndex, out versionLength);
    }

    // https://packaging.python.org/en/latest/specifications/version-specifiers/
    private static bool TryParseVersion(string text, ref int index, out int versionIndex, out int versionLength)
    {
        versionIndex = index;
        while (index < text.Length && (char.IsAsciiLetterOrDigit(text[index]) || text[index] is '.' or '_' or '-' or '+' or '!' or '*'))
        {
            index++;
        }

        versionLength = index - versionIndex;
        index = SkipWhitespaces(text, index);
        return versionLength > 0 && char.IsAsciiLetterOrDigit(text[versionIndex]);
    }

    // https://packaging.python.org/en/latest/specifications/name-normalization/
    private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-';

    private static int SkipWhitespaces(string text, int index)
    {
        while (index < text.Length && text[index] is ' ' or '\t')
        {
            index++;
        }

        return index;
    }

    // A part of a string is updatable only when the raw text between the quotes is the value, so without escape sequences, and on a single line
    private static Location CreateStringLocation(ScanFileContext context, TomlSyntaxTree tree, TomlStringSyntax value, int index, int length)
    {
        var token = value.StringToken;
        if (value.IsMultiLine || !IsRawTextEqualToValue(token))
            return new NonUpdatableLocation(context);

        return CreateLocation(context, tree, new TextSpan(token.SpanStart + 1 + index, length));
    }

    // A key is either bare (requests) or quoted ("requests")
    private static Location CreateKeyLocation(ScanFileContext context, TomlSyntaxTree tree, SyntaxToken key)
    {
        if (key.ContainsDiagnostics)
            return new NonUpdatableLocation(context);

        if (key.Kind() is SyntaxKind.BareKeyToken)
            return CreateLocation(context, tree, key.Span);

        if (key.Kind() is SyntaxKind.BasicStringToken or SyntaxKind.LiteralStringToken && IsRawTextEqualToValue(key))
            return CreateLocation(context, tree, new TextSpan(key.SpanStart + 1, key.ValueText.Length));

        return new NonUpdatableLocation(context);
    }

    private static bool IsRawTextEqualToValue(SyntaxToken token)
    {
        var text = token.Text;
        var value = token.ValueText;
        return !token.ContainsDiagnostics && text.Length == value.Length + 2 && text.AsSpan(1, value.Length).SequenceEqual(value);
    }

    private static TextLocation CreateLocation(ScanFileContext context, TomlSyntaxTree tree, TextSpan span)
    {
        var lineSpan = tree.GetLineSpan(span);
        return new TextLocation(context.FileSystem, context.FullPath, lineSpan.Start.Line + 1, lineSpan.Start.Character + 1, span.Length);
    }
}
