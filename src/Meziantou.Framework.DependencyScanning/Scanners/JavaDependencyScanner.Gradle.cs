using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed partial class JavaDependencyScanner
{
    private async ValueTask ScanGradleAsync(ScanFileContext context, bool isKotlin)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var text = await reader.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);
        var tokens = GradleLexer.Tokenize(text, isKotlin);
        TextLineMap? lineMap = null;

        var namedArgumentsEnd = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            // implementation "group:artifact:version[:classifier][@extension]"
            if (token is { Kind: GradleTokenKind.String, IsVerbatim: true } && GradleCoordinateRegex().Match(token.GetContent(text)) is { Success: true } coordinateMatch)
            {
                var version = coordinateMatch.Groups["version"];
                ReportGradleDependency(context, text, ref lineMap, coordinateMatch.Groups["name"].Value, version.Value, token.ContentStart + version.Index, pluginId: null);
                continue;
            }

            // implementation group: 'g', name: 'a', version: '1.0'
            // The tokens of the arguments are not skipped, so a coordinate string passed as a named argument is still found
            if (i >= namedArgumentsEnd && IsGradleNamedArgument(text, tokens, i))
            {
                namedArgumentsEnd = ReadGradleNamedArguments(text, tokens, i, out var mapName, out var mapVersion);
                if (mapName is not null)
                {
                    ReportGradleDependency(context, text, ref lineMap, mapName, mapVersion.GetContent(text), mapVersion.ContentStart, pluginId: null);
                }

                continue;
            }

            // id("x") version "1.0"
            if (TryReadGradlePlugin(text, tokens, i, isKotlin, out var pluginEnd, out var pluginId, out var pluginVersion))
            {
                ReportGradleDependency(context, text, ref lineMap, GetGradlePluginMarkerName(pluginId), pluginVersion.GetContent(text), pluginVersion.ContentStart, pluginId);
                i = pluginEnd - 1;
            }
        }
    }

    private void ReportGradleDependency(ScanFileContext context, string text, ref TextLineMap? lineMap, string name, string version, int versionIndex, string? pluginId)
    {
        lineMap ??= new TextLineMap(text);
        var versionLocation = TextLocation.FromIndex(context.FileSystem, context.FullPath, lineMap, versionIndex, version.Length);
        if (pluginId is null)
        {
            context.ReportDependency(this, name, version, DependencyType.JavaPackage, new NonUpdatableLocation(context), versionLocation);
        }
        else
        {
            context.ReportDependency(this, name, version, DependencyType.JavaPackage, new NonUpdatableLocation(context), versionLocation,
                tags: [], metadata: [KeyValuePair.Create<string, object?>(PluginIdMetadataKey, pluginId)]);
        }
    }

    /// <summary>
    /// Reads the named arguments that start at <paramref name="start"/>, such as <c>group: 'g', name: 'a', version: '1.0'</c>
    /// in Groovy or <c>group = "g", name = "a", version = "1.0"</c> in Kotlin.
    /// </summary>
    /// <param name="text">The script.</param>
    /// <param name="tokens">The tokens of the script.</param>
    /// <param name="start">The index of the first token of the arguments.</param>
    /// <param name="name">The <c>group:name</c> of the dependency, or <see langword="null"/> when the arguments do not declare a dependency with a literal group, name and version.</param>
    /// <param name="version">The version string.</param>
    /// <returns>The index of the token that follows the arguments.</returns>
    private static int ReadGradleNamedArguments(string text, List<GradleToken> tokens, int start, out string? name, out GradleToken version)
    {
        GradleToken? group = null;
        GradleToken? artifact = null;
        GradleToken? versionToken = null;
        var index = start;
        while (true)
        {
            var value = tokens[index + 2];
            var literal = value is { Kind: GradleTokenKind.String, IsVerbatim: true } ? value : (GradleToken?)null;
            switch (tokens[index].GetText(text))
            {
                case "group":
                    group = literal;
                    break;

                case "name":
                    artifact = literal;
                    break;

                case "version":
                    versionToken = literal;
                    break;
            }

            index += 3;

            // Only a value made of a single token is read, so an expression ends the arguments
            if (tokens.Count > index && tokens[index].Is(text, GradleTokenKind.Punctuation, ",") && IsGradleNamedArgument(text, tokens, index + 1))
            {
                index++;
                continue;
            }

            break;
        }

        if (group is { } groupToken && artifact is { } artifactToken && versionToken is { } literalVersion)
        {
            name = groupToken.GetContent(text) + ":" + artifactToken.GetContent(text);
            version = literalVersion;
        }
        else
        {
            name = null;
            version = default;
        }

        return index;
    }

    private static bool IsGradleNamedArgument(string text, List<GradleToken> tokens, int index)
    {
        return index + 2 < tokens.Count
            && tokens[index].Kind is GradleTokenKind.Identifier
            && (tokens[index + 1].Is(text, GradleTokenKind.Punctuation, ":") || tokens[index + 1].Is(text, GradleTokenKind.Operator, "="));
    }

    /// <summary>
    /// Reads a plugin declared with a version, such as <c>id 'x' version '1.0'</c> in Groovy, or <c>id("x") version "1.0"</c>
    /// and <c>kotlin("jvm") version "1.0"</c> in Kotlin.
    /// </summary>
    private static bool TryReadGradlePlugin(string text, List<GradleToken> tokens, int start, bool isKotlin, out int end, [NotNullWhen(true)] out string? pluginId, out GradleToken version)
    {
        end = start;
        pluginId = null;
        version = default;

        var token = tokens[start];
        var isKotlinPlugin = isKotlin && token.Is(text, GradleTokenKind.Identifier, "kotlin");
        if (!token.Is(text, GradleTokenKind.Identifier, "id") && !isKotlinPlugin)
            return false;

        var index = start + 1;
        if (!TryReadGradleStringArgument(text, tokens, ref index, requireParentheses: isKotlinPlugin, out var idToken))
            return false;

        // id("x") version "1.0", or id("x").version("1.0")
        if (index < tokens.Count && tokens[index].Is(text, GradleTokenKind.Punctuation, "."))
        {
            index++;
        }

        if (index >= tokens.Count || !tokens[index].Is(text, GradleTokenKind.Identifier, "version"))
            return false;

        index++;
        if (!TryReadGradleStringArgument(text, tokens, ref index, requireParentheses: false, out version))
            return false;

        end = index;
        var id = idToken.GetContent(text);
        pluginId = isKotlinPlugin ? "org.jetbrains.kotlin." + id : id;
        return true;
    }

    /// <summary>Reads a string literal, optionally between parentheses, whose value is its text.</summary>
    private static bool TryReadGradleStringArgument(string text, List<GradleToken> tokens, ref int index, bool requireParentheses, out GradleToken value)
    {
        value = default;
        var hasParentheses = index < tokens.Count && tokens[index].Is(text, GradleTokenKind.Punctuation, "(");
        if (requireParentheses && !hasParentheses)
            return false;

        var valueIndex = hasParentheses ? index + 1 : index;
        if (valueIndex >= tokens.Count || tokens[valueIndex] is not { Kind: GradleTokenKind.String, IsVerbatim: true } token || token.ContentStart == token.ContentEnd)
            return false;

        var end = valueIndex + 1;
        if (hasParentheses)
        {
            if (end >= tokens.Count || !tokens[end].Is(text, GradleTokenKind.Punctuation, ")"))
                return false;

            end++;
        }

        value = token;
        index = end;
        return true;
    }

    // group:artifact:version[:classifier][@extension]. Interpolated versions, such as $fooVersion, are not matched.
    [GeneratedRegex("""^(?<name>[A-Za-z0-9_.-]+:[A-Za-z0-9_.-]+):(?<version>[^"'$:@\\]+)(?::[^"'$:@\\]+)?(?:@[^"'$\\]+)?$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex GradleCoordinateRegex();
}
