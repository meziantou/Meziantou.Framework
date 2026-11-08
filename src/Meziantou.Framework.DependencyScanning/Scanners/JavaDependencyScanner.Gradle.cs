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
        VariableDefinitions? variables = null;

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

            // implementation "group:artifact:$version" or "group:artifact:${version}"
            if (token is { Kind: GradleTokenKind.String, IsVerbatim: false } && IsInterpolatedSingleLineString(text, token) && GradleInterpolatedCoordinateRegex().Match(token.GetContent(text)) is { Success: true } interpolatedMatch)
            {
                var version = interpolatedMatch.Groups["version"].Value;
                variables ??= GetGradleVariables(context, text, tokens, isKotlin);
                context.ReportDependency(this, interpolatedMatch.Groups["name"].Value, version, variables.Resolve(version, GetGradleReferences(version, isKotlin)), DependencyType.JavaPackage, new NonUpdatableLocation(context));
                continue;
            }

            // implementation group: 'g', name: 'a', version: '1.0'
            // The tokens of the arguments are not skipped, so a coordinate string passed as a named argument is still found
            if (i >= namedArgumentsEnd && IsGradleNamedArgument(text, tokens, i))
            {
                namedArgumentsEnd = ReadGradleNamedArguments(text, tokens, i, isKotlin, out var mapName, out var mapVersion);
                if (mapName is not null)
                {
                    if (mapVersion.IsVerbatim)
                    {
                        ReportGradleDependency(context, text, ref lineMap, mapName, mapVersion.GetContent(text), mapVersion.ContentStart, pluginId: null);
                    }
                    else
                    {
                        // version: junitVersion, or version: "$junitVersion"
                        var version = mapVersion.GetContent(text);
                        var references = mapVersion.Kind is GradleTokenKind.Identifier ? [new VariableReference(0, version.Length, version)] : GetGradleReferences(version, isKotlin);
                        variables ??= GetGradleVariables(context, text, tokens, isKotlin);
                        context.ReportDependency(this, mapName, version, variables.Resolve(version, references), DependencyType.JavaPackage, new NonUpdatableLocation(context));
                    }
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
    /// <param name="isKotlin">Whether the script is written in Kotlin.</param>
    /// <param name="name">The <c>group:name</c> of the dependency, or <see langword="null"/> when the arguments do not declare a dependency with a literal group and name, and a version.</param>
    /// <param name="version">
    /// The version: a verbatim string, a string that interpolates variables, or an identifier that is the name of a variable.
    /// Its <see cref="GradleToken.IsVerbatim"/> is <see langword="false"/> when it references variables.
    /// </param>
    /// <returns>The index of the token that follows the arguments.</returns>
    private static int ReadGradleNamedArguments(string text, List<GradleToken> tokens, int start, bool isKotlin, out string? name, out GradleToken version)
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
                    versionToken = literal ?? GetGradleVariableVersion(text, tokens, index + 2, isKotlin);
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

    /// <summary>Gets the version of a named argument that references variables, such as <c>version: junitVersion</c> or <c>version: "$junitVersion"</c>.</summary>
    private static GradleToken? GetGradleVariableVersion(string text, List<GradleToken> tokens, int index, bool isKotlin)
    {
        var token = tokens[index];
        if (token.Kind is GradleTokenKind.String)
            return IsInterpolatedSingleLineString(text, token) && GetGradleReferences(token.GetContent(text), isKotlin).Count > 0 ? token with { IsVerbatim = false } : null;

        // Only an identifier that is the whole value is a variable, not the start of an expression such as libs.versions.junit
        if (token.Kind is not GradleTokenKind.Identifier || token.GetText(text) is "true" or "false" or "null")
            return null;

        if (index + 1 < tokens.Count && (tokens[index + 1].Kind is GradleTokenKind.Operator || tokens[index + 1].GetText(text) is "." or "(" or "[" or "{" or "?"))
            return null;

        return token with { IsVerbatim = false };
    }

    /// <summary>Determines whether a string that is not verbatim only interpolates variables: it has no escape sequence and no line break.</summary>
    private static bool IsInterpolatedSingleLineString(string text, GradleToken token)
    {
        return !text.AsSpan(token.ContentStart, token.ContentEnd - token.ContentStart).ContainsAny('\\', '\r', '\n');
    }

    /// <summary>
    /// Gets the variable references of a string, such as <c>$junitVersion</c> or <c>${junitVersion}</c>. In Groovy,
    /// <c>$a.b</c> reads the property <c>b</c> of <c>a</c>, which cannot be resolved. A reference to an extra property,
    /// such as <c>${ext.junitVersion}</c> or <c>${project.ext.junitVersion}</c>, references the variable.
    /// </summary>
    private static List<VariableReference> GetGradleReferences(string value, bool isKotlin)
    {
        var result = new List<VariableReference>();
        var index = 0;
        while (index < value.Length - 1)
        {
            if (value[index] is not '$')
            {
                index++;
                continue;
            }

            if (value[index + 1] is '{')
            {
                var end = value.IndexOf('}', index + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    result.Add(new VariableReference(index, value.Length - index, Name: null));
                    break;
                }

                result.Add(new VariableReference(index, end + 1 - index, GetGradleVariableName(value.AsSpan(index + 2, end - index - 2).Trim())));
                index = end + 1;
                continue;
            }

            if (!char.IsLetter(value[index + 1]) && value[index + 1] is not '_')
            {
                index++;
                continue;
            }

            var nameEnd = index + 2;
            while (nameEnd < value.Length && (char.IsLetterOrDigit(value[nameEnd]) || value[nameEnd] is '_'))
            {
                nameEnd++;
            }

            // Groovy reads a dotted property path: "$a.b" is the property b of a
            var pathEnd = nameEnd;
            if (!isKotlin)
            {
                while (pathEnd + 1 < value.Length && value[pathEnd] is '.' && (char.IsLetter(value[pathEnd + 1]) || value[pathEnd + 1] is '_'))
                {
                    pathEnd += 2;
                    while (pathEnd < value.Length && (char.IsLetterOrDigit(value[pathEnd]) || value[pathEnd] is '_'))
                    {
                        pathEnd++;
                    }
                }
            }

            result.Add(new VariableReference(index, pathEnd - index, GetGradleVariableName(value.AsSpan(index + 1, pathEnd - index - 1))));
            index = pathEnd;
        }

        return result;

        static string? GetGradleVariableName(ReadOnlySpan<char> expression)
        {
            foreach (var prefix in (ReadOnlySpan<string>)["project.ext.", "project.extra.", "rootProject.ext.", "ext.", "extra."])
            {
                if (expression.StartsWith(prefix, StringComparison.Ordinal))
                {
                    expression = expression[prefix.Length..];
                    break;
                }
            }

            if (expression.IsEmpty || !(char.IsLetter(expression[0]) || expression[0] is '_'))
                return null;

            foreach (var c in expression)
            {
                if (!char.IsLetterOrDigit(c) && c is not '_')
                    return null;
            }

            return expression.ToString();
        }
    }

    /// <summary>
    /// Gets the variables that the script defines with a string literal, such as <c>def junitVersion = '5.10.0'</c>,
    /// <c>ext.junitVersion = '5.10.0'</c> or <c>ext { junitVersion = '5.10.0' }</c> in Groovy, and
    /// <c>val junitVersion = "5.10.0"</c>, <c>val junitVersion by extra("5.10.0")</c> or <c>extra["junitVersion"] = "5.10.0"</c> in Kotlin.
    /// </summary>
    /// <remarks>
    /// The scopes of the variables are not tracked, so a variable assigned more than once, or assigned another value
    /// than a string literal, is not resolved.
    /// </remarks>
    private static VariableDefinitions GetGradleVariables(ScanFileContext context, string text, List<GradleToken> tokens, bool isKotlin)
    {
        var result = new VariableDefinitions(StringComparer.Ordinal);
        TextLineMap? lineMap = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            // val name by extra("value")
            if (isKotlin && i + 5 < tokens.Count && token.Kind is GradleTokenKind.Identifier && tokens[i + 1].Is(text, GradleTokenKind.Identifier, "by") && tokens[i + 2].Is(text, GradleTokenKind.Identifier, "extra") && tokens[i + 3].Is(text, GradleTokenKind.Punctuation, "("))
            {
                var hasValue = tokens[i + 5].Is(text, GradleTokenKind.Punctuation, ")");
                Add(token.GetText(text), hasValue ? tokens[i + 4] : null);
                continue;
            }

            // ext["name"] = "value", extra["name"] = "value"
            if (i + 5 < tokens.Count && (token.Is(text, GradleTokenKind.Identifier, "ext") || token.Is(text, GradleTokenKind.Identifier, "extra")) && tokens[i + 1].Is(text, GradleTokenKind.Punctuation, "[") && tokens[i + 2] is { Kind: GradleTokenKind.String, IsVerbatim: true } key && tokens[i + 3].Is(text, GradleTokenKind.Punctuation, "]") && tokens[i + 4].Is(text, GradleTokenKind.Operator, "="))
            {
                Add(key.GetContent(text), GetAssignedValue(i + 5));
                continue;
            }

            // ext.set("name", "value"), extra.set("name", "value")
            if (i + 7 < tokens.Count && (token.Is(text, GradleTokenKind.Identifier, "ext") || token.Is(text, GradleTokenKind.Identifier, "extra")) && tokens[i + 1].Is(text, GradleTokenKind.Punctuation, ".") && tokens[i + 2].Is(text, GradleTokenKind.Identifier, "set") && tokens[i + 3].Is(text, GradleTokenKind.Punctuation, "(") && tokens[i + 4] is { Kind: GradleTokenKind.String, IsVerbatim: true } setKey && tokens[i + 5].Is(text, GradleTokenKind.Punctuation, ","))
            {
                var hasValue = tokens[i + 7].Is(text, GradleTokenKind.Punctuation, ")");
                Add(setKey.GetContent(text), hasValue ? tokens[i + 6] : null);
                continue;
            }

            // name = value, def name = value, val name: String = value, ext.name = value
            if (token.Kind is not GradleTokenKind.Identifier || i + 1 >= tokens.Count)
                continue;

            var valueIndex = i + 1;
            if (isKotlin && i > 0 && tokens[i - 1].GetText(text) is "val" or "var" && valueIndex + 1 < tokens.Count && tokens[valueIndex].Is(text, GradleTokenKind.Punctuation, ":") && tokens[valueIndex + 1].Kind is GradleTokenKind.Identifier)
            {
                valueIndex += 2;
            }

            if (valueIndex >= tokens.Count || !tokens[valueIndex].Is(text, GradleTokenKind.Operator, "="))
                continue;

            // A named argument, such as version = "1.0" in implementation(group = "g", name = "a", version = "1.0"), or the property of another object
            if (i > 0 && tokens[i - 1].GetText(text) is "(" or "," || (i > 1 && tokens[i - 1].GetText(text) is "." && !tokens[i - 2].Is(text, GradleTokenKind.Identifier, "ext")))
                continue;

            Add(token.GetText(text), GetAssignedValue(valueIndex + 1));
        }

        return result;

        GradleToken? GetAssignedValue(int index)
        {
            if (index >= tokens.Count || tokens[index] is not { Kind: GradleTokenKind.String, IsVerbatim: true } value)
                return null;

            // The string must be the whole value, not the start of an expression such as "1.0" + suffix
            if (index + 1 < tokens.Count && (tokens[index + 1].Kind is GradleTokenKind.Operator || tokens[index + 1].GetText(text) is "." or "(" or "[" or "?"))
                return null;

            return value;
        }

        void Add(string name, GradleToken? value)
        {
            if (value is { Kind: GradleTokenKind.String, IsVerbatim: true } literal && literal.ContentEnd > literal.ContentStart)
            {
                result.Add(name, literal.GetContent(text), () =>
                {
                    lineMap ??= new TextLineMap(text);
                    return TextLocation.FromIndex(context.FileSystem, context.FullPath, lineMap, literal.ContentStart, literal.ContentEnd - literal.ContentStart);
                });
            }
            else
            {
                result.Add(name, value: null, () => new NonUpdatableLocation(context));
            }
        }
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

    // group:artifact:version[:classifier][@extension] where only the version interpolates variables, such as $fooVersion or ${fooVersion}
    [GeneratedRegex("""^(?<name>[A-Za-z0-9_.-]+:[A-Za-z0-9_.-]+):(?<version>[^"':@\\]*\$[^"':@\\]*)(?::[^"'$:@\\]+)?(?:@[^"'$\\]+)?$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex GradleInterpolatedCoordinateRegex();

    // group:artifact:version[:classifier][@extension]. Interpolated versions, such as $fooVersion, are not matched.
    [GeneratedRegex("""^(?<name>[A-Za-z0-9_.-]+:[A-Za-z0-9_.-]+):(?<version>[^"'$:@\\]+)(?::[^"'$:@\\]+)?(?:@[^"'$\\]+)?$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex GradleCoordinateRegex();
}
