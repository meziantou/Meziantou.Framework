using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

[assembly: InternalsVisibleTo("Meziantou.Framework.NuGetPackageValidation.Tool.Tests")]

namespace Meziantou.Framework.NuGetPackageValidation.Tool;

internal static partial class Program
{
    private static readonly char[] ValueDelimiters = [',', ';'];

    public static Task<int> Main(string[] args)
    {
        return MainImpl(args, configure: null);
    }

    [SuppressMessage("Performance", "CA1869:Cache and reuse 'JsonSerializerOptions' instances", Justification = "Used only once")]
    internal static Task<int> MainImpl(string[] args, Action<CommandLineConfiguration>? configure)
    {
        var rootCommand = new RootCommand(description: "Validate a NuGet package");
        var pathsArgument = new Argument<string[]>("package-path") { Arity = ArgumentArity.OneOrMore, Description = "Paths to the NuGet packages to validate" };
        var rulesOptions = new Option<NuGetPackageValidationRule[]?>("--rules") { CustomParser = ParseRuleValues, Description = GetRulesDescription() };
        var excludedRulesOptions = new Option<NuGetPackageValidationRule[]?>("--excluded-rules") { CustomParser = ParseRuleValues, Description = GetRulesDescription() };
        var excludedRuleIdsOptions = new Option<int[]?>("--excluded-rule-ids") { Description = "List of rule ids to exclude from analysis", CustomParser = ParseIntValues };
        var githubTokenOptions = new Option<string?>("--github-token") { Description = "GitHub token to authenticate requests" };
        var onlyReportErrorsOptions = new Option<bool>("--only-report-errors") { Description = "Only report errors on the output" };
        rootCommand.Arguments.Add(pathsArgument);
        rootCommand.Options.Add(rulesOptions);
        rootCommand.Options.Add(excludedRulesOptions);
        rootCommand.Options.Add(excludedRuleIdsOptions);
        rootCommand.Options.Add(githubTokenOptions);
        rootCommand.Options.Add(onlyReportErrorsOptions);
        rootCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var paths = parseResult.GetValue(pathsArgument);
            var onlyReportErrors = parseResult.GetValue(onlyReportErrorsOptions);
            var options = new NuGetPackageValidationOptions();

            var includedRules = parseResult.GetValue(rulesOptions);
            if (includedRules is null || includedRules.Length == 0)
            {
                foreach (var rule in NuGetPackageValidationRules.Default)
                {
                    options.Rules.Add(rule);
                }
            }

            var excludedRules = parseResult.GetValue(excludedRulesOptions);
            if (excludedRules is not null && excludedRules.Length > 0)
            {
                foreach (var excludedRule in excludedRules)
                {
                    options.Rules.Remove(excludedRule);
                }
            }

            var excludedRuleIds = parseResult.GetValue(excludedRuleIdsOptions);
            if (excludedRuleIds is not null && excludedRuleIds.Length > 0)
            {
                foreach (var excludedRuleId in excludedRuleIds)
                {
                    options.ExcludedRuleIds.Add(excludedRuleId);
                }
            }

            var githubToken = parseResult.GetValue(githubTokenOptions);
            if (!string.IsNullOrEmpty(githubToken))
            {
                options.ConfigureRequest = request =>
                {
                    var host = request.RequestUri?.Host;
                    if (host is null)
                        return;

                    if (host.EndsWith("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", githubToken);
                    }
                };
            }

            var packageResults = new Dictionary<string, NuGetPackageValidationResult>(capacity: paths.Length, StringComparer.Ordinal);
            foreach (var path in paths)
            {
                var packagePath = FullPath.FromPath(path);
                if (packageResults.ContainsKey(packagePath))
                    continue;

                var packageResult = await NuGetPackageValidator.ValidateAsync(packagePath, options, cancellationToken).ConfigureAwait(false);

                if (!packageResult.IsValid || !onlyReportErrors)
                    packageResults.Add(packagePath, packageResult);
            }

            var result = new Result(packageResults);

            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                TypeInfoResolver = ResultContext.Default,
                WriteIndented = true,
            };
            var json = JsonSerializer.Serialize(result, jsonOptions);
            await parseResult.Configuration.Output.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (!result.IsValid)
            {
                return 1;
            }

            return 0;
        });

        var commandLineConfiguration = new CommandLineConfiguration(rootCommand);
        configure?.Invoke(commandLineConfiguration);
        return commandLineConfiguration.InvokeAsync(args);
    }

    private static string GetRulesDescription()
    {
        return "Available rules: " + string.Join(", ", typeof(NuGetPackageValidationRules).GetMembers(BindingFlags.Public | BindingFlags.Static)
            .OfType<PropertyInfo>()
            .Where(m => m.CanRead && m.PropertyType == typeof(NuGetPackageValidationRule))
            .Select(m => m.Name));
    }

    private static NuGetPackageValidationRule[]? ParseRuleValues(ArgumentResult result)
    {
        var rules = new List<NuGetPackageValidationRule>();
        foreach (var token in result.Tokens)
        {
            if (string.IsNullOrEmpty(token.Value))
                continue;

            foreach (var ruleName in token.Value.Split(ValueDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(ruleName, "default", StringComparison.OrdinalIgnoreCase))
                {
                    rules.AddRange(NuGetPackageValidationRules.Default);
                    continue;
                }

                var members = typeof(NuGetPackageValidationRules).GetMember(ruleName, BindingFlags.Public | BindingFlags.Static);
                if (members is null || members.Length != 1)
                {
                    result.AddError($"Invalid rule '{ruleName}'");
                    return null;
                }

                if (members[0] is not PropertyInfo property)
                {
                    result.AddError($"Invalid rule '{ruleName}'");
                    return null;
                }

                rules.Add((NuGetPackageValidationRule)property.GetValue(obj: null)!);
            }
        }

        return rules.Distinct().ToArray();
    }

    private static int[]? ParseIntValues(ArgumentResult result)
    {
        var resultValue = new List<int>();
        foreach (var token in result.Tokens)
        {
            if (string.IsNullOrEmpty(token.Value))
                continue;

            foreach (var value in token.Value.Split(ValueDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedValue))
                {
                    result.AddError($"Invalid value '{value}'");
                    return null;
                }

                resultValue.Add(parsedValue);
            }
        }

        return resultValue.ToArray();
    }

    private sealed class Result(Dictionary<string, NuGetPackageValidationResult> packages)
    {
        public bool IsValid => Packages.All(p => p.Value.IsValid);

        public Dictionary<string, NuGetPackageValidationResult> Packages { get; } = packages ?? throw new ArgumentNullException(nameof(packages));
    }

    [JsonSourceGenerationOptions(
       GenerationMode = JsonSourceGenerationMode.Serialization | JsonSourceGenerationMode.Metadata,
       WriteIndented = true,
       DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonSerializable(typeof(Result))]
    private sealed partial class ResultContext : JsonSerializerContext
    {
    }
}