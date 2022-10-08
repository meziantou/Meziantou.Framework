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
    public static Task<int> Main(string[] args)
    {
        return MainImpl(args, console: null);
    }

    internal static Task<int> MainImpl(string[] args, IConsole? console)
    {
        var rootCommand = new RootCommand("Validate a NuGet package") { Name = "meziantou.validate-nuget-package" }; // Name must match <ToolCommandName> in csproj
        var pathsArgument = new Argument<string[]>("package-path", "Paths to the NuGet packages to validate") { Arity = ArgumentArity.OneOrMore };
        var rulesOptions = new Option<NuGetPackageValidationRule[]?>("--rules", description: GetRulesDescription(), parseArgument: ParseRuleValues);
        var excludedRulesOptions = new Option<NuGetPackageValidationRule[]?>("--excluded-rules", description: GetRulesDescription(), parseArgument: ParseRuleValues);
        var excludedRuleIdsOptions = new Option<int[]?>("--excluded-rule-ids", description: "List of rule ids to exclude from analysis", parseArgument: ParseIntValues);
        rootCommand.AddArgument(pathsArgument);
        rootCommand.AddOption(rulesOptions);
        rootCommand.AddOption(excludedRulesOptions);
        rootCommand.AddOption(excludedRuleIdsOptions);
        rootCommand.SetHandler(async context =>
        {
            var paths = context.ParseResult.GetValueForArgument(pathsArgument);
            var options = new NuGetPackageValidationOptions();

            var includedRules = context.ParseResult.GetValueForOption(rulesOptions);
            if (includedRules == null || includedRules.Length == 0)
            {
                foreach (var rule in NuGetPackageValidationRules.Default)
                {
                    options.Rules.Add(rule);
                }
            }

            var excludedRules = context.ParseResult.GetValueForOption(excludedRulesOptions);
            if (excludedRules != null && excludedRules.Length > 0)
            {
                foreach (var excludedRule in excludedRules)
                {
                    options.Rules.Remove(excludedRule);
                }
            }

            var excludedRuleIds = context.ParseResult.GetValueForOption(excludedRuleIdsOptions);
            if (excludedRuleIds != null && excludedRuleIds.Length > 0)
            {
                foreach (var excludedRuleId in excludedRuleIds)
                {
                    options.ExcludedRuleIds.Add(excludedRuleId);
                }
            }

            var results = new List<NuGetPackageValidationResult>(capacity: paths.Length);
            foreach (var path in paths)
            {
                var packagePath = FullPath.FromPath(path);
                var result = await NuGetPackageValidator.ValidateAsync(packagePath, options, context.GetCancellationToken()).ConfigureAwait(false);
                results.Add(result);
            }

            var json = JsonSerializer.Serialize(results, ResultContext.Default.ListNuGetPackageValidationResult);
            context.Console.WriteLine(json);
            if (results.Any(result => !result.IsValid))
            {
                context.ExitCode = 1;
            }
        });

        return rootCommand.InvokeAsync(args, console);
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

            foreach (var ruleName in token.Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(ruleName, "default", StringComparison.OrdinalIgnoreCase))
                {
                    rules.AddRange(NuGetPackageValidationRules.Default);
                    continue;
                }

                var members = typeof(NuGetPackageValidationRules).GetMember(ruleName, BindingFlags.Public | BindingFlags.Static);
                if (members == null || members.Length != 1)
                {
                    result.ErrorMessage = $"Invalid rule '{ruleName}'";
                    return null;
                }

                if (members[0] is not PropertyInfo property)
                {
                    result.ErrorMessage = $"Invalid rule '{ruleName}'";
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

            foreach (var value in token.Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedValue))
                {
                    result.ErrorMessage = $"Invalid value '{value}'";
                    return null;
                }

                resultValue.Add(parsedValue);
            }
        }

        return resultValue.ToArray();
    }

    [JsonSourceGenerationOptions(
       GenerationMode = JsonSourceGenerationMode.Serialization,
       WriteIndented = true,
       DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonSerializable(typeof(NuGetPackageValidationResult))]
    [JsonSerializable(typeof(List<NuGetPackageValidationResult>))]
    private sealed partial class ResultContext : JsonSerializerContext
    {
    }
}