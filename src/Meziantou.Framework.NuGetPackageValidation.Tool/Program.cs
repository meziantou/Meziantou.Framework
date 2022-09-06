using System.CommandLine;
using System.CommandLine.Parsing;
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
        var rootCommand = new RootCommand();
        var pathArgument = new Argument<string>("package-path", "Path to the NuGet package to validate") { Arity = ArgumentArity.ExactlyOne };
        var rulesOptions = new Option<NuGetPackageValidationRule[]>("--rules", description: GetRulesDescription(), parseArgument: ParseValues);
        rootCommand.AddArgument(pathArgument);
        rootCommand.AddOption(rulesOptions);
        rootCommand.SetHandler(async context =>
        {
            var path = context.ParseResult.GetValueForArgument(pathArgument);
            ICollection<NuGetPackageValidationRule>? rules = context.ParseResult.GetValueForOption(rulesOptions);
            if (rules == null || rules.Count == 0)
            {
                rules = NuGetPackageValidationRules.Default;
            }

            var packagePath = FullPath.FromPath(path);
            var result = await NuGetPackageValidator.ValidateAsync(packagePath, rules, context.GetCancellationToken()).ConfigureAwait(false);
            var json = JsonSerializer.Serialize(result, ResultContext.Default.NuGetPackageValidationResult);
            context.Console.Write(json);
            if (!result.IsValid)
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

    private static NuGetPackageValidationRule[]? ParseValues(ArgumentResult result)
    {
        var rules = new List<NuGetPackageValidationRule>();
        foreach (var token in result.Tokens)
        {
            if (string.IsNullOrEmpty(token.Value))
                continue;

            foreach (var ruleName in token.Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
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

        return rules.ToArray();
    }

    [JsonSourceGenerationOptions(
       GenerationMode = JsonSourceGenerationMode.Serialization,
       WriteIndented = true,
       DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonSerializable(typeof(NuGetPackageValidationResult))]
    private sealed partial class ResultContext : JsonSerializerContext
    {
    }
}